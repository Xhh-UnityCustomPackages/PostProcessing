Shader "Hidden/PostProcessing/ScreenSpaceReflection"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            // 0
            Name "ScreenSpaceReflection Test"

            HLSLPROGRAM
            #define BINARY_SEARCH 1
            
            #include "ScreenSpaceReflection.hlsl"
            #include "ScreenSpaceReflection_Linear.hlsl"
            #pragma multi_compile_local _ JITTER_BLURNOISE JITTER_DITHER
            
            #pragma vertex Vert
            #pragma fragment FragTestLinear
            ENDHLSL
        }

        Pass
        {
            // 1
            Name "ScreenSpaceReflection Resolve"

            HLSLPROGRAM
            #include "ScreenSpaceReflection.hlsl"
            
            #pragma vertex Vert
            #pragma fragment FragResolve
            ENDHLSL
        }

        Pass
        {
            // 2
            Name "ScreenSpaceReflection Reproject"

            HLSLPROGRAM
            #include "ScreenSpaceReflection.hlsl"
            #pragma vertex Vert
            #pragma fragment FragReproject
            ENDHLSL
        }

        Pass
        {
            // 3
            Name "ScreenSpaceReflection Composite"

            HLSLPROGRAM
            #include "ScreenSpaceReflection.hlsl"
            
            #pragma multi_compile_local _ DEBUG_SCREEN_SPACE_REFLECTION DEBUG_INDIRECT_SPECULAR
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }

        Pass
        {
            // 4
            Name "ScreenSpaceReflection MobilePlanarReflection"

            HLSLPROGRAM
            #include "ScreenSpaceReflection.hlsl"
            
            #pragma multi_compile_local _ DEBUG_SCREEN_SPACE_REFLECTION DEBUG_INDIRECT_SPECULAR
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #pragma vertex Vert
            #pragma fragment FragMobilePlanarReflection
            ENDHLSL
        }

        Pass
        {
            // 5
            Name "ScreenSpaceReflection MobileAntiFlicker"

            HLSLPROGRAM
            #include "ScreenSpaceReflection.hlsl"
            
            #pragma vertex Vert
            #pragma fragment FragMobileAntiFlicker
            ENDHLSL
        }

        Pass
        {
            // 6
            Name "ScreenSpaceReflection HiZ Test"

            HLSLPROGRAM
            #define HIZ 1
            #include "ScreenSpaceReflection.hlsl"
            #include "ScreenSpaceReflection_Hiz.hlsl"
            
            #pragma multi_compile_local _ JITTER_BLURNOISE JITTER_DITHER

            #pragma vertex Vert
            #pragma fragment FragTestHiZ
            ENDHLSL
        }
    }
}
