Shader "Hidden/UberPost"
{
    
    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ScreenCoordOverride.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScalingClamping.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
    #include "Packages/com.game.rendering.postprocessing/Runtime/Overrides/Volumes/Tonemapping/Shaders/Tonemapping.hlsl"
    #include "Packages/com.game.rendering.postprocessing/Runtime/Overrides/Volumes/Vignette/Shaders/Vignette.hlsl"

    #include "Packages/com.game.rendering.postprocessing/ShaderLibrary/EvaluateExposure.hlsl"
    #include "Packages/com.game.rendering.postprocessing/ShaderLibrary/EvaluateScreenSpaceGlobalIllumination.hlsl"


    half4 Frag(Varyings input) : SV_Target
    {
        float2 uv = SCREEN_COORD_APPLY_SCALEBIAS(UnityStereoTransformScreenSpaceTex(input.texcoord));
        half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).xyz;

        // half4 ssgi = SampleScreenSpaceGlobalIllumination(uv);
        // color += ssgi;
        // return ssgi;
        
        color = ApplyExposure(color);
        color = ApplyVignette(color, uv);
        color = ApplyTonemaping(color);

        return half4(color, 1.0);
    }

    ENDHLSL

    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "UberPost"

            HLSLPROGRAM
            #pragma multi_compile_local_fragment _  _TONEMAP_ACES _TONEMAP_NEUTRAL _TONEMAP_GT _TONEMAP_GT_CUSTOM _TONEMAP_NAES _TONEMAP_LOG2
            
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
