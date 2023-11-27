Shader "Hidden/PostProcessing/ScreenSpaceCavity"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    ENDHLSL


    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off


        Pass
        {
            // 0
            Name "ScreenSpaceCavity"

            HLSLPROGRAM

            #pragma multi_compile_local _ _TYPE_CURVATURE
            #pragma multi_compile_local _ _TYPE_CAVITY
            #pragma multi_compile_local _ _ORTHOGRAPHIC

            #pragma vertex Vert
            #pragma fragment SSC
            #include "SSC.hlsl"

            ENDHLSL
        }
    }
}
