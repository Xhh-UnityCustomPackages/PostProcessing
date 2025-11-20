#ifndef VIGNETTE_HLSL
#define VIGNETTE_HLSL

half4 _Vignette_Params1;
float4 _Vignette_Params2;

#define VignetteColor           _Vignette_Params1.xyz
#define VignetteCenter          _Vignette_Params2.xy
#define VignetteIntensity       _Vignette_Params2.z
#define VignetteSmoothness      _Vignette_Params2.w
#define VignetteRoundness       _Vignette_Params1.w


half3 ApplyVignette(half3 input, float2 uv)
{
    return ApplyVignette(input, uv, VignetteCenter, VignetteIntensity, VignetteRoundness, VignetteSmoothness, VignetteColor);
}

#endif
