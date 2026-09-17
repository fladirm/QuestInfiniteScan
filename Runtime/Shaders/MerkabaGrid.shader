Shader "Genesis/RoomScan/MerkabaGrid"
{
    Properties
    {
        _ScanOpacity("Scan Opacity", Range(0,1)) = 1
        // Blend state is material state chosen on the CPU (opaque or real
        // alpha blending), never a per-fragment branch.
        _SrcBlend("Src Blend", Float) = 1
        _DstBlend("Dst Blend", Float) = 0
        _ZWrite("ZWrite", Float) = 1
        // 4 = LEqual (opaque), 3 = Equal (translucent colour pass after the
        // depth-only pass: only the nearest sheet layer blends).
        _ZTest("ZTest", Float) = 4
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
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ XR_LINEAR_DEPTH
            #pragma multi_compile _ XR_HARD_OCCLUSION
            // These features are selected on runtime-created materials.
            // Keep every required Release-player variant; shader_feature
            // variants without a serialized material user may be stripped.
            #pragma multi_compile_local_fragment _ M8_FINE_PREVIEW
            #pragma multi_compile_local_fragment _ M8_ENVIRONMENT_OCCLUSION
            #pragma multi_compile_local_fragment _ M8_ALPHA_BLEND
            #pragma multi_compile_local_fragment _ M8_CHECKER_READOUT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"
            TEXTURE2D_ARRAY_FLOAT(_EnvironmentDepthTexture);
            SAMPLER(sampler_EnvironmentDepthTexture);
            float4x4 _EnvironmentDepthProjectionMatrices[2];
            int _IsOcclusionOn;
            float4x4 _MerkabaGridToWorld;

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
                float3 gridPosition : POSITION;
                half4 packedColor : COLOR;
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
                float3 gridPosition : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 gridPosition = input.gridPosition;
                half3 color = input.packedColor.rgb;
                uint hasRgb = ((uint)round(saturate(input.packedColor.a) *
                    255.0h)) & 1u;
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(gridPosition, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.worldPosition = worldPosition;
                output.gridPosition = gridPosition;
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
                half3 color = input.hasRgb != 0u
                    ? input.color : half3(0.625h, 0.625h, 0.625h);
#if defined(M8_CHECKER_READOUT)
                // Coverage readout: a dark membrane under a thin continuous
                // iridescent film. Hue is a smooth field of the canonical grid
                // position (two low-frequency sinusoids) shifted by the viewing
                // angle; no cell, no parity, no hash, so the film reads as one
                // surface and a hole is a hole.
                float3 faceNormal = normalize(cross(ddy(input.worldPosition),
                    ddx(input.worldPosition)));
                float3 viewDirection = normalize(_WorldSpaceCameraPos -
                    input.worldPosition);
                float fresnel = pow(1.0 - abs(dot(faceNormal, viewDirection)),
                    1.5);
                float phase = dot(input.gridPosition,
                    float3(0.91, 1.27, 0.73)) +
                    0.35 * sin(dot(input.gridPosition,
                        float3(1.61, -0.83, 1.13)));
                float coverageHue = frac(phase * 0.16 + fresnel * 0.60);
                float3 film = 0.5 + 0.5 * cos(6.2831853 *
                    (coverageHue + float3(0.0, 0.333333, 0.666667)));
                color = (half3)saturate(0.10 + film * (0.12 + 0.75 * fresnel));
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
                half alpha = 1.0h;
#if defined(M8_ALPHA_BLEND)
                alpha = (half)_ScanOpacity;
#endif
                return half4(color, alpha);
            }
            ENDHLSL
        }

        // Translucent membrane only: one depth-only pass so the colour pass
        // (ZTest Equal) blends the nearest layer of the double-sided sheet
        // once, never ten stacked layers. Same clip as the colour pass.
        Pass
        {
            Name "MerkabaDepthOnly"
            Tags { "LightMode"="MerkabaDepthOnly" }
            Cull Off
            ColorMask 0
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ XR_LINEAR_DEPTH
            #pragma multi_compile _ XR_HARD_OCCLUSION
            // These features are selected on runtime-created materials.
            // Keep every required Release-player variant; shader_feature
            // variants without a serialized material user may be stripped.
            #pragma multi_compile_local_fragment _ M8_FINE_PREVIEW
            #pragma multi_compile_local_fragment _ M8_ENVIRONMENT_OCCLUSION
            #pragma multi_compile_local_fragment _ M8_ALPHA_BLEND
            #pragma multi_compile_local_fragment _ M8_CHECKER_READOUT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.xr.arfoundation/Assets/Shaders/Utils.hlsl"
            TEXTURE2D_ARRAY_FLOAT(_EnvironmentDepthTexture);
            SAMPLER(sampler_EnvironmentDepthTexture);
            float4x4 _EnvironmentDepthProjectionMatrices[2];
            int _IsOcclusionOn;
            float4x4 _MerkabaGridToWorld;

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
                float3 gridPosition : POSITION;
                half4 packedColor : COLOR;
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
                float3 gridPosition : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 gridPosition = input.gridPosition;
                half3 color = input.packedColor.rgb;
                uint hasRgb = ((uint)round(saturate(input.packedColor.a) *
                    255.0h)) & 1u;
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(gridPosition, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.worldPosition = worldPosition;
                output.gridPosition = gridPosition;
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
                return 0.0h;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
