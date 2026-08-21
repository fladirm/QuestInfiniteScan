Shader "Hidden/Genesis/ConePrism/ContactFilmPreview"
{
    Properties
    {
        _PreviewCoverage ("Preview Coverage", Range(0, 1)) = 0.78
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
            Blend Off

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
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Coverage transparency keeps surviving samples fully opaque in the
                // compositor and writes one coherent first-hit depth. Unlike alpha
                // blending it cannot make the passthrough/UI composition translucent
                // or reveal a stack of films behind the front contact.
                uint2 pixel = (uint2)input.positionCS.xy;
                uint2 p = pixel & 3u;
                static const uint bayer4x4[16] = {
                    0u, 8u, 2u, 10u,
                    12u, 4u, 14u, 6u,
                    3u, 11u, 1u, 9u,
                    15u, 7u, 13u, 5u
                };
                float threshold = ((float)bayer4x4[p.y * 4u + p.x] + 0.5) /
                    16.0;
                clip(saturate(_PreviewCoverage) - threshold);
                float3 view = normalize(GetCameraPositionWS() -
                    input.worldPosition);
                if (dot(input.worldNormal, view) <= 0.0) discard;
                float3 normalColor = 0.25 + 0.55 * abs(input.worldNormal);
                float confidence = saturate(input.confidence);
                return float4(lerp(float3(0.18, 0.22, 0.28), normalColor,
                    0.35 + 0.65 * confidence), 1.0);
            }
            ENDHLSL
        }
    }
}
