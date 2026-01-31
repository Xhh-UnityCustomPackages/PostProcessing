#ifndef STOCHASTIC_SCREEN_SPACE_REFLECTION_INCLUDED
#define STOCHASTIC_SCREEN_SPACE_REFLECTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

#include "TraceLibrary.hlsl"


TEXTURE2D_HALF(_GBuffer0);
TEXTURE2D_HALF(_GBuffer1);
TEXTURE2D_HALF(_GBuffer2);

TEXTURE2D(_SSR_Noise);
TEXTURE2D(_SSR_RayCastRT);  SAMPLER(sampler_SSR_RayCastRT);
TEXTURE2D(_SSR_RayMask_RT); SAMPLER(sampler_SSR_RayMask_RT);
TEXTURE2D(_SSR_Spatial_RT); SAMPLER(sampler_SSR_Spatial_RT);
TEXTURE2D(_SSR_TemporalPrev_RT);
TEXTURE2D(_SSR_TemporalCurr_RT);
TEXTURE2D(_SSR_PreintegratedGF_LUT);

TEXTURE2D_FLOAT(_MotionVectorTexture);


int _SSR_NumSteps_Linear, _SSR_NumSteps_HiZ, _SSR_NumRays, _SSR_NumResolver, _SSR_CullBack, _SSR_BackwardsRay, _SSR_TraceBehind, _SSR_RayStepSize, _SSR_TraceDistance, _SSR_HiZ_MaxLevel, _SSR_HiZ_StartLevel, _SSR_HiZ_StopLevel, _SSR_HiZ_PrevDepthLevel;

half _SSR_BRDFBias, _SSR_ScreenFade, _SSR_TemporalScale, _SSR_TemporalWeight, _SSR_Thickness;

half3 _SSR_CameraClipInfo;

half4 _SSR_ScreenSize, _SSR_RayCastSize, _SSR_NoiseSize, _SSR_Jitter, _SSR_RandomSeed, _SSR_ProjInfo;

float4x4 _SSR_ProjectionMatrix, _SSR_InverseProjectionMatrix, _SSR_ViewProjectionMatrix, _SSR_InverseViewProjectionMatrix, _SSR_LastFrameViewProjectionMatrix, _SSR_WorldToCameraMatrix, _SSR_CameraToWorldMatrix, _SSR_ProjectToPixelMatrix;


////////////////////////////////////////////////
float pow2(float x)
{
    return x * x;
}

float2 pow2(float2 x)
{
    return x * x;
}

float3 pow2(float3 x)
{
    return x * x;
}

float4 pow2(float4 x)
{
    return x * x;
}

float Square(float x)
{
    return x * x;
}

float2 Square(float2 x)
{
    return x * x;
}

inline half3 GetScreenSpacePos(half2 uv, half depth)
{
    return half3(uv.xy * 2 - 1, depth.r);
}

inline half3 GetWorldSpacePos(half3 screenPos, half4x4 _InverseViewProjectionMatrix)
{
    half4 worldPos = mul(_InverseViewProjectionMatrix, half4(screenPos, 1));
    return worldPos.xyz / worldPos.w;
}

inline half3 GetViewDir(half3 worldPos, half3 ViewPos)
{
    return normalize(worldPos - ViewPos);
}

inline half3 GetViewSpacePos(half3 screenPos, half4x4 _InverseProjectionMatrix)
{
    half4 viewPos = mul(_InverseProjectionMatrix, half4(screenPos, 1));
    return viewPos.xyz / viewPos.w;
}


inline half2 GetMotionVector(half SceneDepth, half2 inUV, half4x4 _InverseViewProjectionMatrix, half4x4 _PrevViewProjectionMatrix, half4x4 _ViewProjectionMatrix)
{
    half3 screenPos = GetScreenSpacePos(inUV, SceneDepth);
    half4 worldPos = half4(GetWorldSpacePos(screenPos, _InverseViewProjectionMatrix), 1);

    half4 prevClipPos = mul(_PrevViewProjectionMatrix, worldPos);
    half4 curClipPos = mul(_ViewProjectionMatrix, worldPos);

    half2 prevHPos = prevClipPos.xy / prevClipPos.w;
    half2 curHPos = curClipPos.xy / curClipPos.w;

    half2 vPosPrev = (prevHPos.xy + 1) / 2;
    half2 vPosCur = (curHPos.xy + 1) / 2;
    return vPosCur - vPosPrev;
}

