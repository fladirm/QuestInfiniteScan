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

            #define MERKABA_LATTICE_STEP 0.025
            #define MERKABA_HALF_SUPPORT 0.025

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

            void PatchAxes(int axis, out float3 normal, out float3 tangent0,
                out float3 tangent1)
            {
                if (axis == 0)
                {
                    normal = float3(1, 0, 0); tangent0 = float3(0, 1, 0); tangent1 = float3(0, 0, 1);
                }
                else if (axis == 1)
                {
                    normal = float3(0, 1, 0); tangent0 = float3(0, 0, 1); tangent1 = float3(1, 0, 0);
                }
                else
                {
                    normal = float3(0, 0, 1); tangent0 = float3(1, 0, 0); tangent1 = float3(0, 1, 0);
                }
            }

            void CanonicalVertex(uint patch, uint vertex, out float3 position,
                out float3 normal)
            {
                int face = patch / 4;
                int quadrant = patch % 4;
                int axis = face / 2;
                int sign = (face & 1) == 0 ? -1 : 1;
                int u = (quadrant & 1) == 0 ? -1 : 1;
                int v = (quadrant & 2) == 0 ? -1 : 1;
                float3 n, b, c;
                PatchAxes(axis, n, b, c);
                float3 p00 = n * (sign * MERKABA_HALF_SUPPORT);
                float3 p10 = p00 + b * (u * MERKABA_HALF_SUPPORT);
                float3 p11 = p10 + c * (v * MERKABA_HALF_SUPPORT);
                float3 p01 = p00 + c * (v * MERKABA_HALF_SUPPORT);
                bool forward = sign * u * v > 0;
                uint forwardOrder[6] = { 0, 1, 2, 0, 2, 3 };
                uint reverseOrder[6] = { 0, 2, 1, 0, 3, 2 };
                uint corner = forward ? forwardOrder[vertex] : reverseOrder[vertex];
                position = corner == 0 ? p00 : corner == 1 ? p10 : corner == 2 ? p11 : p01;
                normal = n * sign;
            }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings output;
                RenderRecord record = _MerkabaRenderRecords[instanceID];
                uint patch = vertexID / 6u;
                uint patchVertex = vertexID % 6u;
                if ((record.activeMask & (1u << patch)) == 0u)
                {
                    output.positionCS = float4(2, 2, 2, 1);
                    output.normalWS = half3(0, 1, 0);
                    output.color = half3(0, 0, 0);
                    return output;
                }

                float3 localPosition, localNormal;
                CanonicalVertex(patch, patchVertex, localPosition, localNormal);
                localPosition += (float3)record.coord * MERKABA_LATTICE_STEP;
                float3 worldPosition = mul(_MerkabaGridToWorld, float4(localPosition, 1)).xyz;
                float3 worldNormal = normalize(mul((float3x3)_MerkabaGridToWorld, localNormal));
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.normalWS = worldNormal;
                output.color = half3(record.packedColor & 255u,
                    (record.packedColor >> 8) & 255u,
                    (record.packedColor >> 16) & 255u) / 255.0h * _ColorMultiplier.rgb;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalize(input.normalWS), mainLight.direction));
                half3 ambient = max(SampleSH(input.normalWS), _AmbientFloor.xxx);
                half3 lighting = ambient + mainLight.color * ndotl * mainLight.distanceAttenuation;
                return half4(input.color * lighting, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
