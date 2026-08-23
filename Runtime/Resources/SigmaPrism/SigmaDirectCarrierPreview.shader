Shader "Hidden/Genesis/SigmaPrism/DirectCarrierPreview"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "DirectCarrierSurface"
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma vertex PreviewSurfaceVert
            #pragma fragment PreviewSurfaceColour

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SigmaCarrierAbi.hlsl"

            StructuredBuffer<float4> _ReadoutVertices;
            StructuredBuffer<uint> _CurrentPageSlots;
            StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
            float4x4 _WorldFromReadout;
            uint _SegmentIndex;
            float _PreviewWireframe;
            float _PreviewContactPixels;
            float _PreviewMaxEdgeMeters;

            #define SIGMA_READOUT_EXTENT 65u
            #define SIGMA_READOUT_SAMPLES 4225u
            #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u

            uint SigmaPreviewReadoutIndex(uint pageSlot, uint x, uint y)
            {
                return pageSlot * SIGMA_READOUT_SAMPLES +
                    y * SIGMA_READOUT_EXTENT + x;
            }

            uint2 SigmaPreviewTriangleCorner(uint triangleIndex, uint corner)
            {
                if (triangleIndex == 0u)
                {
                    if (corner == 0u) return uint2(0u, 0u);
                    if (corner == 1u) return uint2(1u, 0u);
                    return uint2(0u, 1u);
                }
                if (corner == 0u) return uint2(1u, 0u);
                if (corner == 1u) return uint2(1u, 1u);
                return uint2(0u, 1u);
            }

            float3 SigmaPreviewBarycentric(uint corner)
            {
                if (corner == 0u) return float3(1.0, 0.0, 0.0);
                if (corner == 1u) return float3(0.0, 1.0, 0.0);
                return float3(0.0, 0.0, 1.0);
            }

            float2 SigmaPreviewFallbackCorner(uint corner)
            {
                if (corner == 0u) return float2(-0.8660254, -0.5);
                if (corner == 1u) return float2(0.0, 1.0);
                return float2(0.8660254, -0.5);
            }

            struct Attributes
            {
                uint vertexId : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 barycentric : TEXCOORD0;
                float2 fallbackCorner : TEXCOORD1;
                nointerpolation float support : TEXCOORD2;
                nointerpolation float surface : TEXCOORD3;
                nointerpolation uint pageHash : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings PreviewSurfaceVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                uint activePage = input.vertexId /
                    SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageVertex = input.vertexId -
                    activePage * SIGMA_READOUT_VERTICES_PER_PAGE;
                uint primitive = pageVertex / 3u;
                uint corner = pageVertex - primitive * 3u;
                uint triangleIndex = primitive & 1u;
                uint cell = primitive >> 1u;
                uint cellX = cell & 63u;
                uint cellY = cell >> 6u;
                uint pageSlot = _CurrentPageSlots[activePage];

                uint2 c0 = SigmaPreviewTriangleCorner(triangleIndex, 0u);
                uint2 c1 = SigmaPreviewTriangleCorner(triangleIndex, 1u);
                uint2 c2 = SigmaPreviewTriangleCorner(triangleIndex, 2u);
                float4 r0 = _ReadoutVertices[SigmaPreviewReadoutIndex(pageSlot,
                    cellX + c0.x, cellY + c0.y)];
                float4 r1 = _ReadoutVertices[SigmaPreviewReadoutIndex(pageSlot,
                    cellX + c1.x, cellY + c1.y)];
                float4 r2 = _ReadoutVertices[SigmaPreviewReadoutIndex(pageSlot,
                    cellX + c2.x, cellY + c2.y)];

                float support = min(r0.w, min(r1.w, r2.w));
                float3 edge01 = r1.xyz - r0.xyz;
                float3 edge02 = r2.xyz - r0.xyz;
                float3 edge12 = r2.xyz - r1.xyz;
                float areaSquared = dot(cross(edge01, edge02),
                    cross(edge01, edge02));
                float maxEdgeSquared = _PreviewMaxEdgeMeters *
                    _PreviewMaxEdgeMeters;
                bool surface = support > 0.0 && areaSquared > 1e-20 &&
                    dot(edge01, edge01) <= maxEdgeSquared &&
                    dot(edge02, edge02) <= maxEdgeSquared &&
                    dot(edge12, edge12) <= maxEdgeSquared;

                float4 selected = corner == 0u ? r0 :
                    corner == 1u ? r1 : r2;
                float4 fallback = r0.w > 0.0 ? r0 :
                    r1.w > 0.0 ? r1 : r2;
                bool fallbackValid = fallback.w > 0.0;
                float3 readoutPosition = surface ? selected.xyz : fallback.xyz;
                float3 worldPosition = mul(_WorldFromReadout,
                    float4(readoutPosition, 1.0)).xyz;
                float4 clip = (surface || fallbackValid)
                    ? TransformWorldToHClip(worldPosition)
                    : float4(0.0, 0.0, 2.0, 1.0);
                float2 fallbackCorner = SigmaPreviewFallbackCorner(corner);
                if (!surface && fallbackValid)
                {
                    float2 screenSize = max(_ScreenParams.xy,
                        float2(1.0, 1.0));
                    clip.xy += fallbackCorner *
                        (2.0 * max(_PreviewContactPixels, 1.0) / screenSize) *
                        clip.w;
                }

                SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
                output.positionCS = clip;
                output.barycentric = SigmaPreviewBarycentric(corner);
                output.fallbackCorner = fallbackCorner;
                output.support = surface ? support :
                    fallbackValid ? fallback.w : 0.0;
                output.surface = surface ? 1.0 : 0.0;
                output.pageHash = metadata.pageXLo * 1664525u ^
                    metadata.pageYLo * 1013904223u ^
                    _SegmentIndex * 2246822519u;
                return output;
            }

            half4 PreviewSurfaceColour(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (input.support <= 0.0)
                    discard;

                float hashPhase = (input.pageHash & 255u) / 255.0;
                half3 carrierColour = lerp(half3(1.0, 0.02, 0.72),
                    half3(0.62, 0.08, 1.0), hashPhase * 0.35);
                if (input.surface < 0.5)
                    return half4(carrierColour, 0.82h);

                float edgeDistance = min(input.barycentric.x,
                    min(input.barycentric.y, input.barycentric.z));
                float edgeWidth = max(fwidth(edgeDistance), 1e-5);
                float edge = 1.0 - smoothstep(edgeWidth, edgeWidth * 2.25,
                    edgeDistance);
                if (_PreviewWireframe > 0.5)
                {
                    if (edge <= 0.01)
                        discard;
                    return half4(half3(1.0, 0.72, 0.04),
                        (half)(0.28 + edge * 0.67));
                }

                half3 colour = lerp(carrierColour,
                    half3(1.0, 0.42, 0.88), (half)(edge * 0.35));
                return half4(colour, (half)(0.24 + edge * 0.18));
            }
            ENDHLSL
        }
    }
}
