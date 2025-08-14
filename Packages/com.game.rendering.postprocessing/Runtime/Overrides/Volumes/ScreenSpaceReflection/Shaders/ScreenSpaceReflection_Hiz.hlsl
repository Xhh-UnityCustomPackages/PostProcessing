#ifndef SCREEN_SPACE_REFLECTION_HIZ_INCLUDED
#define SCREEN_SPACE_REFLECTION_HIZ_INCLUDED

#include  "ScreenSpaceReflectionInput.hlsl"

//Hiz
TEXTURE2D(_HizDepthTexture);                SAMPLER(sampler_HizDepthTexture);
int _HizDepthTextureMipLevel;

float4 _HizDepthTexture_TexelSize;


bool ScreenSpaceRayMarchingHiz(half stepDirection, half end, inout float2 P, inout float3 Q, inout float k, float2 dP, float3 dQ,
    float dk, half rayZ, bool permute, inout int depthDistance, inout int stepCount, inout float2 hitUV, inout bool intersecting)
{
    bool stop = false;
    // 缓存当前深度和位置
    half prevZMaxEstimate = rayZ;
    half rayZMax = prevZMaxEstimate, rayZMin = prevZMaxEstimate;
    
    float mipLevel = 0.0;

    [loop]
    while ((P.x * stepDirection) <= end && stepCount < _MaximumIterationCount && !stop)
    {
        // 步近
        P += dP * exp2(mipLevel);
        Q += dQ * exp2(mipLevel);
        k += dk * exp2(mipLevel);
        stepCount += 1;

        // 得到步近前后两点的深度
        rayZMin = rayZ;
        rayZMax = (dQ.z * exp2(mipLevel) * 0.5 + Q.z) / (dk * exp2(mipLevel) * 0.5 + k);
        // prevZMaxEstimate = rayZMax;

        //确保rayZMin < rayZMax
        UNITY_FLATTEN
        if (rayZMin > rayZMax)
        {
            swap(rayZMin, rayZMax);
        }

        hitUV = permute ? P.yx : P;//恢复正确的坐标轴

        float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_HizDepthTexture, sampler_HizDepthTexture, hitUV * _TestTex_TexelSize.xy, mipLevel).r;
        float sceneZ = -LinearEyeDepth(rawDepth, _ZBufferParams);

        bool isBehind = (rayZMin <= sceneZ);//如果光线深度小于深度图深度
        // intersecting = isBehind && (rayZMax >= sceneZ - layerThickness);//光线与场景相交
        // stop = isBehind;
        
        if (!isBehind)
        {
            mipLevel = min(mipLevel + 1, _HizDepthTextureMipLevel);
        }
        else
        {
            if (mipLevel == 0)
            {
                if ((rayZMax >= sceneZ - layerThickness))
                {
                    intersecting = true;
                    stop = true;
                }
            }
            else
            {
                P -= dP * exp2(mipLevel);
                Q -= dQ * exp2(mipLevel);
                k -= dk * exp2(mipLevel);
                rayZ = Q.z / k;

                mipLevel--;
            }
        }
        
    }
    
    return intersecting;
}







#endif // SCREEN_SPACE_REFLECTION_INCLUDED
