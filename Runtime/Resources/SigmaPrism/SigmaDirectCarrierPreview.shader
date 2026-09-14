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
            Name "PureEyeContacts"
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
            #include "SigmaGeometryReadout.hlsl"

            StructuredBuffer<SigmaPureEyeReadoutGpu> _ReadoutSamples;
            StructuredBuffer<uint> _CurrentPageSlots;
            float4x4 _RoomToWorld;
            float _PreviewWireframe;
            float _PreviewContactPixels;
            float _ReadoutOpacity;

            #define SIGMA_READOUT_VERTICES_PER_PAGE 24576u
            #define SIGMA_CONTACT_VERTICES_PER_SAMPLE 6u

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
                nointerpolation float3 colour : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings PreviewContactVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                uint activePage = input.vertexId /
                    SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageVertex = input.vertexId -
                    activePage * SIGMA_READOUT_VERTICES_PER_PAGE;
                uint pageSlot = _CurrentPageSlots[activePage];
                uint sample = pageVertex / SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                uint corner = pageVertex - sample * SIGMA_CONTACT_VERTICES_PER_SAMPLE;
                SigmaPureEyeReadoutGpu readout = _ReadoutSamples[
                    pageSlot * SIGMA_PAGE_SAMPLE_COUNT + sample];
                uint eye = unity_StereoEyeIndex;
                float4 positionSupport = eye == 0u
                    ? readout.leftPositionSupport
                    : readout.rightPositionSupport;
                float4 colourOrder = eye == 0u
                    ? readout.leftColourOrder
                    : readout.rightColourOrder;
                bool valid = sample < SIGMA_PAGE_SAMPLE_COUNT &&
                    positionSupport.w > 0.0;
                float3 worldPosition = mul(_RoomToWorld,
                    float4(positionSupport.xyz, 1.0)).xyz;
                float4 clip = valid ? TransformWorldToHClip(worldPosition) :
                    float4(0.0, 0.0, 2.0, 1.0);
                float2 billboard = SigmaPreviewBillboardCorner(corner);
                float2 screen = max(_ScreenParams.xy, float2(1.0, 1.0));
                clip.xy += billboard *
                    (2.0 * max(_PreviewContactPixels, 1.0) / screen) * clip.w;
                output.positionCS = clip;
                output.billboard = billboard;
                output.support = valid ? positionSupport.w : 0.0;
                output.colour = colourOrder.xyz;
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
                float feather = max(fwidth(radius), 0.015);
                float outer = 1.0 - smoothstep(1.0 - feather, 1.0, radius);
                float alpha = saturate(_ReadoutOpacity) * outer;
                if (_PreviewWireframe > 0.5)
                {
                    float inner = smoothstep(0.58 - feather,
                        0.58 + feather, radius);
                    alpha *= inner;
                }
                if (alpha <= 0.001)
                    discard;
                return half4((half3)input.colour, (half)alpha);
            }
            ENDHLSL
        }
    }
}
