Shader "Hidden/UberPost"
{
    
    HLSLINCLUDE


    #pragma multi_compile_local_fragment _  _TONEMAP_ACES _TONEMAP_NEUTRAL _TONEMAP_GT


    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "Packages/com.game.rendering.postprocessing/Runtime/Overrides/Volumes/Tonemapping/Shaders/Tonemapping.hlsl"

    TEXTURE2D(_AutoExposureLUT);
    
    half3 ApplyExposure(half3 input)
    {
        half exposure = SAMPLE_TEXTURE2D_LOD(_AutoExposureLUT, sampler_LinearClamp, 0, 0);
        return input * exposure;
    }


    half4 Frag(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).xyz;

        color = ApplyExposure(color);
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
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
