Shader "Hidden/PostProcessing/StochasticScreenSpaceReflection"
{
    
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    #include "StochasticScreenSpaceReflection.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off


        Pass
        {
            // 0
            Name "StochasticScreenSpaceReflection Resolve"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragResolve
            ENDHLSL
        }
    }
}
