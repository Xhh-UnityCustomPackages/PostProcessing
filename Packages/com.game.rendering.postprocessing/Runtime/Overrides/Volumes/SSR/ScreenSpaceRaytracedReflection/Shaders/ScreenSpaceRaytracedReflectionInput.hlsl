#ifndef SCREEN_SPACE_RAYTRACED_REFLECTION_INPUT_INCLUDED
#define SCREEN_SPACE_RAYTRACED_REFLECTION_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"


TEXTURE2D(_GBuffer0);
TEXTURE2D(_GBuffer1);
TEXTURE2D(_GBuffer2);

TEXTURE2D(_MetallicGradientTex);
TEXTURE2D(_SmoothnessGradientTex);

TEXTURE2D(_DownscaledShinyDepthRT);
TEXTURE2D(_RayCastRT);

TEXTURE2D(_NoiseTex);   float4 _NoiseTex_TexelSize;
float4 _BlitTexture_TexelSize;


//TODO OPTIMIZE
TEXTURE2D_X(_BlurRTMip0);
TEXTURE2D_X(_BlurRTMip1);
TEXTURE2D_X(_BlurRTMip2);
TEXTURE2D_X(_BlurRTMip3);
TEXTURE2D_X(_BlurRTMip4);

float4x4 _WorldToViewDir;

float4 _SSRSettings;
float4 _SSRSettings2;
float4 _SSRSettings3;
float4 _SSRSettings4;
float4 _SSRSettings5;

#define THICKNESS                   _SSRSettings.x
#define SAMPLES                     _SSRSettings.y
#define BINARY_SEARCH_ITERATIONS    _SSRSettings.z
#define MAX_RAY_LENGTH              _SSRSettings.w
#define JITTER                      _SSRSettings2.x
#define CONTACT_HARDENING           _SSRSettings2.y
#define REFLECTIONS_MULTIPLIER      _SSRSettings2.z
#define REFLECTIVITY                _SSRSettings2.w
#define INPUT_SIZE                  _SSRSettings3.xy
#define GOLDEN_RATIO_ACUM           _SSRSettings3.z
#define DEPTH_BIAS                  _SSRSettings3.w
#define SEPARATION_POS              _SSRSettings4.x
#define REFLECTIONS_MIN_INTENSITY   _SSRSettings4.y
#define REFLECTIONS_MAX_INTENSITY   _SSRSettings4.z
#define DENOISE_POWER               _SSRSettings4.w

float4 _MaterialData;
#define SMOOTHNESS                  _MaterialData.x
#define FRESNEL                     _MaterialData.y
#define FUZZYNESS                   _MaterialData.z
#define DECAY                       _MaterialData.w

float4 _SSRBlurStrength;
#define BLUR_STRENGTH_HORIZ         _SSRBlurStrength.x
#define BLUR_STRENGTH_VERT          _SSRBlurStrength.y
#define VIGNETTE_SIZE               _SSRBlurStrength.z
#define VIGNETTE_POWER              _SSRBlurStrength.w

float _MinimumBlur;


#if SSR_THICKNESS_FINE
    #define THICKNESS_FINE _SSRSettings5.x
#else
    #define THICKNESS_FINE THICKNESS
#endif

#define dot2(x) dot(x, x)

inline half getLuma(float3 rgb)
{
    const half3 lum = float3(0.299, 0.587, 0.114);
    return dot(rgb, lum);
}

inline half3 GetScreenSpacePos(half2 uv, half depth)
{
    return half3(uv.xy * 2 - 1, depth.r);
}

inline half3 GetViewSpacePos(half3 screenPos, half4x4 _InverseProjectionMatrix)
{
    half4 viewPos = mul(_InverseProjectionMatrix, half4(screenPos, 1));
    return viewPos.xyz / viewPos.w;
}


inline float GetLinearDepth(float2 uv)
{
    float depth = SAMPLE_TEXTURE2D_X_LOD(_DownscaledShinyDepthRT, sampler_PointClamp, uv, 0).r;
    return depth;
}

#if defined(SSR_BLUR_HORIZ)
    #define SSR_FRAG_SETUP_GAUSSIAN_UV(i) float2 offset1 = float2(_BlitTexture_TexelSize.x * 1.3846153846 * BLUR_STRENGTH_HORIZ, 0); float2 offset2 = float2(_BlitTexture_TexelSize.x * 3.2307692308 * BLUR_STRENGTH_HORIZ, 0);
#else
    #define SSR_FRAG_SETUP_GAUSSIAN_UV(i) float2 offset1 = float2(0, _BlitTexture_TexelSize.y * 1.3846153846 * BLUR_STRENGTH_VERT); float2 offset2 = float2(0, _BlitTexture_TexelSize.y * 3.2307692308 * BLUR_STRENGTH_VERT);
#endif

#endif // SCREEN_SPACE_RAYTRACED_REFLECTION_INPUT_INCLUDED
