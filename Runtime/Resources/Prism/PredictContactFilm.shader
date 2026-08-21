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

            struct ContactMeshletVertex
            {
                float3 position;
                uint filmId;
                float3 normal;
                uint generation;
                float2 uv;
                float sigma;
                float confidence;
                uint sidedness;
                uint flags;
                uint reserved0;
                uint reserved1;
            };

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
            };

            Varyings Vert(uint vertexId : SV_VertexID)
            {
                ContactMeshletVertex input = _ContactVertices[_ContactIndices[vertexId]];
                Varyings output;
                output.positionCS = mul(_ClipFromChunk, float4(input.position, 1.0));
                output.positionOptical = mul(_OpticalFromChunk,
                    float4(input.position, 1.0)).xyz;
                output.normalOptical = normalize(mul((float3x3)_OpticalFromChunk,
                    input.normal));
                output.uv = input.uv;
                output.sigma = input.sigma;
                output.confidence = input.confidence;
                output.filmId = input.filmId;
                output.generation = input.generation;
                output.sidedness = input.sidedness;
                output.flags = input.flags;
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
                // Elastic connector/frontier triangles close the conserved
                // membrane for rendering, but they are high-sigma UNKNOWN rather
                // than measured contacts. Only actual ContactFilm material may
                // become the hardware first-hit association target.
                if ((input.flags & (1u << 9u)) != 0u)
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
