Shader "Genesis/RoomScan/MerkabaGrid"
{
    Properties
    {
        _ColorMultiplier("Color Multiplier", Color) = (1,1,1,1)
        _AmbientFloor("Ambient Floor", Range(0,1)) = 0.22
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "MerkabaForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "MerkabaCanonicalGeometry.generated.hlsl"

            #define MERKABA_LATTICE_STEP 0.025

            struct PrimitiveRecord
            {
                uint packedLocalPrimitive;
                uint packedColor;
            };

            StructuredBuffer<PrimitiveRecord> _MerkabaPrimitiveRecordBanks;
            StructuredBuffer<uint> _MerkabaPublishedBanks;
            uint _MerkabaResidentSlot;
            uint _MerkabaResidentSlotCapacity;
            uint _MerkabaPrimitiveCapacityPerChunk;
            float3 _MerkabaChunkOrigin;
            float4x4 _MerkabaGridToWorld;

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorMultiplier;
                half _AmbientFloor;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half3 color : TEXCOORD1;
            };

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings output;
                uint bankStride = _MerkabaResidentSlotCapacity *
                    _MerkabaPrimitiveCapacityPerChunk;
                uint recordIndex =
                    (_MerkabaPublishedBanks[_MerkabaResidentSlot] & 1u) * bankStride +
                    _MerkabaResidentSlot *
                    _MerkabaPrimitiveCapacityPerChunk + instanceID;
                PrimitiveRecord record = _MerkabaPrimitiveRecordBanks[recordIndex];
                uint localIndex = record.packedLocalPrimitive & 32767u;
                uint primitiveId = (record.packedLocalPrimitive >> 15u) & 31u;
                uint3 localCoord = uint3(localIndex & 31u,
                    (localIndex >> 5u) & 31u, (localIndex >> 10u) & 31u);

                float3 localPosition, localNormal;
                MerkabaCanonicalPrimitiveVertex(primitiveId, vertexID,
                    localPosition, localNormal);
                localPosition += (_MerkabaChunkOrigin + (float3)localCoord) *
                    MERKABA_LATTICE_STEP;
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(localPosition, 1)).xyz;
                float3 worldNormal = normalize(mul((float3x3)_MerkabaGridToWorld,
                    localNormal));
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.normalWS = worldNormal;
                output.color = half3(record.packedColor & 255u,
                    (record.packedColor >> 8) & 255u,
                    (record.packedColor >> 16) & 255u) / 255.0h *
                    _ColorMultiplier.rgb;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalize(input.normalWS),
                    mainLight.direction));
                half3 ambient = max(SampleSH(input.normalWS), _AmbientFloor.xxx);
                half3 lighting = ambient + mainLight.color * ndotl *
                    mainLight.distanceAttenuation;
                return half4(input.color * lighting, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
