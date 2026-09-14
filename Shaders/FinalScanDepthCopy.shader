// Copies the resolved URP camera depth (_CameraDepthTexture, bound by Blitter as _BlitTexture) into the owned
// R32F previous-frame depth array used as the §13.5 occlusion prior (Runtime/Render/PrevDepthCapture.cs).
// Blit.hlsl's Vert handles single-pass instancing: the instance multiplier of the XR pass draws both slices and
// UNITY_VERTEX_OUTPUT_STEREO routes each instance to its render-target array layer. Raw depth is copied (reversed-Z
// on Vulkan); the native side reads it with the projection it received from FsRender_SetPrevDepth.
Shader "FinalScan/DepthCopy"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FinalScanDepthCopy"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma exclude_renderers gles

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).r;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
