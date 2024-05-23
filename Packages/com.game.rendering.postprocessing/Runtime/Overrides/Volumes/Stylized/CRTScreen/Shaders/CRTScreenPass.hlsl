#ifndef CRTSCREEN_PASS_HLSL
#define CRTSCREEN_PASS_HLSL

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

float2 _Curvature;
float2 _Resolution;
float4 _PexelScanlineBrightness;
float _RGBSplitOffset;
float4 _VignetteParam;

#define VignetteIntensity   _VignetteParam.x
#define VignetteBrightness  _VignetteParam.y

float2 ScreenEdgeDistortion(float2 screenUV)
{
    float2 halfUV = screenUV * 2 - 1;
    // return halfUV;
    float2 uv = abs(halfUV);

    uv /= _Curvature;

    uv = pow(uv, 4);
    uv = uv * halfUV + halfUV;
    uv = uv * 0.5 + 0.5;
    return uv;
}

//
half4 ScreenScanLine(float2 screenUV)
{
    half2 xy = sin(screenUV.xy * _Resolution.x) * _PexelScanlineBrightness.xz;
    half V = xy.x + _PexelScanlineBrightness.y;
    half H = xy.y + _PexelScanlineBrightness.w;
    return V * H;
}

half4 RGBSplt(float2 screenUV)
{
    float2 uvR = screenUV;
    float2 uvG = screenUV - _RGBSplitOffset;
    float2 uvB = screenUV + _RGBSplitOffset;
    half colorR = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uvR).r;
    half colorG = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uvG).g;
    half colorB = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uvB).b;
    return half4(colorR, colorG, colorB, 1);
}

half ScreenVignette(float2 screenUV)
{
    half shadow = screenUV.x * screenUV.y;
    shadow *= (1 - screenUV.x) * (1 - screenUV.y);
    shadow *= VignetteBrightness;
    shadow = pow(shadow, VignetteIntensity);
    return shadow;
}

half4 Frag(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    float2 edgeUV = ScreenEdgeDistortion(uv);
    half vignette = ScreenVignette(uv);
    // return vignette;
    half4 scanLine = ScreenScanLine(edgeUV);
    return vignette * scanLine * RGBSplt(edgeUV);
}

#endif
