#ifndef SCREEN_SPACE_REFLECTION_INPUT_INCLUDED
#define SCREEN_SPACE_REFLECTION_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
#include "ShaderVariablesScreenSpaceReflection.hlsl"

// Helper structs
//
struct Ray
{
    float3 origin;
    float3 direction;
};

struct Result
{
    bool isHit;

    float2 uv;
    float3 position;

    float iterationCount;
};

TEXTURE2D(_SsrHitPointTexture);
TEXTURE2D(_SsrLightingTexture);

TEXTURE2D(_SsrAccumPrev);

TEXTURE2D_HALF(_GBuffer2);

float4 _Params1;     // x: vignette intensity, y: distance fade, z: maximum march distance, w: intensity

//Debug
float SEPARATION_POS;

#define _Attenuation            .25
#define _VignetteIntensity      _Params1.x
#define _DistanceFade           1
#define _MaximumMarchDistance   _Params1.z
#define _MaximumIterationCount  _Params1.w
#define DOWNSAMPLE                  _SsrDownsamplingDivider
#define SSR_MINIMUM_ATTENUATION 0.275
#define SSR_ATTENUATION_SCALE (1.0 - SSR_MINIMUM_ATTENUATION)
#define SSR_VIGNETTE_SMOOTHNESS 5.

// 外面的thickness被当作了步长在用, 实际的thickness写死了
#define LINEAR_TRACE_2D_THICKNESS              0.1

float4 TransformViewToHScreen(float3 vpos, float2 screenSize)
{
    float4 cpos = mul(UNITY_MATRIX_P, float4(vpos, 0));
    cpos.xy = float2(cpos.x, cpos.y * _ProjectionParams.x) * 0.5 + 0.5 * cpos.w;
    cpos.xy *= screenSize;
    return cpos;
}

float GetSquaredDistance(float2 first, float2 second)
{
    first -= second;
    return dot(first, first);
}

// jitter dither map
static half dither[16] = {
    0.0, 0.5, 0.125, 0.625,
    0.75, 0.25, 0.875, 0.375,
    0.187, 0.687, 0.0625, 0.562,
    0.937, 0.437, 0.812, 0.312
};

inline float ScreenEdgeMask(float2 clipPos)
{
    float yDif = 1 - abs(clipPos.y);
    float xDif = 1 - abs(clipPos.x);
    [flatten]
    if (yDif < 0 || xDif < 0)
    {
        return 0;
    }
    float t1 = smoothstep(0, .2, yDif);
    float t2 = smoothstep(0, .1, xDif);
    return saturate(t2 * t1);
}

bool IsInfinityFar(float rawDepth)
{
    #if UNITY_REVERSED_Z
    // Case for platforms with REVERSED_Z, such as D3D.
    if (rawDepth < 0.00001)
        return true;
    #else
    // Case for platforms without REVERSED_Z, such as OpenGL.
    if(rawDepth > 0.9999)
        return true;
    #endif
    return false;
}

bool IntersectsDepthBuffer(half rayZMin, half rayZMax, half sceneZ, half layerThickness)
{
    return (rayZMax >= sceneZ - layerThickness) && (rayZMin <= sceneZ);
}

float PerceptualRoughnessFade(float perceptualRoughness, float fadeRcpLength, float fadeEndTimesRcpLength)
{
    float t = Remap10(perceptualRoughness, fadeRcpLength, fadeEndTimesRcpLength);
    return Smoothstep01(t);
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
