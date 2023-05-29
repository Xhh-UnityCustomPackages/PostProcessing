#ifndef SCREEN_SPACE_OCCLUSION_COMMON_INCLUDED
#define SCREEN_SPACE_OCCLUSION_COMMON_INCLUDED



// 深度还原View空间坐标
float3 ReconstructPositionVS(float2 uv)
{
    // _TargetScale 处理在half分辨率下一个像素的偏移
    // 使用过滤角色的深度
    float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv * _TargetScale.xy);

    depth = LinearEyeDepth(depth, _ZBufferParams);

    return float3((uv * _UVToView.xy + _UVToView.zw) * depth, depth);
}

// reconstructing normal accurately from depth buffer.
// https://atyuwen.github.io/posts/normal-reconstruction/
// https://wickedengine.net/2019/09/22/improved-normal-reconstruction-from-depth/
float3 ReconstructNormalVS(float2 uv, float2 delta, float3 positionVS)
{
    #if RECONSTRUCT_NORMAL_HIGH

        float3 rVS, lVS, tVS, bVS;
        rVS = ReconstructPositionVS(uv + float2(delta.x, 0));
        lVS = ReconstructPositionVS(uv + float2(-delta.x, 0));
        tVS = ReconstructPositionVS(uv + float2(0, delta.y));
        bVS = ReconstructPositionVS(uv + float2(0, -delta.y));

        rVS = rVS - positionVS; lVS = positionVS - lVS; tVS = tVS - positionVS; bVS = positionVS - bVS;
        float3 minH = (dot(rVS, rVS) < dot(lVS, lVS)) ? rVS : lVS;
        float3 minV = (dot(tVS, tVS) < dot(bVS, bVS)) ? tVS : bVS;
        
        float3 normalVS = normalize(cross(minH, minV));
    #elif RECONSTRUCT_NORMAL_MEDIUM

        float3 rVS, tVS;
        rVS = ReconstructPositionVS(uv + float2(delta.x, 0));
        tVS = ReconstructPositionVS(uv + float2(0, delta.y));
        float3 normalVS = normalize(cross(tVS - positionVS, positionVS - rVS));
    #elif RECONSTRUCT_NORMAL_LOW

        float3 normalVS = -normalize(cross(ddy(positionVS), ddx(positionVS)));
    #else

        float3 normalWS = SampleSceneNormals(uv * _TargetScale.xy);
        float3 normalVS = mul((float3x3)_WorldToCameraMatrix, normalWS);
        normalVS = float3(normalVS.x, -normalVS.yz);
    #endif

    return normalVS;
}

// Encoding/decoding [0..1) floats into 8 bit/channel RG. Note that 1.0 will not be encoded properly.
// https://aras-p.info/blog/2009/07/30/encoding-floats-to-rgba-the-final/
float2 EncodeFloatRG(float v)
{
    float2 kEncodeMul = float2(1.0, 255.0);
    float kEncodeBit = 1.0 / 255.0;
    float2 enc = kEncodeMul * v;
    enc = frac(enc);
    enc.x -= enc.y * kEncodeBit;
    return enc;
}
float DecodeFloatRG(float2 enc)
{
    float2 kDecodeDot = float2(1.0, 1 / 255.0);
    return dot(enc, kDecodeDot);
}

// Jimenez's "Interleaved Gradient Noise"
half JimenezNoise(const half2 xyPixelPos)
{
    return frac(52.9829189 * frac(dot(xyPixelPos, half2(0.06711056, 0.00583715))));
}

// Spatial Offsets and Directions - s2016_pbs_activision_occlusion - Slide 93
half2 GetSpatialDirectionsOffsets(half2 screenPos)
{
    half2 spatialDirectionsOffsets;

    #if defined(SHADER_API_MOBILE)
        const half2 xyPixelPos = ceil(screenPos);
        spatialDirectionsOffsets.x = JimenezNoise((half2)xyPixelPos);
        spatialDirectionsOffsets.y = (1.0 / 4.0) * (half) (frac((xyPixelPos.y - xyPixelPos.x) / 4.0) * 4.0);
    #else
        const int2 xyPixelPos = (int2) (screenPos);
        spatialDirectionsOffsets.x = JimenezNoise((half2)xyPixelPos);
        spatialDirectionsOffsets.y = (1.0 / 4.0) * (half) ((xyPixelPos.y - xyPixelPos.x) & 3);
    #endif

    return spatialDirectionsOffsets;
}

// Trigonometric function utility
half2 CosSin(half theta)
{
    half sn, cs;
    sincos(theta, sn, cs);
    return half2(cs, sn);
}

// max absolute error 9.0x10^-3
// Eberly's polynomial degree 1 - respect bounds
// 4 VGPR, 12 FR (8 FR, 1 QR), 1 scalar
// input [-1, 1] and output [0, PI]
float acosFast(float inX)
{
    float x = abs(inX);
    float res = -0.156583f * x + (0.5 * PI);
    res *= sqrt(1.0f - x);
    return (inX >= 0) ? res : PI - res;
}

// Pseudo random number generator with 2D coordinates
float UVRandom(float u, float v)
{
    float f = dot(float2(12.9898, 78.233), float2(u, v));
    return frac(43758.5453 * sin(f));
}


#endif // SCREEN_SPACE_OCCLUSION_COMMON_INCLUDED
