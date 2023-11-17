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
            Name "ScreenSpaceRaytracedReflection Copy Depth"

            HLSLPROGRAM

            #pragma multi_compile _ SSR_BACK_FACES

            #pragma vertex Vert
            #pragma fragment FragCopyDepth
            ENDHLSL
        }

        Pass
        {
            // 1
            Name "ScreenSpaceRaytracedReflection Deferred"

            HLSLPROGRAM

            #pragma multi_compile_local _ SSR_JITTER
            #pragma multi_compile_local _ SSR_THICKNESS_FINE
            #pragma multi_compile _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ SSR_BACK_FACES
            #pragma multi_compile_local _ SSR_METALLIC_WORKFLOW
            
            #pragma vertex VertSSR
            #pragma fragment FragSSR
            ENDHLSL
        }

        Pass
        {
            // 2
            Name "ScreenSpaceRaytracedReflection Resolve"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragResolve
            ENDHLSL
        }

        Pass
        {
            // 3
            Name "ScreenSpaceRaytracedReflection Blur horizontally"
            HLSLPROGRAM

            #pragma multi_compile_local _ SSR_DENOISE

            #define SSR_BLUR_HORIZ

            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }

        Pass
        {
            // 4
            Name "ScreenSpaceRaytracedReflection Blur vertically"
            HLSLPROGRAM
            
            #pragma multi_compile_local _ SSR_DENOISE

            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }

        Pass
        {
            // 5
            Name "ScreenSpaceRaytracedReflection Combine"
            // Stencil
            // {
            //     Ref [_StencilValue]
            //     Comp [_StencilCompareFunction]
            // }
            Blend One One // precomputed alpha in Resolve pass
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCombine
            ENDHLSL
        }

        Pass
        {
            // 6
            Name "ScreenSpaceRaytracedReflection Combine with compare"
            // Stencil
            // {
            //     Ref [_StencilValue]
            //     Comp [_StencilCompareFunction]
            // }
            Blend One One // precomputed alpha in Resolve pass
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCombineWithCompare
            ENDHLSL
        }

        Pass
        {
            // 7
            Name "ScreenSpaceRaytracedReflection Debug"
            Blend One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCopyExact
            ENDHLSL
        }
    }
}
