Shader "Hidden/PostProcessing/ScreenSpaceOcclusion"
{
    HLSLINCLUDE
    #include "ScreenSpaceOcclusionInput.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "ScreenSpaceOcclusion AO"

            HLSLPROGRAM

            #pragma multi_compile_local HORIZON_BASED_AMBIENTOCCLUSION GROUNDTRUTH_BASED_AMBIENTOCCLUSION SCALABLE_AMBIENT_OBSCURANCE
            #pragma multi_compile_local QUALITY_LOWEST QUALITY_LOW QUALITY_MEDIUM QUALITY_HIGH QUALITY_HIGHEST
            #pragma multi_compile_local _ RECONSTRUCT_NORMAL_LOW RECONSTRUCT_NORMAL_MEDIUM RECONSTRUCT_NORMAL_HIGH
            #pragma multi_compile_local _ DEBUG_AO DEBUG_VIEWNORMAL
            
            #pragma vertex Vert
            #pragma fragment FragAO

            #include "ScreenSpaceOcclusionAO.hlsl"
            
            ENDHLSL
        }

        Pass
        {
            Name "ScreenSpaceOcclusion BlurH"

            HLSLPROGRAM
            #pragma multi_compile_local BLUR_RADIUS_2 BLUR_RADIUS_3 BLUR_RADIUS_4 BLUR_RADIUS_5

            #pragma vertex Vert
            #pragma fragment FragBlurH

            #include "ScreenSpaceOcclusionBlur.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ScreenSpaceOcclusion BlurV"

            HLSLPROGRAM
            #pragma multi_compile_local BLUR_RADIUS_2 BLUR_RADIUS_3 BLUR_RADIUS_4 BLUR_RADIUS_5

            #pragma vertex Vert
            #pragma fragment FragBlurV

            #include "ScreenSpaceOcclusionBlur.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ScreenSpaceOcclusion Composite"

            HLSLPROGRAM
            #pragma multi_compile_local HORIZON_BASED_AMBIENTOCCLUSION GROUNDTRUTH_BASED_AMBIENTOCCLUSION SCALABLE_AMBIENT_OBSCURANCE
            #pragma multi_compile_local _ DEBUG_AO DEBUG_VIEWNORMAL

            #pragma vertex Vert
            #pragma fragment FragComposite

            #include "ScreenSpaceOcclusionComposite.hlsl"
            ENDHLSL
        }
    }
}
