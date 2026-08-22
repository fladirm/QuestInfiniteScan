Shader "Hidden/Genesis/ConePrism/ContactFilmDepth"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+9" }
        Pass
        {
            Name "Cone-PRISM Front Depth"
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ContactMeshletVertexAbi.hlsl"

            StructuredBuffer<ContactMeshletVertex> _ContactVertices;
            StructuredBuffer<uint> _ContactIndices;
            float4x4 _WorldFromChunk;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                nointerpolation uint filmId : TEXCOORD2;
                nointerpolation uint generation : TEXCOORD3;
                nointerpolation uint flags : TEXCOORD4;
                float coverage : TEXCOORD5;
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
                    UnpackContactMeshletNormal(input.packedNormal)));
                output.filmId = ContactMeshletFilmId(
                    input.packedFilmMaterial);
                output.generation = input.generation;
                output.flags = ContactMeshletFlags(input.packedFilmMaterial);
                output.coverage = ContactMeshletCoverage(
                    input.packedFilmMaterial);
                return output;
            }

            void Frag(Varyings input)
            {
                if ((input.flags & 1u) == 0u || input.filmId == 0u ||
                    input.generation == 0u || input.coverage < 0.5) discard;
                float3 view = normalize(GetCameraPositionWS() - input.worldPosition);
                if (dot(input.worldNormal, view) <= 0.0) discard;
            }
            ENDHLSL
        }
    }
}
