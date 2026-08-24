Shader "Hidden/Genesis/SigmaPrism/Predict"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SigmaContactFootprintPrediction"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ContactVert
            #pragma fragment PredictionFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SigmaCarrierAbi.hlsl"
            #include "SigmaPoseConsume.hlsl"

            StructuredBuffer<float4> _ReadoutVertices;
            StructuredBuffer<uint> _CurrentPageSlots;
            StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
            float4x4 _ClipFromWorld;
            float4x4 _OpticalFromWorld;
            uint _SegmentIndex;
            float _ContactFootprintPixels;

            #define SIGMA_READOUT_EXTENT 65u
            #define SIGMA_READOUT_SAMPLES 4225u
            #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u
            #define SIGMA_CONTACT_VERTICES_PER_SAMPLE 6u

            uint SigmaReadoutIndex(uint pageSlot, uint x, uint y)
            {
                return pageSlot * SIGMA_READOUT_SAMPLES +
                    y * SIGMA_READOUT_EXTENT + x;
            }

            float2 SigmaBillboardCorner(uint corner)
            {
                if (corner == 0u) return float2(-1.0, -1.0);
                if (corner == 1u) return float2(-1.0, 1.0);
                if (corner == 2u) return float2(1.0, -1.0);
                if (corner == 3u) return float2(1.0, -1.0);
                if (corner == 4u) return float2(-1.0, 1.0);
                return float2(1.0, 1.0);
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

            struct PredictionVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOptical : TEXCOORD0;
                float3 normalOptical : TEXCOORD1;
                float2 carrierLocal : TEXCOORD2;
                nointerpolation float support : TEXCOORD3;
                nointerpolation uint4 pageCoordinate : TEXCOORD4;
                nointerpolation uint4 stateKey : TEXCOORD5;
            };

            PredictionVaryings SigmaEmptyPrediction()
            {
                PredictionVaryings output;
                output.positionCS = float4(0.0, 0.0, 2.0, 1.0);
                output.positionOptical = 0.0;
                output.normalOptical = float3(0.0, 0.0, 1.0);
                output.carrierLocal = 0.0;
                output.support = 0.0;
                output.pageCoordinate = 0u;
                output.stateKey = 0u;
                return output;
            }

            float3 SigmaContactNormal(uint pageSlot, uint x, uint y,
                float3 centreWorld, float3 centreOptical)
            {
                float4 horizontal = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
                    x + 1u, y)];
                float4 vertical = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
                    x, y + 1u)];
                float3 normal = cross(horizontal.xyz - centreWorld,
                    vertical.xyz - centreWorld);
                float lengthSquared = dot(normal, normal);
                if (horizontal.w > 0.0 && vertical.w > 0.0 &&
                    lengthSquared > 1e-20)
                {
                    normal *= rsqrt(lengthSquared);
                    return normalize(mul((float3x3)_OpticalFromWorld,
                        SigmaPoseUnapplyVectorWorld(normal)));
                }
                return normalize(-centreOptical);
            }

            PredictionVaryings ContactVert(uint vertexId : SV_VertexID)
            {
                PredictionVaryings output = SigmaEmptyPrediction();
                uint activePage = vertexId / SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageVertex = vertexId -
                    activePage * SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageSlot = _CurrentPageSlots[activePage];
                uint sample = pageVertex / SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                uint corner = pageVertex - sample * SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                uint x = sample & 63u;
                uint y = sample >> 6u;
                float4 readout = _ReadoutVertices[SigmaReadoutIndex(pageSlot, x, y)];
                bool valid = sample < SIGMA_PAGE_SAMPLE_COUNT && readout.w > 0.0;
                float3 position = SigmaPoseUnapplyWorld(readout.xyz);
                float3 optical = mul(_OpticalFromWorld,
                    float4(position, 1.0)).xyz;
                float4 clip = valid
                    ? mul(_ClipFromWorld, float4(position, 1.0))
                    : output.positionCS;
                float2 screen = max(_ScreenParams.xy, float2(1.0, 1.0));
                clip.xy += SigmaBillboardCorner(corner) *
                    (2.0 * max(_ContactFootprintPixels, 0.75) / screen) * clip.w;

                SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
                output.positionCS = clip;
                output.positionOptical = optical;
                output.normalOptical = SigmaContactNormal(pageSlot, x, y,
                    readout.xyz, optical);
                output.carrierLocal = float2(x, y);
                output.support = valid ? readout.w : 0.0;
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

            PredictionOutput PredictionFrag(PredictionVaryings input)
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
