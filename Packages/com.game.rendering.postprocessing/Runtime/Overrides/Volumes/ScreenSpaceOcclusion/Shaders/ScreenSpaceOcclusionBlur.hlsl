
#ifndef SCREEN_SPACE_OCCLUSION_BLUR_INCLUDED
#define SCREEN_SPACE_OCCLUSION_BLUR_INCLUDED

#if BLUR_RADIUS_2
    #define KERNEL_RADIUS  2
#elif BLUR_RADIUS_3
    #define KERNEL_RADIUS  3
#elif BLUR_RADIUS_4
    #define KERNEL_RADIUS  4
#elif BLUR_RADIUS_5
    #define KERNEL_RADIUS  5
#else
    #define KERNEL_RADIUS  0
#endif

#include "ScreenSpaceOcclusionCommon.hlsl"

float2 GetAOAndDepth(float2 uv, inout half ao, inout float depth)
{
    float4 source = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);

    // Linear01Depth = LinearEyeDepth / far
    depth = DecodeFloatRG(source.rg);

    ao = source.b;

    return source.rg;
}

float CrossBilateralWeight(float r, float d, float d0)
{
    const float sigma = (float)KERNEL_RADIUS * 0.5;
    const float falloff = 1.0 / (2.0 * sigma * sigma);

    // LinearEyeDepth = Linear01Depth * far
    float diff = (d0 - d) * _ProjectionParams.z * _BlurSharpness;
    return exp2( - (r * r) * falloff - diff * diff);
}

void ProcessRadius(float2 uv, float2 deltaUV, float d0, inout half totalAO, inout float totalW)
{
    half ao; float d; float2 uvr;
    UNITY_UNROLL
    for (int r = 1; r <= KERNEL_RADIUS; r++)
    {
        uvr = uv + r * deltaUV;
        GetAOAndDepth(uvr, ao, d);

        float w = CrossBilateralWeight(r, d, d0);

        totalAO += w * ao;
        totalW += w;
    }
}

float4 FragBlur(Varyings input, half2 deltaUV)
{
    float2 uv = input.texcoord;

    half totalAO; float depth; float totalW = 1.0;
    float2 depth01 = GetAOAndDepth(uv, totalAO, depth);
    
    ProcessRadius(uv, -deltaUV * _Scaled_TexelSize.xy, depth, totalAO, totalW);
    ProcessRadius(uv, deltaUV * _Scaled_TexelSize.xy, depth, totalAO, totalW);

    totalAO /= totalW;

    //
    return float4(depth01, totalAO, 1);
}

float4 FragBlurH(Varyings input) : SV_Target
{
    return FragBlur(input, half2(1, 0));
}
float4 FragBlurV(Varyings input) : SV_Target
{
    return FragBlur(input, half2(0, 1));
}

#endif // SCREEN_SPACE_OCCLUSION_BLUR_INCLUDED
