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

            uint SigmaPreviewHash(uint value)
            {
                value ^= value >> 16u;
                value *= 0x7feb352du;
                value ^= value >> 15u;
                value *= 0x846ca68bu;
                return value ^ (value >> 16u);
            }

            uint SigmaPreviewPageHash(SigmaCarrierPageMetaGpu metadata)
            {
                uint value = SigmaPreviewHash(metadata.pageXLo ^
                    SigmaPreviewHash(metadata.pageXHi));
                value ^= SigmaPreviewHash(metadata.pageYLo ^
                    SigmaPreviewHash(metadata.pageYHi));
                return SigmaPreviewHash(value ^
                    SigmaPreviewHash(_SegmentIndex));
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

            float3 SigmaPreviewHue(float hue)
            {
                float3 phase = frac(hue + float3(0.0, 0.6666667, 0.3333333));
                return saturate(abs(phase * 6.0 - 3.0) - 1.0);
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
                nointerpolation float generationTone : TEXCOORD2;
                nointerpolation uint pageHash : TEXCOORD3;
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

                SigmaCarrierPageMetaGpu metadata = _PageMetadata[pageSlot];
                uint pageHash = SigmaPreviewPageHash(metadata);
                float generationTone = 0.62 + 0.38 *
                    frac((float)metadata.generation * 0.38196601125);
                float generationSize = 0.88 + 0.12 *
                    (float)(metadata.generation & 3u);
                float2 billboard = SigmaPreviewBillboardCorner(corner);
                float4 clip = valid
                    ? TransformWorldToHClip(readout.xyz)
                    : float4(0.0, 0.0, 2.0, 1.0);
                float2 screenSize = max(_ScreenParams.xy,
                    float2(1.0, 1.0));
                clip.xy += billboard *
                    (2.0 * max(_PreviewContactPixels, 1.0) *
                        generationSize / screenSize) * clip.w;

                output.positionCS = clip;
                output.billboard = billboard;
                output.support = valid ? readout.w : 0.0;
                output.generationTone = generationTone;
                output.pageHash = pageHash;
                return output;
            }

            half4 PreviewContactColour(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (input.support <= 0.0)
                    discard;

                float radius = length(input.billboard);
                if (radius > 1.0)
                    discard;

                float hue = (float)(input.pageHash & 1023u) / 1024.0;
                float3 pageColour = SigmaPreviewHue(hue);
                pageColour = lerp(float3(0.08, 0.08, 0.08), pageColour,
                    input.generationTone);

                float feather = max(fwidth(radius), 0.015);
                float outer = 1.0 - smoothstep(1.0 - feather, 1.0, radius);
                if (_PreviewWireframe > 0.5)
                {
                    float inner = smoothstep(0.58 - feather,
                        0.58 + feather, radius);
                    float ring = outer * inner;
                    if (ring <= 0.01)
                        discard;
                    return half4((half3)pageColour, (half)(0.92 * ring));
                }

                float centre = 1.0 - smoothstep(0.0, 0.34, radius);
                float3 colour = lerp(pageColour, float3(1.0, 1.0, 1.0),
                    centre * 0.45);
                return half4((half3)colour, (half)(0.86 * outer));
            }
            ENDHLSL
        }
    }
}
