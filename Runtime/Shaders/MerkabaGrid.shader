Shader "Genesis/RoomScan/MerkabaGrid"
{
    Properties
    {
        _ScanOpacity("Scan Opacity", Range(0,1)) = 1
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "MerkabaForward"
            Tags { "LightMode"="UniversalForward" }
            // The readout emits one zero-thickness canonical sheet, never a
            // second UNKNOWN-side surface. Both physical sides of that one
            // sheet therefore use the same disposable vertex stream.
            Cull Off
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct MerkabaReadoutVertex
            {
                float3 gridPosition;
                uint packedColor;
            };

            StructuredBuffer<MerkabaReadoutVertex> _M8ReadoutVertices0;
            StructuredBuffer<MerkabaReadoutVertex> _M8ReadoutVertices1;
            float4x4 _MerkabaGridToWorld;

            CBUFFER_START(UnityPerMaterial)
                half _ScanOpacity;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
#if UNITY_ANY_INSTANCING_ENABLED
                UNITY_VERTEX_INPUT_INSTANCE_ID
#else
                uint proceduralInstanceID : SV_InstanceID;
#endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
                nointerpolation uint hasRgb : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                MerkabaReadoutVertex vertex;
                if (input.vertexID < 6291456u)
                    vertex = _M8ReadoutVertices0[input.vertexID];
                else
                    vertex = _M8ReadoutVertices1[
                        input.vertexID - 6291456u];
                float3 worldPosition = mul(_MerkabaGridToWorld,
                    float4(vertex.gridPosition, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(worldPosition);
                uint rgb = vertex.packedColor & 0x00ffffffu;
                output.color = half3(rgb & 255u, (rgb >> 8u) & 255u,
                    (rgb >> 16u) & 255u) / 255.0h;
                output.hasRgb = (vertex.packedColor >> 24u) & 1u;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 color = input.hasRgb != 0u
                    ? input.color : half3(0.55h, 0.16h, 0.42h);
                return half4(color, _ScanOpacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
