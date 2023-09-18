Shader "Hidden/PostProcessing/ScreenSpaceGlobalIllumination"
{
    
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    #include "ScreenSpaceGlobalIllumination.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            // 0
            Name "SSGI"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}

