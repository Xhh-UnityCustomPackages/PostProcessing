#ifndef SCREEN_SPACE_REFLECTION_REPROJECT_INCLUDED
#define SCREEN_SPACE_REFLECTION_REPROJECT_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/DeclareMotionVectorTexture.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/BilateralFilter.hlsl"

#define MIN_GGX_ROUGHNESS           0.00001f
#define MAX_GGX_ROUGHNESS           0.99999f

//
// Fragment shaders
//
float4 FragReproject(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    // 没有使用jitter 不考虑sceneview 依赖MotionVector
    half2 motionVector = SampleMotionVector(uv);
    float2 prevUV = uv - motionVector;

    float2 k = _BlitTexture_TexelSize.xy;

    float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);

    // 0 1 2
    // 3
    float4x4 top = float4x4(
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(0.0, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, 0.0))
    );

    //     0
    // 1 2 3
    float4x4 bottom = float4x4(
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, 0.0)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(0.0, k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, k.y))
    );

    // 简单的minmax
    float4 minimum = min(min(min(min(min(min(min(min(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);
    float4 maximum = max(max(max(max(max(max(max(max(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);

    float4 history = SAMPLE_TEXTURE2D(_HistoryTex, sampler_LinearClamp, prevUV);
    // 简单的clamp
    history = clamp(history, minimum, maximum);

    // alpha通道在移动端不一定有 简单的blend
    float blend = saturate(smoothstep(0.002 * _BlitTexture_TexelSize.z, 0.0035 * _BlitTexture_TexelSize.z, length(motionVector)));
    blend *= 0.85;

    float weight = clamp(lerp(0.95, 0.7, blend * 100.0), 0.7, 0.95);

    return lerp(color, history, weight);
}

//--------------------------------------------------------------------------------------------------
// Helpers
//--------------------------------------------------------------------------------------------------

// Weight for SSR where Fresnel == 1 (returns value/pdf)
float GetSSRSampleWeight(float3 V, float3 L, float roughness)
{
    // Simplification:
    // value = D_GGX / (lambdaVPlusOne + lambdaL);
    // pdf = D_GGX / lambdaVPlusOne;

    const float lambdaVPlusOne = Lambda_GGX(roughness, V) + 1.0;
    const float lambdaL = Lambda_GGX(roughness, L);

    return lambdaVPlusOne / (lambdaVPlusOne + lambdaL);
}

float Normalize01(float value, float minValue, float maxValue)
{
    return (value - minValue) / (maxValue - minValue);
}

float2 GetHitNDC(float2 positionNDC)
{
    // TODO: it's important to account for occlusion/disocclusion to avoid artifacts in motion.
    // This would require keeping the depth buffer from the previous frame.
    float2 motionVectorNDC;
    DecodeMotionVector(SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, min(positionNDC, 1.0f - 0.5f * _ScreenSize.zw) * _RTHandleScale.xy, 0), motionVectorNDC);
    float2 prevFrameNDC = positionNDC - motionVectorNDC;
    return prevFrameNDC;
}

float3 GetHitColor(float2 hitPositionNDC, float perceptualRoughness, out float opacity, int mipLevel = 0)
{
    float2 prevFrameNDC = GetHitNDC(hitPositionNDC);
    float2 prevFrameUV = prevFrameNDC;
    float tmpCoef = PerceptualRoughnessFade(perceptualRoughness, _SsrRoughnessFadeRcpLength, _SsrRoughnessFadeEndTimesRcpLength);
    opacity = tmpCoef;
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, prevFrameUV, mipLevel).rgb;
}

// Performs fading at the edge of the screen.
float EdgeOfScreenFade(float2 coordNDC, float fadeRcpLength)
{
    float2 coordCS = coordNDC * 2 - 1;
    float2 t = Remap10(abs(coordCS), fadeRcpLength, fadeRcpLength);
    return Smoothstep01(t.x) * Smoothstep01(t.y);
}

float4 FragSSRReprojection(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    uint2 positionSS0 = (uint2)(input.texcoord * _ScreenSize.xy);
    
    // half4 gbuffer1 = SAMPLE_TEXTURE2D_LOD(_GBuffer1, sampler_PointClamp, uv, 0);
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);
    
    // float3 N = normalize(UnpackNormal(gbuffer2.xyz));;
    float smoothness = 1 - gbuffer2.a;
    float perceptualRoughness = PerceptualRoughnessToRoughness(smoothness);
    
    float4 ssrTest = SAMPLE_TEXTURE2D(_SSR_TestTex, sampler_PointClamp, input.texcoord);
    float2 hitPositionNDC = ssrTest.xy;
    if (max(hitPositionNDC.x, hitPositionNDC.y) == 0)
    {
        // Miss.
        return 0;
    }
    
    float2 motionVectorNDC;
    DecodeMotionVector(SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, min(hitPositionNDC, 1.0f - 0.5f * _ScreenSize.zw) * _RTHandleScale.xy, 0), motionVectorNDC);
    float2 prevFrameNDC = hitPositionNDC - motionVectorNDC;
    float2 prevFrameUV = prevFrameNDC;
    // TODO: filtering is quite awful. Needs to be non-Gaussian, bilateral and anisotropic.
    float mipLevel = PerceptualRoughnessToMipmapLevel(perceptualRoughness);//lerp(0, _SsrColorPyramidMaxMip, perceptualRoughness);

    // if (any(prevFrameUV < float2(0.0,0.0)) || any(prevFrameUV > limit))
    // {
    //     // Off-Screen.
    //     return 0;
    // }

    float opacity = PerceptualRoughnessFade(perceptualRoughness, _SsrRoughnessFadeRcpLength, _SsrRoughnessFadeEndTimesRcpLength);
    opacity *= Attenuate(hitPositionNDC) * Vignette(hitPositionNDC);;
    
    // #ifdef DEBUG_SCREEN_SPACE_REFLECTION
    float3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, hitPositionNDC.xy, mipLevel);
    
    // Disable SSR for negative, infinite and NaN history values.
    uint3 intCol   = asuint(color);
    bool  isPosFin = Max3(intCol.r, intCol.g, intCol.b) < 0x7F800000;
    
    color   = isPosFin ? color   : 0;
    opacity = isPosFin ? opacity : 0;
    
    float fade = ssrTest.z;

    // fade是低频部分 理论上相当于一个锐化操作
    fade = (1.0 - saturate(fade * smoothstep(0.5, 1.0, fade) * _DistanceFade)) * opacity;
    
    
    return float4(color * _SSRIntensity, 1.0) * fade;
    // #endif
    
    
    
    #define BLOCK_SAMPLE_RADIUS 1
    
    int samplesCount = 0;
    float4 outputs = 0.0f;
    float wAll = 0.0f;
    for (int y = -BLOCK_SAMPLE_RADIUS; y <= BLOCK_SAMPLE_RADIUS; ++y)
    {
        for (int x = -BLOCK_SAMPLE_RADIUS; x <= BLOCK_SAMPLE_RADIUS; ++x)
        {
            if (abs(x) == abs(y) && abs(x) == 1)
                continue;
            
            uint2 positionSS = uint2(int2(positionSS0) + int2(x, y));
            float3 color;
            float opacity;
            float weight;
            // float2 hitData = GetSampleInfo(positionSS, color, weight, opacity);
        }
    }
    
    
    return half4(hitPositionNDC.xy, 0, 1);
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
