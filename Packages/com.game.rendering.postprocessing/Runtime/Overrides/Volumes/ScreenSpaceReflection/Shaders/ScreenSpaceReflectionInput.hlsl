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
float4x4 _ScreenSpaceProjectionMatrixSSR;

int _MobileMode;

float4 _Params1;     // x: vignette intensity, y: distance fade, z: maximum march distance, w: intensity
float4 _Params2;    // x: aspect ratio, y: noise tiling, z: thickness, w: maximum iteration count

// 因为SSR无法稳定获取到正确的reflectionProbe和PerObjectData, 我们需要手动在SSR里面指定天空球并解析Environment Reflection Intensity Multiplier
half4 _Inutan_GlossyEnvironmentCubeMap_HDR;

float3 GetViewSpacePosition(float2 uv)
{
    float depth = SampleSceneDepth(uv);

    // 跨平台深度修正
    #if defined(UNITY_REVERSED_Z)
    #else
    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
    #endif
    float4 result = mul(_InverseProjectionMatrixSSR, float4(2.0 * uv - 1.0, depth, 1.0));
    return result.xyz / result.w;
}

float4 ProjectToScreenSpace(float3 position)
{
    return float4(
        _ScreenSpaceProjectionMatrixSSR[0][0] * position.x + _ScreenSpaceProjectionMatrixSSR[0][2] * position.z,
        _ScreenSpaceProjectionMatrixSSR[1][1] * position.y + _ScreenSpaceProjectionMatrixSSR[1][2] * position.z,
        _ScreenSpaceProjectionMatrixSSR[2][2] * position.z + _ScreenSpaceProjectionMatrixSSR[2][3],
        _ScreenSpaceProjectionMatrixSSR[3][2] * position.z
    );
}

void swap(inout float v0, inout float v1)
{  
    float temp = v0;  
    v0 = v1;    
    v1 = temp;
}  

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