//////////////////////////

float3x3 GetTangentBasis(float3 TangentZ)
{
    float3 UpVector = abs(TangentZ.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 TangentX = normalize(cross(UpVector, TangentZ));
    float3 TangentY = cross(TangentZ, TangentX);
    return float3x3(TangentX, TangentY, TangentZ);
}

float3 TangentToWorld(float3 Vec, float3 TangentZ)
{
    return mul(Vec, GetTangentBasis(TangentZ));
}

float4 TangentToWorld(float3 Vec, float4 TangentZ)
{
    half3 T2W = TangentToWorld(Vec, TangentZ.rgb);
    return half4(T2W, TangentZ.a);
}

float4 ImportanceSampleGGX(float2 E, float Roughness)
{
    float m = Roughness * Roughness;
    float m2 = m * m;

    float Phi = 2 * PI * E.x;
    float CosTheta = sqrt((1 - E.y) / (1 + (m2 - 1) * E.y));
    float SinTheta = sqrt(1 - CosTheta * CosTheta);

    float3 H = float3(SinTheta * cos(Phi), SinTheta * sin(Phi), CosTheta);

    float d = (CosTheta * m2 - CosTheta) * CosTheta + 1;
    float D = m2 / (PI * d * d);

    float PDF = D * CosTheta;
    return float4(H, PDF);
}

float Vis_SmithGGXCorrelated(half NoL, half NoV, half Roughness)
{
    float a = Roughness * Roughness;
    float LambdaV = NoV * sqrt((-NoL * a + NoL) * NoL + a);
    float LambdaL = NoL * sqrt((-NoV * a + NoV) * NoV + a);
    return (0.5 / (LambdaL + LambdaV)) / PI;
}

half SSR_BRDF(half3 V, half3 L, half3 N, half Roughness)
{
    half3 H = normalize(L + V);

    half NoH = max(dot(N, H), 0);
    half NoL = max(dot(N, L), 0);
    half NoV = max(dot(N, V), 0);

    half D = D_GGX(NoH, Roughness);
    half G = Vis_SmithGGXCorrelated(NoL, NoV, Roughness);

    return max(0, D * G);
}

////////////////////////////////-----Linear_2DTrace Sampler-----------------------------------------------------------------------------
void Linear_2DTrace_SingleSPP(Varyings input, out half4 RayHit_PDF : SV_Target0, out half4 Mask : SV_Target1)
{
    float2 uv = input.texcoord;

    float2 _TargetScale = 1;
    float SceneDepth = SampleSceneDepth(uv * _TargetScale.xy);

    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);
    half Roughness = clamp(1 - gbuffer2.a, 0.02, 1);

    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float3 normalVS = mul((float3x3)_SSR_WorldToCameraMatrix, normalWS);

    half3 ScreenPos = GetScreenSpacePos(uv, SceneDepth);
    half3 WorldPos = GetWorldSpacePos(ScreenPos, _SSR_InverseViewProjectionMatrix);
    half3 ViewPos = GetViewSpacePos(ScreenPos, _SSR_InverseProjectionMatrix);
    half3 ViewDir = GetViewDir(WorldPos, _WorldSpaceCameraPos);


    //-----Consten Property-------------------------------------------------------------------------
    half Ray_HitMask = 0.0, Ray_NumMarch = 0.0;
    half2 Ray_HitUV = 0.0;
    half3 Ray_HitPoint = 0.0;

    //-----Trace Start-----------------------------------------------------------------------------
    half4 Screen_TexelSize = half4(1 / _SSR_ScreenSize.x, 1 / _SSR_ScreenSize.y, _SSR_ScreenSize.x, _SSR_ScreenSize.y);
    half3 Ray_Origin_VS = GetPosition(Screen_TexelSize, _SSR_ProjInfo, uv);
    half Ray_Bump = max(-0.01 * Ray_Origin_VS.z, 0.001);

    //使用blue noise
    half2 Hash = SAMPLE_TEXTURE2D_LOD(_SSR_Noise, sampler_PointClamp, (uv + _SSR_Jitter.zw) * _SSR_RayCastSize.xy / _SSR_NoiseSize.xy, 0).xy;
    half Jitter = Hash.x + Hash.y;
    Hash.y = lerp(Hash.y, 0.0, _SSR_BRDFBias);

    half4 H = 0.0;
    if (Roughness > 0.1)
    {
        H = TangentToWorld(ImportanceSampleGGX(Hash, Roughness), half4(normalVS, 1.0));
    }
    else
    {
        H = half4(normalVS, 1.0);
    }
    half3 Ray_Dir_VS = reflect(normalize(Ray_Origin_VS), H);

    //-----BackwardRay-----------------------------------------------------------------------------
    UNITY_BRANCH
    if (_SSR_BackwardsRay == 0 && Ray_Dir_VS.z > 0)
    {
        RayHit_PDF = 0;
        Mask = 0;
        return;
    }

    //-----Ray Trace-----------------------------------------------------------------------------
    bool Hit = Linear2D_Trace(Ray_Origin_VS + normalVS * Ray_Bump, Ray_Dir_VS, _SSR_ProjectToPixelMatrix, _SSR_ScreenSize, Jitter, _SSR_NumSteps_Linear, _SSR_Thickness, _SSR_TraceDistance, Ray_HitUV, _SSR_RayStepSize, _SSR_TraceBehind == 1, Ray_HitPoint, Ray_NumMarch);
    Ray_HitUV /= _SSR_ScreenSize;

    UNITY_BRANCH
    if (Hit)
    {
        Ray_HitMask = Square(1 - max(2 * half(Ray_NumMarch) / half(_SSR_NumSteps_Linear) - 1, 0));
        Ray_HitMask *= saturate(((_SSR_TraceDistance - dot(Ray_HitPoint - Ray_Origin_VS, Ray_Dir_VS))));

        if (_SSR_CullBack < 1)
        {
            half3 Ray_HitNormal_WS = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, Ray_HitUV, 0).rgb * 2 - 1;
            half3 Ray_Dir_WS = mul(_SSR_CameraToWorldMatrix, half4(Ray_Dir_VS, 0)).xyz;
            if (dot(Ray_HitNormal_WS, Ray_Dir_WS) > 0)
                Ray_HitMask = 0;
        }
    }

    RayHit_PDF = half4(Ray_HitUV, SampleSceneDepth(Ray_HitUV).r, H.a);
    Mask = Square(Ray_HitMask * GetScreenFadeBord(Ray_HitUV, _SSR_ScreenFade));
}






