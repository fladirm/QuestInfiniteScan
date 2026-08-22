Shader "Hidden/Genesis/ConePrism/ContactFilmPreview"
{
    Properties
    {
        _PreviewCoverage ("Preview Opacity", Range(0, 1)) = 0.72
        _PreviewMode ("Preview Mode", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 3
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }
        Pass
        {
            Name "Cone-PRISM Preview"
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ContactMeshletVertexAbi.hlsl"

            StructuredBuffer<ContactMeshletVertex> _ContactVertices;
            StructuredBuffer<uint> _ContactIndices;
            float4x4 _WorldFromChunk;
            float _PreviewCoverage;
            float _PreviewMode;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float confidence : TEXCOORD2;
                nointerpolation uint flags : TEXCOORD3;
                float coverage : TEXCOORD4;
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
                output.confidence = UnpackContactMeshletHalf2(
                    input.packedSigmaConfidence).y;
                output.flags = ContactMeshletFlags(input.packedFilmMaterial);
                output.coverage = ContactMeshletCoverage(
                    input.packedFilmMaterial);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                bool measured = (input.flags & (1u << 0u)) != 0u;
                bool connector = (input.flags & (1u << 2u)) != 0u;
                bool measuredPass = _PreviewMode < 0.5;
                if (measuredPass && (!measured || input.coverage < 0.5)) discard;
                if (!measuredPass && !connector) discard;
                float opacity = saturate(_PreviewCoverage) *
                    lerp(0.72, 1.0, saturate(input.confidence));
                if (connector) opacity *= 0.30;
                float3 view = normalize(GetCameraPositionWS() -
                    input.worldPosition);
                if (dot(input.worldNormal, view) <= 0.0) discard;
                float3 normalColor = 0.25 + 0.55 * abs(input.worldNormal);
                float confidence = saturate(input.confidence);
                return float4(lerp(float3(0.18, 0.22, 0.28), normalColor,
                    0.35 + 0.65 * confidence), opacity);
            }
            ENDHLSL
        }
    }
}
