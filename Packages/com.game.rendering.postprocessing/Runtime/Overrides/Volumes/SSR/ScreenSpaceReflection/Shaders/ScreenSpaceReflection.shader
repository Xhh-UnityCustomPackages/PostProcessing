Shader "Hidden/PostProcessing/ScreenSpaceReflection"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            // 0
            Name "ScreenSpaceReflection LinearSS Test"

            HLSLPROGRAM
            
            #include "ScreenSpaceReflection_Linear.hlsl"
            
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            
            #pragma vertex Vert
            #pragma fragment FragTestLinear
            ENDHLSL
        }

        Pass
        {
            // 1
            Name "ScreenSpaceReflection HiZ Test"

            HLSLPROGRAM

            #include "ScreenSpaceReflection_Hiz.hlsl"

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #pragma vertex Vert
            #pragma fragment FragTestHiZ
            ENDHLSL
        }

        Pass
        {
            // 2
            Name "ScreenSpaceReflection Reprojection"
            HLSLPROGRAM
            #include "ScreenSpaceReflection_Reprojection.hlsl"

            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #pragma vertex Vert
            #pragma fragment FragSSRReprojection
            ENDHLSL
        }

        Pass
        {
            // 3
            Name "ScreenSpaceReflection Composite"

            HLSLPROGRAM
            #include "ScreenSpaceReflection_Resolve.hlsl"

            #pragma shader_feature_local _ DEBUG_SCREEN_SPACE_REFLECTION SPLIT_SCREEN_SPACE_REFLECTION

            #pragma vertex Vert
            #pragma fragment FragSSRComposite
            ENDHLSL
        }

        Pass
        {
            // 4
            Name "ScreenSpaceReflection Composite"

            HLSLPROGRAM
            #include "ScreenSpaceReflection_Reprojection.hlsl"

            #pragma shader_feature_local _ DEBUG_SCREEN_SPACE_REFLECTION SPLIT_SCREEN_SPACE_REFLECTION

            #pragma vertex Vert
            #pragma fragment FragSSRAccumulation
            ENDHLSL
        }
    }
}
