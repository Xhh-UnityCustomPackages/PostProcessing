Shader "Hidden/PostProcessing/StochasticScreenSpaceReflection"
{
    
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
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
            Name "StochasticScreenSpaceReflection Linear_2DTrace_SingleSampler"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Linear_2DTrace_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 1
            Name "StochasticScreenSpaceReflection Hierarchical_ZTrace_SingleSampler"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Linear_2DTrace_SingleSPP
            //#pragma fragment TestSSGi_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 2
            Name "StochasticScreenSpaceReflection Linear_2DTrace_MultiSampler"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Linear_2DTrace_SingleSPP
            //#pragma fragment TestSSGi_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 3
            Name "StochasticScreenSpaceReflection Hierarchical_ZTrace_MultiSampler"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Linear_2DTrace_SingleSPP
            //#pragma fragment TestSSGi_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 4
            Name "StochasticScreenSpaceReflection Spatiofilter_SingleSampler"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Spatiofilter_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 5
            Name "StochasticScreenSpaceReflection Spatiofilter_MultiSampler"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Spatiofilter_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 6
            Name "StochasticScreenSpaceReflection Temporalfilter_SingleSampler"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Temporalfilter_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 7
            Name "StochasticScreenSpaceReflection Temporalfilter_MultiSampler"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Temporalfilter_SingleSPP
            ENDHLSL
        }

        Pass
        {
            // 8
            Name"StochasticScreenSpaceReflection CombineReflection"
            HLSLPROGRAM

            #pragma multi_compile_local _ DEBUG_SCREEN_SPACE_REFLECTION DEBUG_INDIRECT_SPECULAR
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #pragma vertex Vert
            #pragma fragment CombineReflectionColor
            ENDHLSL
        }
    }
}
