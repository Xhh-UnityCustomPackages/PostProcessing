#ifndef SCREEN_SPACE_REFLECTION_INPUT_INCLUDED
#define SCREEN_SPACE_REFLECTION_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

// Helper structs
//
struct Ray
{
    float3 origin;
    float3 direction;
};

struct Segment
{
    float3 start;
    float3 end;

    float3 direction;
};

struct Result
{
    bool isHit;

    float2 uv;
    float3 position;

    float iterationCount;
};


//
// Uniforms
//
TEXTURE2D(_NoiseTex);
TEXTURE2D(_TestTex);
TEXTURE2D(_ResolveTex);

TEXTURE2D(_HistoryTex);
TEXTURE2D_FLOAT(_MotionVectorTexture);

TEXTURE2D_HALF(_GBuffer0);
TEXTURE2D_HALF(_GBuffer1);
TEXTURE2D_HALF(_GBuffer2);

// copy depth of gbuffer
TEXTURE2D_FLOAT(_MaskDepthRT);              SAMPLER(sampler_MaskDepthRT);

// minimapReflection
TEXTURE2D(_MinimapPlanarReflectTex);        SAMPLER(sampler_MinimapPlanarReflectTex);



float4 _BlitTexture_TexelSize;
float4 _TestTex_TexelSize;

float4x4 _ViewMatrixSSR;
float4x4 _InverseViewMatrixSSR;
float4x4 _InverseProjectionMatrixSSR;

int _MobileMode;

float4 _Params1;     // x: vignette intensity, y: distance fade, z: maximum march distance, w: intensity
float4 _Params2;    // x: aspect ratio, y: noise tiling, z: thickness, w: maximum iteration count

// 因为SSR无法稳定获取到正确的reflectionProbe和PerObjectData, 我们需要手动在SSR里面指定天空球并解析Environment Reflection Intensity Multiplier
half4 _Inutan_GlossyEnvironmentCubeMap_HDR;


#define _Attenuation            .25
#define _VignetteIntensity      _Params1.x
#define _DistanceFade           _Params1.y
#define _MaximumMarchDistance   _Params1.z
#define _Intensity              _Params1.w
#define _AspectRatio            _Params2.x
#define _NoiseTiling            _Params2.y
#define _Bandwidth              _Params2.z
#define _MaximumIterationCount  _Params2.w

#define SSR_MINIMUM_ATTENUATION 0.275
#define SSR_ATTENUATION_SCALE (1.0 - SSR_MINIMUM_ATTENUATION)
#define SSR_VIGNETTE_SMOOTHNESS 5.
#define SSR_KILL_FIREFLIES 0

// 外面的thickness被当作了步长在用, 实际的thickness写死了
#define Thickness              0.05

float3 GetNormalWS(float2 uv)
{
    //Deferred
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);
    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    //forward
    return normalWS;
}

float3 GetViewSpacePosition(float rawDepth, float2 uv)
{
    // float rawDepth = SampleSceneDepth(uv);

    // 跨平台深度修正
    #if defined(UNITY_REVERSED_Z)
    #else
    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
    #endif
    float4 result = mul(_InverseProjectionMatrixSSR, float4(2.0 * uv - 1.0, rawDepth, 1.0));
    return result.xyz / result.w;
}

float4 TransformViewToHScreen(float3 vpos, float2 screenSize)
{
    float4 cpos = mul(UNITY_MATRIX_P, vpos);
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

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