////////////////////////////////-----Spatio Sampler-----------------------------------------------------------------------------
//static const int2 offset[4] = { int2(0, 0), int2(0, 2), int2(2, 0), int2(2, 2) };
//static const int2 offset[9] ={int2(-1.0, -1.0), int2(0.0, -1.0), int2(1.0, -1.0), int2(-1.0, 0.0), int2(0.0, 0.0), int2(1.0, 0.0), int2(-1.0, 1.0), int2(0.0, 1.0), int2(1.0, 1.0)};
static const int2 offset[9] = {
    int2(-2.0, -2.0), int2(0.0, -2.0), int2(2.0, -2.0), int2(-2.0, 0.0), int2(0.0, 0.0), int2(2.0, 0.0), int2(-2.0, 2.0), int2(0.0, 2.0), int2(2.0, 2.0)
};
float4 Spatiofilter_SingleSPP(Varyings i) : SV_Target
{
    half2 UV = i.texcoord.xy;

    float2 _TargetScale = 1;
    float SceneDepth = SampleSceneDepth(UV * _TargetScale.xy);

    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, UV, 0);
    half Roughness = clamp(1 - gbuffer2.a, 0.02, 1);

    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float3 normalVS = mul((float3x3)_SSR_WorldToCameraMatrix, normalWS);
    half3 ScreenPos = GetScreenSpacePos(UV, SceneDepth);
    // half3 WorldPos = GetWorldSpacePos(ScreenPos, _SSR_InverseViewProjectionMatrix);
    half3 positionVS = GetViewSpacePos(ScreenPos, _SSR_InverseProjectionMatrix);

    half2 BlueNoise = SAMPLE_TEXTURE2D(_SSR_Noise, sampler_PointClamp, (UV + _SSR_Jitter.zw) * _SSR_ScreenSize.xy / _SSR_ScreenSize.xy).xy * 2 - 1;
    // half2 BlueNoise = tex2D(_SSR_Noise, (UV + _SSR_Jitter.zw) * _SSR_ScreenSize.xy / _SSR_NoiseSize.xy) * 2 - 1;
    half2x2 OffsetRotationMatrix = half2x2(BlueNoise.x, BlueNoise.y, -BlueNoise.y, -BlueNoise.x);

    half NumWeight, Weight;
    half2 Offset_UV, Neighbor_UV;
    half4 SampleColor, ReflecttionColor;

    for (int i = 0; i < _SSR_NumResolver; i++)
    {
        Offset_UV = mul(OffsetRotationMatrix, offset[i] * (1 / _SSR_ScreenSize.xy));
        Neighbor_UV = UV + Offset_UV;

        half4 HitUV_PDF = SAMPLE_TEXTURE2D_LOD(_SSR_RayCastRT, sampler_SSR_RayCastRT, Neighbor_UV, 0);
        half3 Hit_ViewPos = GetViewSpacePos(GetScreenSpacePos(HitUV_PDF.rg, HitUV_PDF.b), _SSR_InverseProjectionMatrix);

        ///SpatioSampler
        Weight = SSR_BRDF(normalize(-positionVS), normalize(Hit_ViewPos - positionVS), normalVS, Roughness) / max(1e-5, HitUV_PDF.a);
        SampleColor.rgb = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, HitUV_PDF.rg);
        // SampleColor.rgb = tex2Dlod(_SSR_SceneColor_RT, half4(HitUV_PDF.rg, 0, 0)).rgb;
        SampleColor.rgb /= 1 + Luminance(SampleColor.rgb);
        // SampleColor.a = tex2Dlod(_SSR_RayMask_RT, half4(Neighbor_UV, 0, 0)).r;
        SampleColor.a = SAMPLE_TEXTURE2D_LOD(_SSR_RayMask_RT, sampler_PointClamp, Neighbor_UV, 0).r;

        ReflecttionColor += SampleColor * Weight;
        NumWeight += Weight;
    }

    ReflecttionColor /= NumWeight;
    ReflecttionColor.rgb /= 1 - Luminance(ReflecttionColor.rgb);
    ReflecttionColor = max(1e-5, ReflecttionColor);
    //ReflecttionColor.a = tex2D(_SSR_RayMask_RT, UV).r;

    return ReflecttionColor;
}


