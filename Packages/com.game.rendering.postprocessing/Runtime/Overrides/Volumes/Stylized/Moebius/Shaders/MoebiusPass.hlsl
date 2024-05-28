#ifndef CRTSCREEN_PASS_HLSL
#define CRTSCREEN_PASS_HLSL

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"


float4 _BlitTexture_TexelSize;
float4 _UVToView;
float _NoiseIntensity;

TEXTURE2D(_SobelResultRT);
TEXTURE2D(_NoiseMap);


//亮度信息
float luminance(half4 color)
{
    return 0.2125 * color.r + 0.7154 * color.g + 0.0721 * color.b;
}

//自定义一个Sobel算子
half Sobel(float2 uv)
{
    //定义卷积核：
    const half Gx[9] = {
        - 1, 0, 1,
        - 2, 0, 2,
        - 1, 0, 1
    };
    const half Gy[9] = {
        - 1, -2, -1,
        0, 0, 0,
        1, 2, 1
    };
    half texColor;
    half edgeX = 0;
    half edgeY = 0;


    //依次对9个像素采样，计算明度值
    half color00 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(-1, -1)));
    half color10 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(0, -1)));
    half color20 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(1, -1)));

    half color01 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(-1, 0)));
    half color11 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(0, 0)));
    half color21 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(1, 0)));

    half color02 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(-1, 1)));
    half color12 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(0, 1)));
    half color22 = luminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + _BlitTexture_TexelSize.xy * half2(1, 1)));
    
    
    edgeX += color00 * Gx[0];
    edgeY += color00 * Gy[0];
    edgeX += color10 * Gx[1];
    edgeY += color10 * Gy[1];
    edgeX += color20 * Gx[2];
    edgeY += color20 * Gy[2];

    edgeX += color01 * Gx[3];
    edgeY += color01 * Gy[3];
    edgeX += color11 * Gx[4];
    edgeY += color11 * Gy[4];
    edgeX += color21 * Gx[5];
    edgeY += color21 * Gy[5];

    edgeX += color02 * Gx[6];
    edgeY += color02 * Gy[6];
    edgeX += color12 * Gx[7];
    edgeY += color12 * Gy[7];
    edgeX += color22 * Gx[8];
    edgeY += color22 * Gy[8];
    
    float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
    half skyMask = step(0.00001, depth);
    half edge = max(abs(edgeX), abs(edgeY)) * skyMask; //绝对值代替开根号求模，节省开销
    //half edge = 1 - pow(edgeX*edgeX + edgeY*edgeY, 0.5);
    return edge;
}

half4 FragSobelDepth(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;

    half2 noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_LinearClamp, uv);
    uv += noise * _NoiseIntensity * 0.01;
    // return half4(uv, 0, 1);

    half edge = Sobel(uv);
    return edge;
    half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    return color;
}


// 深度还原View空间坐标
float3 ReconstructPositionVS(float2 uv)
{
    // _TargetScale 处理在half分辨率下一个像素的偏移
    // 使用过滤角色的深度
    float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);

    depth = LinearEyeDepth(depth, _ZBufferParams);

    return float3((uv * _UVToView.xy + _UVToView.zw) * depth, depth);
}

half4 FragSobelNormal(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    return color;
}


half4 FragCombine(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    half4 sobel = SAMPLE_TEXTURE2D(_SobelResultRT, sampler_LinearClamp, uv);
    return color + sobel;
}

#endif
