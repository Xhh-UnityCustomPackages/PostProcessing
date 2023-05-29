#ifndef TEMPORAL_ANTIALIASING_LITE_INCLUDED
#define TEMPORAL_ANTIALIASING_LITE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

#define CLAMP_MAX 65472.0 // (2 - 2^-9) * 2^15

TEXTURE2D(_SourceTex);
TEXTURE2D(_HistoryTex);
TEXTURE2D_FLOAT(_MotionVectorTexture);

float4 _SourceTex_TexelSize;
float4 _CameraDepthTexture_TexelSize;
float4 _HistoryTex_TexelSize;

float4x4 _PrevViewProjectionMatrix;
float2 _Jitter;
float4 _FinalBlendParameters; // x: static, y: dynamic, z: motion amplification
float _Sharpness;

#if defined(UNITY_REVERSED_Z)
    #define COMPARE_DEPTH(a, b) step(b, a)
#else
    #define COMPARE_DEPTH(a, b) step(a, b)
#endif

float2 GetClosestFragment(float depth, float2 uv)
{
    float2 k = _CameraDepthTexture_TexelSize.xy;

    // 只用了斜角 tl tr bl br 节省性能
    float3 tl = float3(-1, -1, SampleSceneDepth(uv - k));
    float3 tr = float3(1, -1, SampleSceneDepth(uv + float2(k.x, -k.y)));
    float3 mc = float3(0, 0, depth);
    float3 bl = float3(-1, 1, SampleSceneDepth(uv + float2(-k.x, k.y)));
    float3 br = float3(1, 1, SampleSceneDepth(uv + k));

    float3 rmin = mc;
    rmin = lerp(rmin, tl, COMPARE_DEPTH(tl.z, rmin.z));
    rmin = lerp(rmin, tr, COMPARE_DEPTH(tr.z, rmin.z));
    rmin = lerp(rmin, bl, COMPARE_DEPTH(bl.z, rmin.z));
    rmin = lerp(rmin, br, COMPARE_DEPTH(br.z, rmin.z));

    return uv + rmin.xy * k;
}

// 没有MotionVector 只处理摄像机运动的偏移
float2 GetReprojection(float depth, float2 uv)
{
    #if UNITY_REVERSED_Z
        depth = 1.0 - depth;
    #endif

    depth = 2.0 * depth - 1.0;
    // 深度还原世界坐标 TODO 这里unity_CameraInvProjection需要传递
    float3 viewPos = ComputeViewSpacePosition(uv, depth, unity_CameraInvProjection);
    float4 worldPos = float4(mul(unity_CameraToWorld, float4(viewPos, 1.0)).xyz, 1.0);

    // 利用上一帧VP矩阵 找到当前坐标在上一帧的uv
    float4 prevClipPos = mul(_PrevViewProjectionMatrix, worldPos);
    float2 prevPosCS = prevClipPos.xy / prevClipPos.w;
    return prevPosCS * 0.5 + 0.5;
}

float4 ClipToAABB(float4 color, float3 minimum, float3 maximum)
{
    // Note: only clips towards aabb center (but fast!)
    float3 center = 0.5 * (maximum + minimum);
    float3 extents = 0.5 * (maximum - minimum);

    // This is actually `distance`, however the keyword is reserved
    float3 offset = color.rgb - center;

    float3 ts = abs(extents / (offset +0.0001));
    float t = saturate(Min3(ts.x, ts.y, ts.z));
    color.rgb = center + offset * t;
    return color;
}

// ------------------------------------------------------------------------------------

struct VaryingsTemporal
{
    float4 positionCS : SV_POSITION;
    float4 uv : TEXCOORD0;
};

VaryingsTemporal VertTemporal(Attributes input)
{
    VaryingsTemporal output;
    UNITY_SETUP_INSTANCE_ID(input);

    #if SHADER_API_GLES
        float4 pos = input.positionOS;
        float2 uv = input.uv;
    #else
        float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
        float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
    #endif

    output.positionCS = pos;
    output.uv.xy = uv;

    float4 ndc = output.positionCS * 0.5f;
    // 注意这里不要管_ProjectionParams.x
    output.uv.zw = ndc.xy + ndc.w;

    return output;
}

// ------------------------------------------------------------------------------------
float4 FragTemporal(VaryingsTemporal input) : SV_Target
{
    float depth = SampleSceneDepth(input.uv.xy);

    #if _USEMOTIONVECTOR
        float2 closest = GetClosestFragment(depth, input.uv.xy);
        float2 motionVector = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_LinearClamp, closest).xy;
        float2 prevUV = input.uv.xy - motionVector;
    #else
        float2 prevUV = GetReprojection(depth, input.uv.zw);
        float2 motionVector = input.uv.xy - prevUV;
    #endif

    float4 history = SAMPLE_TEXTURE2D(_HistoryTex, sampler_LinearClamp, prevUV);

    // ---------------------------------------------------------
    float2 uv = input.uv.xy - _Jitter;

    float4 color = SAMPLE_TEXTURE2D(_SourceTex, sampler_LinearClamp, uv);

    float4 topLeft = SAMPLE_TEXTURE2D(_SourceTex, sampler_LinearClamp, (uv - _SourceTex_TexelSize.xy * 0.5));
    float4 bottomRight = SAMPLE_TEXTURE2D(_SourceTex, sampler_LinearClamp, (uv + _SourceTex_TexelSize.xy * 0.5));
    float4 corners = 4.0 * (topLeft + bottomRight) - 2.0 * color;

    // Sharpen output
    color += (color - (corners * 0.166667)) * 2.718282 * _Sharpness;
    color = clamp(color, 0.0, CLAMP_MAX);

    // Tonemap color and history samples
    float4 average = (corners + color) * 0.142857;

    float motionLength = length(motionVector);
    float2 luma = float2(Luminance(average), Luminance(color));
    //float nudge = 4.0 * abs(luma.x - luma.y);
    float nudge = lerp(4.0, 0.25, saturate(motionLength * 100.0)) * abs(luma.x - luma.y);

    float4 minimum = min(bottomRight, topLeft) - nudge;
    float4 maximum = max(topLeft, bottomRight) + nudge;

    // Clip history samples
    history = ClipToAABB(history, minimum.xyz, maximum.xyz);

    // Blend method
    float weight = clamp(
        lerp(_FinalBlendParameters.x, _FinalBlendParameters.y, motionLength * _FinalBlendParameters.z),
        _FinalBlendParameters.y, _FinalBlendParameters.x
    );

    color = lerp(color, history, weight);
    color = clamp(color, 0.0, CLAMP_MAX);

    return color;
}

#endif // TEMPORAL_ANTIALIASING_LITE_INCLUDED