inline void ResolverAABB(Texture2D currColor, half Sharpness, half ExposureScale, half AABBScale, half2 uv, half2 TexelSize, inout half Variance, inout half4 MinColor, inout half4 MaxColor, inout half4 FilterColor)
{
    const int2 SampleOffset[9] = {
        int2(-1.0, -1.0), int2(0.0, -1.0), int2(1.0, -1.0), int2(-1.0, 0.0), int2(0.0, 0.0), int2(1.0, 0.0), int2(-1.0, 1.0), int2(0.0, 1.0), int2(1.0, 1.0)
    };
    half4 SampleColors[9];

    for (uint i = 0; i < 9; i++)
    {
        #if AA_BicubicFilter
            half4 BicubicSize = half4(TexelSize, 1.0 / TexelSize);
            SampleColors[i] = Texture2DSampleBicubic(currColor, uv + (SampleOffset[i] / TexelSize), BicubicSize.xy, BicubicSize.zw);
        #else
            SampleColors[i] = SAMPLE_TEXTURE2D(currColor, sampler_LinearClamp, uv + (SampleOffset[i] / TexelSize));
        #endif
    }

    #if AA_Filter
        half SampleWeights[9];
        for (uint j = 0; j < 9; j++)
        {
            SampleWeights[j] = HdrWeight4(SampleColors[j].rgb, ExposureScale);
        }

        half TotalWeight = 0;
        for (uint k = 0; k < 9; k++)
        {
            TotalWeight += SampleWeights[k];
        }
        SampleColors[4] = (SampleColors[0] * SampleWeights[0] + SampleColors[1] * SampleWeights[1] + SampleColors[2] * SampleWeights[2] + SampleColors[3] * SampleWeights[3] + SampleColors[4] * SampleWeights[4] + SampleColors[5] * SampleWeights[5] + SampleColors[6] * SampleWeights[6] + SampleColors[7] * SampleWeights[7] + SampleColors[8] * SampleWeights[8]) / TotalWeight;
    #endif

    half4 m1 = 0.0; half4 m2 = 0.0;
    for (uint x = 0; x < 9; x++)
    {
        m1 += SampleColors[x];
        m2 += SampleColors[x] * SampleColors[x];
    }

    half4 mean = m1 / 9.0;
    half4 stddev = sqrt((m2 / 9.0) - pow2(mean));
    
    MinColor = mean - AABBScale * stddev;
    MaxColor = mean + AABBScale * stddev;

    FilterColor = SampleColors[4];
    MinColor = min(MinColor, FilterColor);
    MaxColor = max(MaxColor, FilterColor);

    half4 TotalVariance = 0;
    for (uint z = 0; z < 9; z++)
    {
        TotalVariance += pow2(Luminance(SampleColors[z]) - Luminance(mean));
    }
    Variance = saturate((TotalVariance / 9) * 256);
    Variance *= FilterColor.a;
}


