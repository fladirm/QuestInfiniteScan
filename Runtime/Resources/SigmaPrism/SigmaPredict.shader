Shader "Hidden/Genesis/SigmaPrism/Predict"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #pragma target 4.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "SigmaCarrierAbi.hlsl"
        #include "SigmaTopologyAbi.hlsl"
        #include "SigmaPoseConsume.hlsl"

        StructuredBuffer<float4> _ReadoutVertices;
        StructuredBuffer<uint> _CurrentPageSlots;
        StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
        StructuredBuffer<uint> _TopologyCellFlags;
        StructuredBuffer<uint4> _TopologyPageKeys;
        float4x4 _ClipFromWorld;
        float4x4 _OpticalFromWorld;
        uint _SegmentIndex;
        float _ContactFootprintPixels;

        #define SIGMA_READOUT_EXTENT 65u
        #define SIGMA_READOUT_SAMPLES 4225u
        #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u
        #define SIGMA_CONTACT_SAMPLES_PER_PAGE 4096u
        #define SIGMA_CONTACT_VERTICES_PER_SAMPLE 6u

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

        float2 SigmaContactBillboardCorner(uint corner)
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

        void SigmaStorePredictionIdentity(inout PredictionVaryings output,
            SigmaCarrierPageMetaGpu metadata, uint pageSlot,
            float2 carrierLocal, float support)
        {
            output.carrierLocal = carrierLocal;
            output.support = support;
            output.pageCoordinate = uint4(metadata.pageXLo, metadata.pageXHi,
                metadata.pageYLo, metadata.pageYHi);
            output.stateKey = uint4(metadata.generation, metadata.revision,
                _SegmentIndex, pageSlot);
        }

        PredictionVaryings SurfaceVert(uint vertexId : SV_VertexID)
        {
            PredictionVaryings output = SigmaEmptyPrediction();
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
            SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
            uint4 topologyKey = _TopologyPageKeys[pageSlot];
            uint cellTopology = _TopologyCellFlags[
                pageSlot * SIGMA_PAGE_SAMPLE_COUNT + cell];
            uint triangleCut = triangleIndex == 0u
                ? SIGMA_TOPOLOGY_TRIANGLE_0_CUT
                : SIGMA_TOPOLOGY_TRIANGLE_1_CUT;
            bool topologyCurrent = topologyKey.x == metadata.generation &&
                topologyKey.y == metadata.revision && topologyKey.z == 1u;

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
            bool valid = topologyCurrent &&
                (cellTopology & triangleCut) == 0u &&
                support > 0.0 && areaSquared > 1e-20;

            uint2 selectedCorner = SigmaTriangleCorner(triangleIndex, corner);
            float3 position = corner == 0u ? r0.xyz :
                corner == 1u ? r1.xyz : r2.xyz;
            float3 normal = valid ? areaVector * rsqrt(areaSquared) :
                float3(0.0, 0.0, 1.0);
            position = SigmaPoseUnapplyWorld(position);
            normal = SigmaPoseUnapplyVectorWorld(normal);
            output.positionCS = valid
                ? mul(_ClipFromWorld, float4(position, 1.0))
                : output.positionCS;
            output.positionOptical = mul(_OpticalFromWorld,
                float4(position, 1.0)).xyz;
            output.normalOptical = normalize(mul((float3x3)_OpticalFromWorld,
                normal));
            SigmaStorePredictionIdentity(output, metadata, pageSlot,
                float2(cellX + selectedCorner.x, cellY + selectedCorner.y),
                valid ? support : 0.0);
            return output;
        }

        float3 SigmaContactNormalOptical(uint pageSlot, uint x, uint y,
            float3 centreWorld, float3 centreOptical)
        {
            float4 horizontal = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
                x + 1u, y)];
            float4 vertical = _ReadoutVertices[SigmaReadoutIndex(pageSlot,
                x, y + 1u)];
            float3 tangentU = horizontal.xyz - centreWorld;
            float3 tangentV = vertical.xyz - centreWorld;
            float3 normalWorld = cross(tangentU, tangentV);
            float normalSquared = dot(normalWorld, normalWorld);
            if (horizontal.w > 0.0 && vertical.w > 0.0 &&
                normalSquared > 1e-20)
            {
                normalWorld *= rsqrt(normalSquared);
                return normalize(mul((float3x3)_OpticalFromWorld,
                    SigmaPoseUnapplyVectorWorld(normalWorld)));
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
            uint corner = pageVertex -
                sample * SIGMA_CONTACT_VERTICES_PER_SAMPLE;
            uint x = sample & 63u;
            uint y = sample >> 6u;
            float4 readout = _ReadoutVertices[SigmaReadoutIndex(pageSlot, x, y)];
            bool valid = sample < SIGMA_CONTACT_SAMPLES_PER_PAGE &&
                readout.w > 0.0;
            float3 position = SigmaPoseUnapplyWorld(readout.xyz);
            float3 positionOptical = mul(_OpticalFromWorld,
                float4(position, 1.0)).xyz;
            float4 clip = valid
                ? mul(_ClipFromWorld, float4(position, 1.0))
                : output.positionCS;
            float2 screenSize = max(_ScreenParams.xy, float2(1.0, 1.0));
            clip.xy += SigmaContactBillboardCorner(corner) *
                (2.0 * max(_ContactFootprintPixels, 0.75) / screenSize) * clip.w;

            SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
            output.positionCS = clip;
            output.positionOptical = positionOptical;
            output.normalOptical = SigmaContactNormalOptical(pageSlot, x, y,
                readout.xyz, positionOptical);
            SigmaStorePredictionIdentity(output, metadata, pageSlot,
                float2(x, y), valid ? readout.w : 0.0);
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

        Pass
        {
            Name "SigmaForwardPrediction"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex SurfaceVert
            #pragma fragment PredictionFrag
            ENDHLSL
        }

        Pass
        {
            Name "SigmaContactFootprintPrediction"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex ContactVert
            #pragma fragment PredictionFrag
            ENDHLSL
        }
    }
}
