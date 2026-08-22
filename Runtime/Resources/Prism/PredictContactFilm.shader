Shader "Hidden/Genesis/ConePrism/PredictContactFilm"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Prediction"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ContactMeshletVertexAbi.hlsl"

            StructuredBuffer<ContactMeshletVertex> _ContactVertices;
            StructuredBuffer<uint> _ContactIndices;
            float4x4 _ClipFromChunk;
            float4x4 _OpticalFromChunk;
            Texture2DArray<float2> _PeelDepth;
            int _PeelEye;
            float _PeelEnabled;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOptical : TEXCOORD0;
                float3 normalOptical : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float sigma : TEXCOORD3;
                float confidence : TEXCOORD4;
                nointerpolation uint filmId : TEXCOORD5;
                nointerpolation uint generation : TEXCOORD6;
                nointerpolation uint sidedness : TEXCOORD7;
                nointerpolation uint flags : TEXCOORD8;
                float coverage : TEXCOORD9;
            };

            Varyings Vert(uint vertexId : SV_VertexID)
            {
                ContactMeshletVertex input = _ContactVertices[_ContactIndices[vertexId]];
                Varyings output;
                output.positionCS = mul(_ClipFromChunk, float4(input.position, 1.0));
                output.positionOptical = mul(_OpticalFromChunk,
                    float4(input.position, 1.0)).xyz;
                output.normalOptical = normalize(mul((float3x3)_OpticalFromChunk,
                    UnpackContactMeshletNormal(input.packedNormal)));
                output.uv = UnpackContactMeshletHalf2(input.packedUv);
                float2 sigmaConfidence = UnpackContactMeshletHalf2(
                    input.packedSigmaConfidence);
                output.sigma = sigmaConfidence.x;
                output.confidence = sigmaConfidence.y;
                output.filmId = ContactMeshletFilmId(
                    input.packedFilmMaterial);
                output.generation = input.generation;
                output.sidedness = ContactMeshletSidedness(
                    input.packedFilmMaterial);
                output.flags = ContactMeshletFlags(input.packedFilmMaterial);
                output.coverage = ContactMeshletCoverage(
                    input.packedFilmMaterial);
                return output;
            }

            struct PredictionOutput
            {
                float2 depthSigma : SV_Target0;
                float4 normalConfidence : SV_Target1;
                uint2 idGeneration : SV_Target2;
                float4 uvMetadata : SV_Target3;
            };

            PredictionOutput Frag(Varyings input)
            {
                // Positive eligibility: only explicitly measured fragments with a
                // generation-tagged ContactFilm may become a first-hit predictor.
                // The continuous support contour partitions measured and latent
                // copies without relying on provoking-vertex ID semantics.
                if ((input.flags & (1u << 0u)) == 0u || input.filmId == 0u ||
                    input.generation == 0u || input.coverage < 0.5)
                    discard;
                float3 normal = normalize(input.normalOptical);
                float3 surfaceToEye = -normalize(input.positionOptical);
                // ContactFilms are intrinsically one-sided. A back-side observation
                // is UNKNOWN/new-layer evidence, never the same predicted contact.
                if (dot(normal, surfaceToEye) <= 0.0)
                    discard;

                float contactRange = length(input.positionOptical);
                if (_PeelEnabled > 0.5)
                {
                    uint2 pixel = (uint2)input.positionCS.xy;
                    float firstRange = _PeelDepth.Load(
                        int4(pixel.x, pixel.y, _PeelEye, 0)).x;
                    float peelGate = max(0.0015, 1.5 * input.sigma);
                    if (firstRange <= 0.0 || contactRange <= firstRange + peelGate)
                        discard;
                }

                PredictionOutput output;
                output.depthSigma = float2(contactRange, input.sigma);
                output.normalConfidence = float4(normal, input.confidence);
                output.idGeneration = uint2(input.filmId, input.generation);
                output.uvMetadata = float4(input.uv,
                    (float)input.sidedness, (float)input.flags);
                return output;
            }
            ENDHLSL
        }
    }
}
