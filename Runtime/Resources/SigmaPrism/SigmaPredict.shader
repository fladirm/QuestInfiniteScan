Shader "Hidden/Genesis/SigmaPrism/Predict"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SigmaForwardPrediction"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "SigmaCarrierAbi.hlsl"

            StructuredBuffer<float4> _ReadoutVertices;
            StructuredBuffer<uint> _CurrentPageSlots;
            StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
            float4x4 _ClipFromWorld;
            float4x4 _OpticalFromWorld;
            uint _SegmentIndex;

            #define SIGMA_READOUT_EXTENT 65u
            #define SIGMA_READOUT_SAMPLES 4225u
            #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u

            uint SigmaReadoutIndex(uint pageSlot, uint x, uint y)
            {
                return pageSlot * SIGMA_READOUT_SAMPLES +
                    y * SIGMA_READOUT_EXTENT + x;
            }

            uint2 SigmaTriangleCorner(uint triangleIndex, uint corner)
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

            float2 SigmaEncodeOctahedral(float3 normal)
            {
                normal /= max(1e-20,
                    abs(normal.x) + abs(normal.y) + abs(normal.z));
                float2 encoded = normal.xy;
                if (normal.z < 0.0)
                    encoded = (1.0 - abs(encoded.yx)) *
                        float2(encoded.x >= 0.0 ? 1.0 : -1.0,
                            encoded.y >= 0.0 ? 1.0 : -1.0);
                return encoded;
            }

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOptical : TEXCOORD0;
                float3 normalOptical : TEXCOORD1;
                float2 carrierLocal : TEXCOORD2;
                nointerpolation float support : TEXCOORD3;
                nointerpolation uint4 pageCoordinate : TEXCOORD4;
                nointerpolation uint4 stateKey : TEXCOORD5;
            };

            Varyings Vert(uint vertexId : SV_VertexID)
            {
                Varyings output;
                uint activePage = vertexId / SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageVertex = vertexId -
                    activePage * SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageSlot = _CurrentPageSlots[activePage];
                uint primitive = pageVertex / 3u;
                uint corner = pageVertex - primitive * 3u;
                uint triangleIndex = primitive & 1u;
                uint cell = primitive >> 1u;
                uint cellX = cell & 63u;
                uint cellY = cell >> 6u;

                uint2 c0 = SigmaTriangleCorner(triangleIndex, 0u);
                uint2 c1 = SigmaTriangleCorner(triangleIndex, 1u);
                uint2 c2 = SigmaTriangleCorner(triangleIndex, 2u);
                float4 r0 = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
                    cellX + c0.x, cellY + c0.y)];
                float4 r1 = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
                    cellX + c1.x, cellY + c1.y)];
                float4 r2 = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
                    cellX + c2.x, cellY + c2.y)];
                float support = min(r0.w, min(r1.w, r2.w));
                float3 areaVector = cross(r1.xyz - r0.xyz, r2.xyz - r0.xyz);
                float areaSquared = dot(areaVector, areaVector);
                bool valid = support > 0.0 && areaSquared > 1e-20;

                uint2 selectedCorner = SigmaTriangleCorner(triangleIndex, corner);
                float3 position = corner == 0u ? r0.xyz :
                    corner == 1u ? r1.xyz : r2.xyz;
                float3 normal = valid ? areaVector * rsqrt(areaSquared) :
                    float3(0.0, 0.0, 1.0);
                output.positionCS = valid
                    ? mul(_ClipFromWorld, float4(position, 1.0))
                    : float4(0.0, 0.0, 2.0, 1.0);
                output.positionOptical = mul(_OpticalFromWorld,
                    float4(position, 1.0)).xyz;
                output.normalOptical = normalize(mul((float3x3)_OpticalFromWorld,
                    normal));
                output.carrierLocal = float2(cellX + selectedCorner.x,
                    cellY + selectedCorner.y);
                output.support = valid ? support : 0.0;
                SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
                output.pageCoordinate = uint4(metadata.pageXLo, metadata.pageXHi,
                    metadata.pageYLo, metadata.pageYHi);
                output.stateKey = uint4(metadata.generation, metadata.revision,
                    _SegmentIndex, pageSlot);
                return output;
            }

            struct PredictionOutput
            {
                float2 depthSupport : SV_Target0;
                uint4 carrierPage : SV_Target1;
                float4 carrierUvNormal : SV_Target2;
                uint4 stateKey : SV_Target3;
            };

            PredictionOutput Frag(Varyings input)
            {
                if (input.support <= 0.0)
                    discard;
                PredictionOutput output;
                output.depthSupport = float2(length(input.positionOptical),
                    input.support);
                output.carrierPage = input.pageCoordinate;
                output.carrierUvNormal = float4(input.carrierLocal,
                    SigmaEncodeOctahedral(normalize(input.normalOptical)));
                output.stateKey = input.stateKey;
                return output;
            }
            ENDHLSL
        }
    }
}
