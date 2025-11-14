#ifndef SCREEN_SPACE_CAVITY_INPUT_INCLUDED
#define SCREEN_SPACE_CAVITY_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"


//float4 _UVToView;
float4 _Input_TexelSize;
// URP 17 在其他HLSL定义了 在这不需要定义了
// float4 _BlitTexture_TexelSize;
float4x4 _WorldToCameraMatrix;

float _EffectIntensity;
float _DistanceFade;

float _CurvaturePixelRadius;
float _CurvatureBrights;
float _CurvatureDarks;

float _CavityWorldRadius;
float _CavityBrights;
float _CavityDarks;
int _CavitySamplesCount;
float2 _TargetScale;

TEXTURE2D(_CavityTex);



#define ACTUAL_CAVITY_SAMPLES _CavitySamplesCount


float _InterleavedGradientNoise(float2 pixCoord, int frameCount)
{
    const float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    float2 frameMagicScale = float2(2.083f, 4.867f);
    pixCoord += frameCount * frameMagicScale;
    return frac(magic.z * frac(dot(pixCoord, magic.xy)));
}

float3 PickSamplePoint(float2 uv, float randAddon, int index)
{
    float2 positionSS = uv * _BlitTexture_TexelSize.zw;
    float gn = _InterleavedGradientNoise(positionSS, index);
    float u = frac(gn) * 2.0 - 1.0;
    float theta = gn * 6.28318530717958647693;
    float sn, cs;
    sincos(theta, sn, cs);
    return float3(float2(cs, sn) * sqrt(1.0 - u * u), u);
}

float3x3 GetCoordinateConversionParameters(out float2 p11_22, out float2 p13_31)
{
    float3x3 camProj = (float3x3)unity_CameraProjection;
    //float3x3 camProj = (float3x3)/*UNITY_MATRIX_P*/_Projection;
    p11_22 = rcp(float2(camProj._11, camProj._22));
    p13_31 = float2(camProj._13, camProj._23);
    return camProj;
}

inline float FetchRawDepth(float2 uv)
{
    return SampleSceneDepth(uv * _TargetScale.xy);
}


inline float3 FetchViewPos(float2 uv)
{
    float depth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    //return float3((uv * _UVToView.xy + _UVToView.zw) * depth, depth);
    float4 UVToView = float4(2 / unity_CameraProjection._m00, -2 / unity_CameraProjection._m11, -1 / unity_CameraProjection._m00, 1 / unity_CameraProjection._m11);

    return float3((uv * UVToView.xy + UVToView.zw) * depth, depth);
}

inline float3 FetchViewNormals(float3 P, float2 uv)
{
    #if NORMALS_RECONSTRUCT
        float c = SampleSceneDepth(uv);
        half3 viewSpacePos_c = FetchViewPos(uv);
        // get data at 1 pixel offsets in each major direction
        half3 viewSpacePos_l = FetchViewPos(uv + float2(-1.0, 0.0) * _Input_TexelSize.xy);
        half3 viewSpacePos_r = FetchViewPos(uv + float2(+1.0, 0.0) * _Input_TexelSize.xy);
        half3 viewSpacePos_d = FetchViewPos(uv + float2(0.0, -1.0) * _Input_TexelSize.xy);
        half3 viewSpacePos_u = FetchViewPos(uv + float2(0.0, +1.0) * _Input_TexelSize.xy);
        half3 l = viewSpacePos_c - viewSpacePos_l;
        half3 r = viewSpacePos_r - viewSpacePos_c;
        half3 d = viewSpacePos_c - viewSpacePos_d;
        half3 u = viewSpacePos_u - viewSpacePos_c;
        half4 H = half4(
            SampleSceneDepth(uv + float2(-1.0, 0.0) * _Input_TexelSize.xy),
            SampleSceneDepth(uv + float2(+1.0, 0.0) * _Input_TexelSize.xy),
            SampleSceneDepth(uv + float2(-2.0, 0.0) * _Input_TexelSize.xy),
            SampleSceneDepth(uv + float2(+2.0, 0.0) * _Input_TexelSize.xy)
        );
        half4 V = half4(
            SampleSceneDepth(uv + float2(0.0, -1.0) * _Input_TexelSize.xy),
            SampleSceneDepth(uv + float2(0.0, +1.0) * _Input_TexelSize.xy),
            SampleSceneDepth(uv + float2(0.0, -2.0) * _Input_TexelSize.xy),
            SampleSceneDepth(uv + float2(0.0, +2.0) * _Input_TexelSize.xy)
        );
        half2 he = abs((2 * H.xy - H.zw) - c);
        half2 ve = abs((2 * V.xy - V.zw) - c);
        half3 hDeriv = he.x < he.y ? l : r;
        half3 vDeriv = ve.x < ve.y ? d : u;
        float3 N = normalize(cross(hDeriv, vDeriv));
    #else
        //GBUffer Mask
        float c = SampleSceneDepth(uv);
        c = 1 - step(c, 0.00001);

        float3 N = SampleSceneNormals(uv) * c;
        N = mul((float3x3)_WorldToCameraMatrix, N);
        
        N = float3(N.x, -N.yz);
    #endif

    N = float3(N.x, -N.y, N.z);
    return N;
}

float3 ReconstructViewPos(float2 uv, float depth, float2 p11_22, float2 p13_31)
{

    float3 viewPos = float3(depth * ((uv.xy * 2.0 - 1.0 - p13_31) * p11_22), depth);
    return viewPos;
}

void SampleDepthAndViewpos(float2 uv, float2 p11_22, float2 p13_31, out float depth, out float3 vpos)
{
    depth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    vpos = ReconstructViewPos(uv, depth, p11_22, p13_31);
}




//CURVATURE V
float CurvatureSoftClamp(float curvature, float control)
{
    if (curvature < 0.5 / control)
        return curvature * (1.0 - curvature * control);
    return 0.25 / control;
}
float Curvature(float2 uv, float3 P)
{
    float3 offset = float3(_Input_TexelSize.xy, 0.0) * (_CurvaturePixelRadius);

    float normal_up = FetchViewNormals(P, uv + offset.zy).g;
    float normal_down = FetchViewNormals(P, uv - offset.zy).g;
    float normal_right = FetchViewNormals(P, uv + offset.xz).r;
    float normal_left = FetchViewNormals(P, uv - offset.xz).r;

    float normal_diff = (normal_up - normal_down) + (normal_right - normal_left);

    //if (abs(normal_diff) <= 0.1) return 0; //slight low pass filter to remove noise from camera normals precision
    //new and improved low pass filter:
    //if (uv.x < 0.5)

    {
        if (normal_diff > 0.0) normal_diff = sign(normal_diff) * pow(normal_diff, 2.0);
        _CavityBrights += 0.5;
    }

    if (normal_diff >= 0.0)
        return 2.0 * CurvatureSoftClamp(normal_diff, _CurvatureBrights);
    else
        return -2.0 * CurvatureSoftClamp(-normal_diff, _CurvatureDarks);
}
//CURVATURE ^


#endif // SCREEN_SPACE_CAVITY_INPUT_INCLUDED
