Shader "Hidden/Genesis/SigmaPrism/Predict"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SigmaPureEyePrediction"
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
            #include "SigmaGeometryReadout.hlsl"
            #include "SigmaPoseConsume.hlsl"

            StructuredBuffer<SigmaPureEyeReadoutGpu> _ReadoutSamples;
            StructuredBuffer<uint> _CurrentPageSlots;
            StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
            float4x4 _ClipFromWorld;
            float4x4 _OpticalFromWorld;
            uint _SegmentIndex;
            uint _ReadoutEye;
            float _ContactFootprintPixels;

            #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u
            #define SIGMA_CONTACT_VERTICES_PER_SAMPLE 6u

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
                float2 carrierLocal : TEXCOORD1;
                nointerpolation float2 orderSupport : TEXCOORD2;
                nointerpolation uint4 pageCoordinate : TEXCOORD3;
                nointerpolation uint4 stateKey : TEXCOORD4;
            };

            PredictionVaryings ContactVert(uint vertexId : SV_VertexID)
            {
                PredictionVaryings output = (PredictionVaryings)0;
                output.positionCS = float4(0.0, 0.0, 2.0, 1.0);
                uint activePage = vertexId / SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageVertex = vertexId -
                    activePage * SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageSlot = _CurrentPageSlots[activePage];
                uint sample = pageVertex / SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                uint corner = pageVertex - sample * SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                SigmaPureEyeReadoutGpu readout = _ReadoutSamples[
                    pageSlot * SIGMA_PAGE_SAMPLE_COUNT + sample];
                float4 positionSupport = _ReadoutEye == 0u
                    ? readout.leftPositionSupport
                    : readout.rightPositionSupport;
                float4 colourOrder = _ReadoutEye == 0u
                    ? readout.leftColourOrder
                    : readout.rightColourOrder;
                bool valid = sample < SIGMA_PAGE_SAMPLE_COUNT &&
                    positionSupport.w > 0.0 && colourOrder.w > 0.0;
                float3 position = SigmaPoseUnapplyWorld(positionSupport.xyz);
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
                output.carrierLocal = float2(sample & 63u, sample >> 6u);
                output.orderSupport = valid
                    ? float2(colourOrder.w, positionSupport.w) : 0.0;
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
                if (input.orderSupport.y <= 0.0)
                    discard;
                PredictionOutput output;
                output.depthSupport = input.orderSupport;
                output.carrierPage = input.pageCoordinate;
                output.carrierUvNormal = float4(input.carrierLocal,
                    SigmaEncodeOctahedral(normalize(-input.positionOptical)));
                output.stateKey = input.stateKey;
                return output;
            }
            ENDHLSL
        }
    }
}
