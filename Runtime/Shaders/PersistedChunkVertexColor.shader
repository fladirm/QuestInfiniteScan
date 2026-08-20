Shader "Genesis/RoomScan/PersistedChunkVertexColor"
{
    Properties
    {
        _ColorTint ("Color Tint", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorTint;
                half _Ambient;
            CBUFFER_END

            float _RSNormalFallback;
            float _RSWireframe;
            float _RSWireThickness;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float3 barycentric : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half4 color : COLOR;
                float3 barycentric : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color * _ColorTint;
                output.barycentric = input.barycentric;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 baseColor = _RSNormalFallback > 0.5
                    ? half3(normalWS * 0.5 + 0.5)
                    : input.color.rgb;

                if (_RSWireframe > 0.5)
                {
                    float thickness = max(_RSWireThickness, 0.2);
                    float3 bary = input.barycentric;
                    float3 dx = ddx(bary);
                    float3 dy = ddy(bary);
                    float3 edgeWidth = sqrt(dx * dx + dy * dy);
                    float3 edge = smoothstep(0.0, edgeWidth * thickness, bary);
                    float minEdge = min(edge.x, min(edge.y, edge.z));
                    float discardThreshold = saturate(1.0 - thickness * 0.15);
                    if (minEdge > discardThreshold)
                        discard;

                    float vertexProximity = max(bary.x, max(bary.y, bary.z));
                    float vertexBlend = smoothstep(0.35, 0.85, vertexProximity);
                    baseColor = lerp(half3(0.9, 0.9, 0.92), baseColor, vertexBlend);
                }
                return half4(baseColor, 1.0h);
            }
            ENDHLSL
        }
    }
}
