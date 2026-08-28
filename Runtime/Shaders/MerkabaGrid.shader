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

            struct RenderRecord
            {
                int3 coord;
                uint activeMask;
                uint packedColor;
                uint padding0;
                uint padding1;
                uint padding2;
            };

            StructuredBuffer<RenderRecord> _MerkabaRenderRecords;
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

            bool PrimitiveActive(RenderRecord record, uint primitiveId)
            {
                return (record.activeMask & (1u << primitiveId)) != 0u;
            }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings output;
                RenderRecord record = _MerkabaRenderRecords[instanceID];
                uint primitiveId = vertexID / MERKABA_VERTICES_PER_PRIMITIVE;
                uint primitiveVertex = vertexID % MERKABA_VERTICES_PER_PRIMITIVE;
                if (!PrimitiveActive(record, primitiveId))
                {
                    // R6 removes inactive invocations by publishing actual triangles.
                    output.positionCS = float4(2, 2, 2, 1);
                    output.normalWS = half3(0, 1, 0);
                    output.color = half3(0, 0, 0);
                    return output;
                }

                float3 localPosition, localNormal;
                MerkabaCanonicalPrimitiveVertex(primitiveId, primitiveVertex,
                    localPosition, localNormal);
                localPosition += (float3)record.coord * MERKABA_LATTICE_STEP;
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