////////////////////////////////-----Temporal Sampler-----------------------------------------------------------------------------
half4 Temporalfilter_SingleSPP(Varyings i) : SV_Target
{
    half2 UV = i.texcoord.xy;
    half HitDepth = SAMPLE_TEXTURE2D(_SSR_RayCastRT, sampler_SSR_RayCastRT, UV).b;

    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, UV, 0);
    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));

    // Get Reprojection Velocity
    half2 Depth_Velocity = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_LinearClamp, UV);
    half2 Ray_Velocity = GetMotionVector(HitDepth, UV, _SSR_InverseViewProjectionMatrix, _SSR_LastFrameViewProjectionMatrix, _SSR_ViewProjectionMatrix);
    half Velocity_Weight = saturate(dot(normalWS, half3(0, 1, 0)));
    half2 Velocity = lerp(Depth_Velocity, Ray_Velocity, Velocity_Weight);

    /////Get AABB ClipBox
    half SSR_Variance = 0;
    half4 SSR_CurrColor = 0;
    half4 SSR_MinColor, SSR_MaxColor;
    ResolverAABB(_SSR_Spatial_RT, 0, 10, _SSR_TemporalScale, UV, _SSR_ScreenSize.xy, SSR_Variance, SSR_MinColor, SSR_MaxColor, SSR_CurrColor);

    /////Clamp TemporalColor
    half4 SSR_PrevColor = SAMPLE_TEXTURE2D(_SSR_TemporalPrev_RT, sampler_LinearClamp, UV - Velocity);
    //half4 SSR_PrevColor = Bilateralfilter(_SSR_TemporalPrev_RT, UV - Velocity, _SSR_ScreenSize.xy);
    SSR_PrevColor = clamp(SSR_PrevColor, SSR_MinColor, SSR_MaxColor);

    /////Combine TemporalColor
    half Temporal_BlendWeight = saturate(_SSR_TemporalWeight * (1 - length(Velocity) * 8));
    half4 ReflectionColor = lerp(SSR_CurrColor, SSR_PrevColor, Temporal_BlendWeight);

    return ReflectionColor;
}


