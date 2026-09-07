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
            // Captured sheets retain two-sided presentation. Direct/complete
            // provenance and DIRT remain separate fields of the same symbols.
            Cull Off
            Blend One Zero
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            // Unity 6000.5 supports native DXC -> SPIR-V for Vulkan. Keep
            // generated constant-table indexing and bounded shared-knot loops
            // out of the legacy FXC -> HLSLcc constant-array expansion path.
            // This changes the compiler backend, not precise root arithmetic.
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ XR_LINEAR_DEPTH
            #pragma multi_compile _ XR_HARD_OCCLUSION
            // These four features are selected on runtime-created materials.
            // Keep every required Release-player variant; shader_feature
            // variants without a serialized material user may be stripped.
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
            float4x4 _MerkabaWorldToGrid;

            #include "MerkabaFlowerVertex.hlsl"

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
                float3 worldPosition : TEXCOORD0;
                float2 chart : TEXCOORD1;
                nointerpolation uint4 symbol : TEXCOORD2;
                nointerpolation uint packedColor : TEXCOORD3;
                nointerpolation uint parentEpoch : TEXCOORD4;
                nointerpolation uint planeFlags : TEXCOORD5;
                noperspective float valid : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
#if UNITY_ANY_INSTANCING_ENABLED
                uint pageSlot = unity_InstanceID;
#else
                uint pageSlot = input.proceduralInstanceID;
#endif
                M8FlowerGraphicsVertex vertex;
                if (!M8FlowerReadGraphicsVertex(input.vertexID, pageSlot, vertex))
                    return output;
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(vertex.GridPosition, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.worldPosition = worldPosition;
                output.chart = vertex.Chart;
                output.symbol = uint4(vertex.Symbol.OwnerAndCarrier,
                    vertex.Symbol.RootsAndWedges, vertex.Symbol.DetailRef,
                    vertex.Symbol.ThreadRef);
                output.packedColor = vertex.PackedColor;
                output.parentEpoch = vertex.ParentEpoch;
                output.planeFlags = vertex.PlaneFlags;
                output.valid = 1.0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Take derivatives before divergence/discard. They invert the
                // same L2 raster interpolant, never a sampled metric V field.
                float3 dpdx = ddx(input.worldPosition);
                float3 dpdy = ddy(input.worldPosition);
                float2 duvdx = ddx(input.chart);
                float2 duvdy = ddy(input.chart);
                clip(input.valid - 1.0);
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
                M8FlowerSymbolRecord symbol;
                symbol.OwnerAndCarrier = input.symbol.x;
                symbol.RootsAndWedges = input.symbol.y;
                symbol.DetailRef = input.symbol.z;
                symbol.ThreadRef = input.symbol.w;
                float3 color;
                if (M8FlowerDrawIsDirt(symbol))
                {
                    color = M8FlowerDirtSupportLinearRgba.rgb;
                }
                else
                {
                    uint wedge;
                    float3 barycentric, chartU, chartV;
                    if (!M8FlowerCarrierChartWedge(input.chart, wedge,
                        barycentric, chartU, chartV)) clip(-1.0);
                    // The six index triangles share seven VS outputs. Only
                    // this per-fragment mask can reject an arbitrary subset
                    // without moving a shared boundary position.
                    if ((M8FlowerDrawActiveWedgeMask(symbol) & (1u << wedge)) == 0u)
                        clip(-1.0);
                    float3 tangentU, tangentV, normal, barycentricU, barycentricV;
                    bool frameValid = M8FlowerGraphicsFrame(input.worldPosition,
                        input.chart, wedge, dpdx, dpdy, duvdx, duvdy,
                        tangentU, tangentV, normal, barycentricU, barycentricV);
                    if (!frameValid)
                    {
                        float3 parentNormal; float parentOffset;
                        M8FlowerUnpackPlane(input.planeFlags, parentNormal, parentOffset);
                        normal = normalize(mul(parentNormal,
                            (float3x3)_MerkabaWorldToGrid));
                        barycentricU = barycentricV = 0.0;
                    }
                    if ((M8FlowerDrawReverseWedgeMask(symbol) & (1u << wedge)) != 0u)
                    {
                        normal = -normal;
                        tangentV = -tangentV;
                        barycentricV = -barycentricV;
                    }
                    M8FlowerSkinLocality locality = M8FlowerResolveSkinLocality(
                        barycentric, wedge, barycentricU, barycentricV);
                    M8FlowerSkinDrawSample sample;
                    if (!M8FlowerReadGraphicsSkin(symbol, input.parentEpoch,
                        wedge, locality, M8FlowerCapturedColor(input.packedColor), sample))
                        clip(-1.0);
                    float metricV = 0.0;
                    float3 microNormal = normal;
                    if (frameValid)
                        microNormal = M8FlowerEvaluateSkinNormal(locality, sample,
                            tangentU, tangentV, normal, metricV);
                    // The certified optical-response consumer is separate from
                    // reconstruction. Uncertified capture is never relit as albedo.
                    color = sample.CapturedRgb;
                }
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
