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

        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "SigmaCarrierAbi.hlsl"
        #include "SigmaTopologyAbi.hlsl"

        StructuredBuffer<float4> _ReadoutVertices;
        StructuredBuffer<uint> _CurrentPageSlots;
        StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
        StructuredBuffer<uint> _TopologyCellFlags;
        StructuredBuffer<uint4> _TopologyPageKeys;
        uint _SegmentIndex;
        float _PreviewWireframe;
        float _PreviewAlpha;
        float _PreviewWireThickness;

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

        struct Attributes
        {
            uint vertexId : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 barycentric : TEXCOORD0;
            nointerpolation float support : TEXCOORD1;
            nointerpolation uint pageHash : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings PreviewVert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            uint activePage = input.vertexId /
                SIGMA_READOUT_VERTICES_PER_PAGE;
            uint pageVertex = input.vertexId -
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
            float3 areaVector = cross(r1.xyz - r0.xyz, r2.xyz - r0.xyz);
            bool valid = topologyCurrent &&
                (cellTopology & triangleCut) == 0u && support > 0.0 &&
                dot(areaVector, areaVector) > 1e-20;

            float3 positionWS = corner == 0u ? r0.xyz :
                corner == 1u ? r1.xyz : r2.xyz;
            output.positionCS = valid
                ? TransformWorldToHClip(positionWS)
                : float4(0.0, 0.0, 2.0, 1.0);
            output.barycentric = corner == 0u ? float3(1.0, 0.0, 0.0) :
                corner == 1u ? float3(0.0, 1.0, 0.0) :
                float3(0.0, 0.0, 1.0);
            output.support = valid ? support : 0.0;
            output.pageHash = metadata.pageXLo * 1664525u ^
                metadata.pageYLo * 1013904223u ^ _SegmentIndex * 2246822519u;
            return output;
        }

        void PreviewDepth(Varyings input)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            if (input.support <= 0.0)
                discard;
        }

        half4 PreviewColour(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            if (input.support <= 0.0)
                discard;
            float3 derivative = max(fwidth(input.barycentric), 1e-5);
            float3 interior = smoothstep(0.0,
                derivative * max(_PreviewWireThickness, 0.25),
                input.barycentric);
            float edge = 1.0 - min(interior.x,
                min(interior.y, interior.z));
            float hashPhase = (input.pageHash & 255u) / 255.0;
            half3 carrierColour = lerp(half3(0.02, 0.55, 0.85),
                half3(0.02, 0.95, 0.65), hashPhase * 0.35);
            half alpha = _PreviewWireframe > 0.5
                ? lerp(0.025h, 0.82h, saturate(edge))
                : (half)_PreviewAlpha;
            return half4(carrierColour, alpha);
        }
        ENDHLSL

        Pass
        {
            Name "DirectCarrierDepth"
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Blend Off

            HLSLPROGRAM
            #pragma vertex PreviewVert
            #pragma fragment PreviewDepth
            ENDHLSL
        }

        Pass
        {
            Name "DirectCarrierColour"
            Cull Off
            ZWrite Off
            ZTest Equal
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex PreviewVert
            #pragma fragment PreviewColour
            ENDHLSL
        }
    }
}