half3 GlossyEnvironmentReflectionSSR(half3 reflectVector, float3 positionWS, half perceptualRoughness, half occlusion)
{
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, reflectVector, mip));

    half3 irradiance = 0;

    #if defined(UNITY_USE_NATIVE_HDR) || defined(UNITY_DOTS_INSTANCING_ENABLED)
        irradiance = encodedIrradiance.rbg;
    #else
        irradiance = DecodeHDREnvironment(encodedIrradiance, 1);
    #endif // UNITY_USE_NATIVE_HDR || UNITY_DOTS_INSTANCING_ENABLED
    return irradiance * occlusion;
}

half4 PreintegratedDGF_LUT(Texture2D PreintegratedLUT, inout half3 EnergyCompensation, half3 SpecularColor, half Roughness, half NoV)
{
    half3 Enviorfilter_GFD = SAMPLE_TEXTURE2D_LOD(PreintegratedLUT, sampler_LinearClamp, half2(Roughness, NoV), 0.0).rgb;
    half3 ReflectionGF = lerp(saturate(50.0 * SpecularColor.g) * Enviorfilter_GFD.ggg, Enviorfilter_GFD.rrr, SpecularColor);

    #if Multi_Scatter
        EnergyCompensation = 1.0 + SpecularColor * (1.0 / Enviorfilter_GFD.r - 1.0);
    #else
        EnergyCompensation = 1.0;
    #endif

    return half4(ReflectionGF, Enviorfilter_GFD.b);
}

////////////////////////////////-----CombinePass-----------------------------------------------------------------------------
half4 CombineReflectionColor(Varyings i) : SV_Target
{
    half2 uv = i.texcoord.xy;

    half4 gbuffer1 = SAMPLE_TEXTURE2D_LOD(_GBuffer1, sampler_PointClamp, uv, 0);//这不是AO吗
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);
    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));

    BRDFData brdfData = BRDFDataFromGbuffer(0, gbuffer1, gbuffer2);
    

    float SceneDepth = SampleSceneDepth(uv);

    half3 ScreenPos = GetScreenSpacePos(uv, SceneDepth);
    half3 positionWS = GetWorldSpacePos(ScreenPos, _SSR_InverseViewProjectionMatrix);
    half3 ViewDir = GetViewDir(positionWS, _WorldSpaceCameraPos);
    half3 reflectVector = normalize(reflect(-ViewDir, normalWS));
    half NoV = saturate(dot(normalWS, ViewDir));
    half3 EnergyCompensation;
    //反射部分的采样
    half4 PreintegratedGF = half4(PreintegratedDGF_LUT(_SSR_PreintegratedGF_LUT, EnergyCompensation, brdfData.specular, brdfData.perceptualRoughness, NoV).rgb, 1);
    // PreintegratedGF = 1;
    // return PreintegratedGF;
    #if defined(_SCREEN_SPACE_OCCLUSION) // GBuffer never has transparents
        float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
        AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);
        half ReflectionOcclusion = aoFactor.directAmbientOcclusion;
    #else
        half ReflectionOcclusion = 1;
    #endif

    // half4 SceneColor = SAMPLE_TEXTURE2D_LOD(_SSR_SceneColor_RT, sampler_LinearClamp, uv, 0);
    half4 SceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);
    
    half4 CubemapColor = half4(GlossyEnvironmentReflectionSSR(reflectVector, positionWS, brdfData.perceptualRoughness, ReflectionOcclusion), 1);
    // CubemapColor = 0;//
    SceneColor.rgb = max(1e-5, SceneColor.rgb - CubemapColor.rgb);

    half4 SSRColor = SAMPLE_TEXTURE2D(_SSR_TemporalCurr_RT, sampler_LinearClamp, uv);
    half SSRMask = Square(SSRColor.a);
    half4 ReflectionColor = (CubemapColor * (1 - SSRMask)) + (SSRColor * PreintegratedGF * SSRMask * ReflectionOcclusion);


    #if DEBUG_SCREEN_SPACE_REFLECTION || DEBUG_INDIRECT_SPECULAR
        return half4(ReflectionColor.rgb, 1);
    #endif

    // return SSRMask;

    return SceneColor + ReflectionColor  ;
}

#endif
