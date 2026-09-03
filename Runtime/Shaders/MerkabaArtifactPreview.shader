Shader "Hidden/QuestMerkaba/ArtifactPreview"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination Blend", Float) = 0
        [Toggle] _ZWrite("Depth Write", Float) = 1
        [Toggle] _AlphaDither("Alpha Dither", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ArtifactPreview"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _AlphaDither;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = TransformObjectToWorld(
                    input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.worldPosition);
                output.color = input.color * _BaseColor;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_AlphaDither > 0.5h)
                {
                    int3 worldCell = (int3)floor(input.worldPosition * 40.0);
                    uint hash = (uint)input.positionCS.x * 0x8da6b343u ^
                        (uint)input.positionCS.y * 0xd8163841u ^
                        asuint(worldCell.x) * 0xcb1ab31fu ^
                        asuint(worldCell.y) * 0x165667b1u ^
                        asuint(worldCell.z) * 0x27d4eb2fu;
                    hash ^= hash >> 13u;
                    hash *= 0x85ebca6bu;
                    hash ^= hash >> 16u;
                    half threshold = (half)((hash & 255u) + 0.5) / 256.0h;
                    clip(input.color.a - threshold);
                    return half4(input.color.rgb, 1.0h);
                }
                return input.color;
            }
            ENDHLSL
        }
    }
}
