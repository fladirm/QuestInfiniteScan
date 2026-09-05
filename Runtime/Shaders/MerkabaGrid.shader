Shader "Genesis/RoomScan/MerkabaGrid"
{
    Properties
    {
        _ScanOpacity("Scan Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "MerkabaForward"
            Tags { "LightMode"="UniversalForward" }
            // The readout emits one zero-thickness canonical sheet, never a
            // second UNKNOWN-side surface. Both physical sides of that one
            // sheet therefore use the same disposable vertex stream.
            Cull Off
            Blend One Zero
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ XR_LINEAR_DEPTH
            #pragma multi_compile _ XR_HARD_OCCLUSION
            // These four features are selected on runtime-created materials.
            // Keep every required Release-player variant; shader_feature
            // variants without a serialized material user may be stripped.
            #pragma multi_compile_local_vertex _ M8_STEREO_MESH
            #pragma multi_compile_local_fragment _ M8_FINE_PREVIEW
            #pragma multi_compile_local_fragment _ M8_ENVIRONMENT_OCCLUSION
            #pragma multi_compile_local_fragment _ M8_ALPHA_COVERAGE
            #pragma multi_compile_local_fragment _ M8_CHECKER_READOUT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"
            TEXTURE2D_ARRAY_FLOAT(_EnvironmentDepthTexture);
            SAMPLER(sampler_EnvironmentDepthTexture);
            float4x4 _EnvironmentDepthProjectionMatrices[2];
            int _IsOcclusionOn;
            float4x4 _MerkabaGridToWorld;

            struct MerkabaReadoutVertex
            {
                float3 gridPosition;
                uint packedColor;
            };

            StructuredBuffer<MerkabaReadoutVertex> _M8ReadoutVertices0;
            StructuredBuffer<MerkabaReadoutVertex> _M8ReadoutVertices1;

            float M8EnvironmentVisibility(float3 worldPosition)
            {
#if defined(M8_ENVIRONMENT_OCCLUSION) && defined(XR_HARD_OCCLUSION)
                if (_IsOcclusionOn == 0)
                    return 1.0;
                float4 depthPosition = mul(
                    _EnvironmentDepthProjectionMatrices[unity_StereoEyeIndex],
                    float4(worldPosition, 1.0));
                float2 uv = (depthPosition.xy / depthPosition.w + 1.0) * 0.5;
                if (any(uv < 0.0) || any(uv > 1.0))
                    return 1.0;
                float environmentDepth = SAMPLE_TEXTURE2D_ARRAY(
                    _EnvironmentDepthTexture, sampler_EnvironmentDepthTexture,
                    uv, unity_StereoEyeIndex).r;
#if defined(XR_LINEAR_DEPTH)
                float linearEnvironmentDepth = environmentDepth;
#else
                float linearEnvironmentDepth = LinearizeDepth(
                    ConvertDepthToSymmetricRange(environmentDepth));
#endif
                float linearSceneDepth = LinearizeDepth(
                    depthPosition.z / depthPosition.w);
                return linearEnvironmentDepth > linearSceneDepth
                    ? 1.0 : 0.0;
#else
                return 1.0;
#endif
            }

            CBUFFER_START(UnityPerMaterial)
                half _ScanOpacity;
                float4 _FineCursorPosition;
                float4 _FineBrushAxis;
                float4 _FineBrushParams;
                half4 _FinePreviewColor;
            CBUFFER_END

            struct Attributes
            {
#if defined(M8_STEREO_MESH)
                float3 gridPosition0 : POSITION;
                half4 packedColor0 : COLOR;
                float3 gridPosition1 : TEXCOORD0;
                half4 packedColor1 : TEXCOORD1;
#else
                uint vertexID : SV_VertexID;
#endif
#if UNITY_ANY_INSTANCING_ENABLED
                UNITY_VERTEX_INPUT_INSTANCE_ID
#else
                uint proceduralInstanceID : SV_InstanceID;
#endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
                nointerpolation uint hasRgb : TEXCOORD1;
                float3 worldPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
#if defined(M8_STEREO_MESH)
                float3 gridPosition = input.gridPosition1;
                half4 packedColor = input.packedColor1;
                if (unity_StereoEyeIndex == 0)
                {
                    gridPosition = input.gridPosition0;
                    packedColor = input.packedColor0;
                }
                half3 color = packedColor.rgb;
                uint hasRgb = ((uint)round(saturate(packedColor.a) *
                    255.0h)) & 1u;
#else
                MerkabaReadoutVertex vertex;
                if (input.vertexID < 6291456u)
                    vertex = _M8ReadoutVertices0[input.vertexID];
                else
                    vertex = _M8ReadoutVertices1[
                        input.vertexID - 6291456u];
                float3 gridPosition = vertex.gridPosition;
                uint rgb = vertex.packedColor & 0x00ffffffu;
                half3 color = half3(rgb & 255u, (rgb >> 8u) & 255u,
                    (rgb >> 16u) & 255u) / 255.0h;
                uint hasRgb = (vertex.packedColor >> 24u) & 1u;
#endif
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(gridPosition, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.worldPosition = worldPosition;
                output.color = color;
                output.hasRgb = hasRgb;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#if defined(M8_ENVIRONMENT_OCCLUSION) && defined(XR_HARD_OCCLUSION)
                clip(M8EnvironmentVisibility(input.worldPosition) - 0.5);
#endif
#if defined(M8_ALPHA_COVERAGE)
                uint2 coveragePixel = (uint2)input.positionCS.xy;
                uint coverageHash = coveragePixel.x * 0x8da6b343u ^
                    coveragePixel.y * 0xd8163841u;
                coverageHash ^= coverageHash >> 13u;
                coverageHash *= 0x85ebca6bu;
                coverageHash ^= coverageHash >> 16u;
                half coverageThreshold =
                    (half)((coverageHash & 255u) + 0.5) / 256.0h;
                clip(_ScanOpacity - coverageThreshold);
#endif
                half3 color = input.hasRgb != 0u
                    ? input.color : half3(0.55h, 0.16h, 0.42h);
#if defined(M8_CHECKER_READOUT)
                float3 surfaceAxis = abs(cross(ddx(input.worldPosition),
                    ddy(input.worldPosition)));
                float2 checkerPosition = surfaceAxis.x >= surfaceAxis.y &&
                    surfaceAxis.x >= surfaceAxis.z
                    ? input.worldPosition.yz
                    : surfaceAxis.y >= surfaceAxis.z
                        ? input.worldPosition.xz : input.worldPosition.xy;
                int2 checkerCell = (int2)floor(checkerPosition / 0.05);
                uint checkerParity = (uint(checkerCell.x) ^
                    uint(checkerCell.y)) & 1u;
                color = checkerParity == 0u
                    ? half3(1.0h, 1.0h, 0.0h)
                    : half3(1.0h, 0.0h, 1.0h);
#endif
#if defined(M8_FINE_PREVIEW)
                if (_FineBrushParams.x > 0.5)
                {
                    float3 relative = input.worldPosition -
                        _FineCursorPosition.xyz;
                    float axial = dot(relative, _FineBrushAxis.xyz);
                    float3 radial = relative -
                        _FineBrushAxis.xyz * axial;
                    bool inside = axial >= 0.0 &&
                        axial <= _FineBrushParams.z &&
                        dot(radial, radial) <= _FineBrushParams.y;
                    if (inside)
                        color = lerp(color, _FinePreviewColor.rgb,
                            _FinePreviewColor.a);
                }
#endif
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
