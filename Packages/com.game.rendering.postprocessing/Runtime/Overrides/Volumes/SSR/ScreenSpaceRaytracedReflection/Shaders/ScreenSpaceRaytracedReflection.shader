Shader "Hidden/PostProcessing/ScreenSpaceRaytracedReflection"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    #include "ScreenSpaceRaytracedReflection.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            // 0
            Name "ScreenSpaceReflection Copy Depth"

            HLSLPROGRAM

            #pragma multi_compile _ SSR_BACK_FACES

            #pragma vertex Vert
            #pragma fragment FragCopyDepth
            ENDHLSL
        }

        Pass
        {
            // 1
            Name "ScreenSpaceReflection Deferred"

            HLSLPROGRAM

            #pragma multi_compile_local _ SSR_JITTER
            #pragma multi_compile_local _ SSR_THICKNESS_FINE
            #pragma multi_compile _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ SSR_BACK_FACES
            #pragma multi_compile _ SSR_SKYBOX

            #pragma vertex VertSSR
            #pragma fragment FragSSR
            ENDHLSL
        }

        // Pass
        // {
        //     // 2
        //     Name "ScreenSpaceReflection Reproject"

        //     HLSLPROGRAM
        //     #pragma vertex Vert
        //     #pragma fragment FragReproject
        //     ENDHLSL
        // }

        // Pass
        // {
        //     // 3
        //     Name "ScreenSpaceReflection Composite"

        //     HLSLPROGRAM
        //     #pragma multi_compile_local _ DEBUG_SCREEN_SPACE_REFLECTION DEBUG_INDIRECT_SPECULAR

        //     #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
        //     #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

        //     #pragma vertex Vert
        //     #pragma fragment FragComposite
        //     ENDHLSL
        // }

        // Pass
        // {
        //     // 4
        //     Name "ScreenSpaceReflection MobilePlanarReflection"

        //     HLSLPROGRAM
        //     #pragma multi_compile_local _ DEBUG_SCREEN_SPACE_REFLECTION DEBUG_INDIRECT_SPECULAR

        //     #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
        //     #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

        //     #pragma vertex Vert
        //     #pragma fragment FragMobilePlanarReflection
        //     ENDHLSL
        // }
        // Pass
        // {
        //     Name "ScreenSpaceReflection MobileAntiFlicker"

        //     HLSLPROGRAM
        //     #pragma vertex Vert
        //     #pragma fragment FragMobileAntiFlicker
        //     ENDHLSL
        // }

    }
}
