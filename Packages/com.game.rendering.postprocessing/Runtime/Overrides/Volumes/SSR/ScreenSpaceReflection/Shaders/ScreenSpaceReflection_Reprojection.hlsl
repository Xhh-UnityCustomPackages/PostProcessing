#ifndef SCREEN_SPACE_REFLECTION_REPROJECT_INCLUDED
#define SCREEN_SPACE_REFLECTION_REPROJECT_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/DeclareMotionVectorTexture.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/BilateralFilter.hlsl"

#define MIN_GGX_ROUGHNESS           0.00001f
#define MAX_GGX_ROUGHNESS           0.99999f

float3 CompositeSSRColor(float2 uv, float2 reflectUV, float mask, float2 offset)
{
    float3 ssrColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, reflectUV + offset, 0).rgb;
    // float3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + offset, 0).rgb;
    return lerp(0, ssrColor, mask * _SSRIntensity);
}

//
// Fragment shaders
//
float4 FragSSRAccumulation(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    
    float4 ssrColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);
    float4 ssrTest = SAMPLE_TEXTURE2D(_SsrHitPointTexture, sampler_PointClamp, uv);
    float2 hitPositionNDC = ssrTest.xy;
    
    float2 motionVectorNDC;
    DecodeMotionVector(SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, uv, 0), motionVectorNDC);
    float2 prevFrameNDC = hitPositionNDC - motionVectorNDC;
    float2 prevFrameUV = prevFrameNDC;

    
    float3 prevSsrColor = SAMPLE_TEXTURE2D_X_LOD(_SsrAccumPrev, sampler_LinearClamp, prevFrameUV, 0).rgb;
    float3 minColor = 9999.0, maxColor = -9999.0;
    for(int x = -1; x <= 1; ++x)
    {
        for(int y = -1; y <= 1; ++y)
        {
            float3 checkColor = CompositeSSRColor(uv, hitPositionNDC, 1, float2(x,y) * _BlitTexture_TexelSize.xy);
            minColor = min(minColor, checkColor); // Take min and max
            maxColor = max(maxColor, checkColor);
        }
    }
    // Clamp previous color to min/max bounding box
    prevSsrColor = clamp(prevSsrColor, minColor, 2 * maxColor);
    
    float3 blendedColor = lerp(prevSsrColor, ssrColor, 0.95);
    float luminance = dot(blendedColor, float3(0.299, 0.587, 0.114));
    float luminanceWeight = 1.0 / (1.0 + luminance);
    blendedColor = float4(blendedColor, 1.0) * luminanceWeight;
    
    return float4(blendedColor, 1);
}

//--------------------------------------------------------------------------------------------------
// Helpers
//--------------------------------------------------------------------------------------------------

float Attenuate(float2 uv)
{
    float offset = min(1.0 - max(uv.x, uv.y), min(uv.x, uv.y));

    float result = offset / (SSR_ATTENUATION_SCALE * _Attenuation + SSR_MINIMUM_ATTENUATION);
    result = saturate(result);

    return pow(result, 0.5);
}

float Vignette(float2 uv)
{
    float2 k = abs(uv - 0.5) * _VignetteIntensity;
    k.x *= _BlitTexture_TexelSize.y * _BlitTexture_TexelSize.z;
    return pow(saturate(1.0 - dot(k, k)), SSR_VIGNETTE_SMOOTHNESS);
}

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

float2 GetSampleInfo(uint2 positionSS, out float3 color, out float weight, out float opacity)
{
    float3 positionSrcWS;
    float3 positionDstWS;
    // float2 hitData = GetWorldSpacePoint(positionSS, positionSrcWS, positionDstWS);
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
    
    float4 ssrTest = SAMPLE_TEXTURE2D(_SsrHitPointTexture, sampler_PointClamp, input.texcoord);
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

    if (any(prevFrameUV < float2(0.0,0.0)) /*|| any(prevFrameUV > limit)*/)
    {
        // Off-Screen.
        return 0;
    }

    float opacity = PerceptualRoughnessFade(perceptualRoughness, _SsrRoughnessFadeRcpLength, _SsrRoughnessFadeEndTimesRcpLength);
    opacity *= Attenuate(hitPositionNDC) * Vignette(hitPositionNDC);
    //HDRP 过渡太生硬了 EdgeOfScreenFade
    
    //额外的渐变
    float fade = ssrTest.z;//命中概率
    fade = (1.0 - saturate(fade * smoothstep(0.5, 1.0, fade) * _DistanceFade));
    opacity *= fade;
    
    float3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, hitPositionNDC.xy, mipLevel);
    
    // Disable SSR for negative, infinite and NaN history values.
    uint3 intCol   = asuint(color);
    bool  isPosFin = Max3(intCol.r, intCol.g, intCol.b) < 0x7F800000;
    
    color   = isPosFin ? color   : 0;
    opacity = isPosFin ? opacity : 0;
    
    return float4(color * _SSRIntensity, 1.0) * opacity;


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
            float2 hitData = GetSampleInfo(positionSS, color, weight, opacity);
            if (max(hitData.x, hitData.y) != 0.0f && opacity > 0.0f)
            {
                //// Note that the color pyramid uses it's own viewport scale, since it lives on the camera.
                // Disable SSR for negative, infinite and NaN history values.
                uint3 intCol   = asuint(color);
                bool  isPosFin = Max3(intCol.r, intCol.g, intCol.b) < 0x7F800000;
                float2 prevFrameUV = hitData;

                color   = isPosFin ? color : 0;

                outputs += weight * float4(color, 1.0f);
                wAll += weight;
            }
        }
    }
    
    if (wAll > 0.0f)
    {
        uint3 intCol = asuint(outputs.rgb);
        bool  isPosFin = Max3(intCol.r, intCol.g, intCol.b) < 0x7F800000;

        outputs.rgb = isPosFin ? outputs.rgb : 0;
        opacity     = isPosFin ? opacity : 0;
        wAll = isPosFin ? wAll : 0;

        half4 ssrColor = opacity * outputs / wAll;
        ssrColor.rgb *= _SSRIntensity;
        return ssrColor;
    }
    
    return half4(hitPositionNDC.xy, 0, 1);
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
