Shader "Hidden/PostProcessing/LightShaft"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    TEXTURE2D(_LightShafts1);      SAMPLER(sampler_LightShafts1);


    #include "LightShaftInclude.cs.hlsl"

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "LightShafts Occlusion Prefilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment LightShaftsOcclusionPrefilterPassFragment
            #include "LightShaftPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LightShafts Bloom Prefilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment LightShaftsOcclusionPrefilterPassFragment
            #include "LightShaftPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LightShafts Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment LightShaftsBlurFragment
            #include "LightShaftPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LightShafts Occlusion Blend"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment LightShaftsOcclusionBlendFragment
            #include "LightShaftPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LightShafts Bloom Blend"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment LightShaftsBloomBlendFragment
            #include "LightShaftPass.hlsl"
            ENDHLSL
        }
    }
}
