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
            Name "DirectCarrierContacts"
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma vertex PreviewContactVert
            #pragma fragment PreviewContactColour

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SigmaCarrierAbi.hlsl"

            StructuredBuffer<float4> _ReadoutVertices;
            StructuredBuffer<uint> _CurrentPageSlots;
            StructuredBuffer<SigmaCarrierPageMetaGpu> _PageMetadata;
            uint _SegmentIndex;
            float _PreviewWireframe;
            float _PreviewContactPixels;

            #define SIGMA_READOUT_EXTENT 65u
            #define SIGMA_READOUT_SAMPLES 4225u
            #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u
            #define SIGMA_CONTACT_SAMPLES_PER_PAGE 4096u
            #define SIGMA_CONTACT_VERTICES_PER_SAMPLE 6u

            uint SigmaPreviewReadoutIndex(uint pageSlot, uint x, uint y)
            {
                return pageSlot * SIGMA_READOUT_SAMPLES +
                    y * SIGMA_READOUT_EXTENT + x;
            }

            float2 SigmaPreviewBillboardCorner(uint corner)
            {
                if (corner == 0u) return float2(-1.0, -1.0);
                if (corner == 1u) return float2(-1.0, 1.0);
                if (corner == 2u) return float2(1.0, -1.0);
                if (corner == 3u) return float2(1.0, -1.0);
                if (corner == 4u) return float2(-1.0, 1.0);
                return float2(1.0, 1.0);
            }

            struct Attributes
            {
                uint vertexId : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 billboard : TEXCOORD0;
                nointerpolation float support : TEXCOORD1;
                nointerpolation uint pageHash : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings PreviewContactVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                uint activePage = input.vertexId /
                    SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageVertex = input.vertexId -
                    activePage * SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageSlot = _CurrentPageSlots[activePage];
                uint sample = pageVertex / SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                uint corner = pageVertex -
                    sample * SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                uint x = sample & 63u;
                uint y = sample >> 6u;
                float4 readout = _ReadoutVertices[
                    SigmaPreviewReadoutIndex(pageSlot, x, y)];
                bool valid = sample < SIGMA_CONTACT_SAMPLES_PER_PAGE &&
                    readout.w > 0.0;
                float2 billboard = SigmaPreviewBillboardCorner(corner);
                float4 clip = valid
                    ? TransformWorldToHClip(readout.xyz)
                    : float4(0.0, 0.0, 2.0, 1.0);
                float2 screenSize = max(_ScreenParams.xy,
                    float2(1.0, 1.0));
                clip.xy += billboard *
                    (2.0 * max(_PreviewContactPixels, 1.0) / screenSize) *
                    clip.w;

                SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
                output.positionCS = clip;
                output.billboard = billboard;
                output.support = valid ? readout.w : 0.0;
                output.pageHash = metadata.pageXLo * 1664525u ^
                    metadata.pageYLo * 1013904223u ^
                    _SegmentIndex * 2246822519u;
                return output;
            }

            half4 PreviewContactColour(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (input.support <= 0.0 ||
                    dot(input.billboard, input.billboard) > 1.0)
                    discard;
                float hashPhase = (input.pageHash & 255u) / 255.0;
                half3 colour = _PreviewWireframe > 0.5
                    ? half3(1.0, 0.72, 0.04)
                    : lerp(half3(1.0, 0.02, 0.72),
                        half3(0.72, 0.05, 1.0), hashPhase * 0.35);
                return half4(colour, 0.92h);
            }
            ENDHLSL
        }
    }
}
