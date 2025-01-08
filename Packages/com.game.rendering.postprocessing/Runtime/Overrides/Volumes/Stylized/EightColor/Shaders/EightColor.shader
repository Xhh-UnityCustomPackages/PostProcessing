Shader "Hidden/PostProcessing/EightColor"
{

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    #include "EightColor.hlsl"
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        ZTest Always ZWrite Off Cull Off Blend Off
        LOD 200

        Pass
        {
            Name "EightColor"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4x4 _Palette1, _Palette2;
            float _Opacity, _Dithering, _Downsampling;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                float2 pixelSS, outUV;
                EdgeColorUV(uv, _ScreenParams.x, _ScreenParams.y, _Downsampling, pixelSS, outUV);
                float4 sceneColor = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, outUV, 0);

                float4 eightColor;
                EightColorCore_float(sceneColor, pixelSS, _Palette1, _Palette2, _Dithering, _Downsampling, eightColor);
                return lerp(sceneColor, eightColor, _Opacity);
            }
            ENDHLSL
        }
    }
}