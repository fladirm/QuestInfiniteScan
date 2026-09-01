Shader "Genesis/RoomScan/MerkabaGrid"
{
    Properties
    {
        _ScanOpacity("Scan Opacity", Range(0,1)) = 1
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 10
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
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
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ XR_LINEAR_DEPTH
            #pragma multi_compile _ XR_HARD_OCCLUSION
            #pragma shader_feature_local_vertex _ M8_STEREO_MESH
            #pragma shader_feature_local_fragment _ M8_FINE_PREVIEW
            #pragma shader_feature_local_fragment _ M8_ENVIRONMENT_OCCLUSION
            #pragma multi_compile_local_fragment _ M8_CHECKER_READOUT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"
            TEXTURE2D_ARRAY_FLOAT(_EnvironmentDepthTexture);
            SAMPLER(sampler_EnvironmentDepthTexture);
            float4x4 _EnvironmentDepthProjectionMatrices[2];
            int _IsOcclusionOn;

            float M8EnvironmentVisibility(float3 worldPosition)
            {
#if defined(M8_ENVIRONMENT_OCCLUSION) && defined(XR_HARD_OCCLUSION)
                if (_IsOcclusionOn == 0)
                    return 1.0;
                float4 depthPosition = mul(
                    _EnvironmentDepthProjectionMatrices[unity_StereoEyeIndex],
                    float4(worldPosition, 1.0));
                float2 uv = (depthPosition.xy / depthPosition.w + 1.0) * 0.5;
                if (all(uv < 0.0) || all(uv > 1.0))
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

            struct MerkabaReadoutVertex
            {
                float3 gridPosition;
                uint packedColor;
            };

            StructuredBuffer<MerkabaReadoutVertex> _M8ReadoutVertices0;
            StructuredBuffer<MerkabaReadoutVertex> _M8ReadoutVertices1;
            float4x4 _MerkabaGridToWorld;

            CBUFFER_START(UnityPerMaterial)
                half _ScanOpacity;
                float4 _FineEyeOrigin;
                float4 _FineBrushAxis;
                float4 _FineBrushParams;
                half4 _FinePreviewColor;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
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
                MerkabaReadoutVertex vertex;
#if defined(M8_STEREO_MESH)
                vertex = unity_StereoEyeIndex == 0
                    ? _M8ReadoutVertices0[input.vertexID]
                    : _M8ReadoutVertices1[input.vertexID];
#else
                if (input.vertexID < 6291456u)
                    vertex = _M8ReadoutVertices0[input.vertexID];
                else
                    vertex = _M8ReadoutVertices1[
                        input.vertexID - 6291456u];
#endif
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(vertex.gridPosition, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.worldPosition = worldPosition;
                uint rgb = vertex.packedColor & 0x00ffffffu;
                output.color = half3(rgb & 255u, (rgb >> 8u) & 255u,
                    (rgb >> 16u) & 255u) / 255.0h;
                output.hasRgb = (vertex.packedColor >> 24u) & 1u;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
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
                    : half3(0.0h, 0.0h, 0.0h);
#endif
#if defined(M8_FINE_PREVIEW)
                if (_FineBrushParams.x > 0.5)
                {
                    float3 relative = input.worldPosition -
                        _FineEyeOrigin.xyz;
                    float distanceSquared = dot(relative, relative);
                    float axial = dot(relative, _FineBrushAxis.xyz);
                    bool inside = distanceSquared <= _FineBrushParams.z &&
                        axial >= 0.0 && axial * axial >=
                        distanceSquared * _FineBrushParams.y;
                    if (inside)
                    {
                        float cosineSquared = distanceSquared > 1.0e-12
                            ? axial * axial / distanceSquared : 1.0;
                        half brushWeight = (half)saturate(
                            (cosineSquared - _FineBrushParams.y) /
                            max(1.0e-6, 1.0 - _FineBrushParams.y));
                        color = lerp(color, _FinePreviewColor.rgb,
                            _FinePreviewColor.a * brushWeight);
                    }
                }
#endif
                half alpha = _ScanOpacity *
                    M8EnvironmentVisibility(input.worldPosition);
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
