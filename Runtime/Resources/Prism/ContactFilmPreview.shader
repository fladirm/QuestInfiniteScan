Shader "Hidden/Genesis/ConePrism/ContactFilmPreview"
{
    Properties
    {
        _PreviewCoverage ("Preview Opacity", Range(0, 1)) = 0.72
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry+10"
            "RenderType"="Opaque"
        }
        Pass
        {
            Name "Cone-PRISM Preview"
            Cull Off
            ZWrite On
            ZTest LEqual
            // Mesh-only transparency. ZWrite keeps the front ContactFilm coherent
            // and prevents stacks of hidden layers from becoming an alpha soup;
            // no global/UI/compositor material state is touched.
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ContactMeshletVertex
            {
                float3 position; uint filmId;
                float3 normal; uint generation;
                float2 uv; float sigma; float confidence;
                uint sidedness; uint flags; uint appearancePage;
                uint meshletIndex;
            };

            StructuredBuffer<ContactMeshletVertex> _ContactVertices;
            StructuredBuffer<uint> _ContactIndices;
            float4x4 _WorldFromChunk;
            float _PreviewCoverage;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float confidence : TEXCOORD2;
                nointerpolation uint flags : TEXCOORD3;
            };

            Varyings Vert(uint vertexId : SV_VertexID)
            {
                ContactMeshletVertex input =
                    _ContactVertices[_ContactIndices[vertexId]];
                Varyings output;
                float4 world = mul(_WorldFromChunk, float4(input.position, 1.0));
                output.positionCS = TransformWorldToHClip(world.xyz);
                output.worldPosition = world.xyz;
                output.worldNormal = normalize(mul((float3x3)_WorldFromChunk,
                    input.normal));
                output.confidence = input.confidence;
                output.flags = input.flags;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                bool latentConnector = (input.flags & (1u << 9u)) != 0u;
                float opacity = saturate(_PreviewCoverage) *
                    lerp(0.72, 1.0, saturate(input.confidence));
                if (latentConnector) opacity *= 0.30;
                float3 view = normalize(GetCameraPositionWS() -
                    input.worldPosition);
                if (dot(input.worldNormal, view) <= 0.0) discard;
                float3 normalColor = 0.25 + 0.55 * abs(input.worldNormal);
                float confidence = saturate(input.confidence);
                return float4(lerp(float3(0.18, 0.22, 0.28), normalColor,
                    0.35 + 0.65 * confidence), opacity);
            }
            ENDHLSL
        }
    }
}
