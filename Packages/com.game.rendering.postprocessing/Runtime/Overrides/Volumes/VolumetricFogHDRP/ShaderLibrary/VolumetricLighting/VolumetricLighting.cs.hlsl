//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VOLUMETRICLIGHTING_CS_HLSL
#define VOLUMETRICLIGHTING_CS_HLSL
//
// Game.Core.PostProcessing.LocalVolumetricFogBlendingMode:  static fields
//
#define LOCALVOLUMETRICFOGBLENDINGMODE_OVERWRITE (0)
#define LOCALVOLUMETRICFOGBLENDINGMODE_ADDITIVE (1)
#define LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY (2)
#define LOCALVOLUMETRICFOGBLENDINGMODE_MIN (3)
#define LOCALVOLUMETRICFOGBLENDINGMODE_MAX (4)

//
// Game.Core.PostProcessing.LocalVolumetricFogFalloffMode:  static fields
//
#define LOCALVOLUMETRICFOGFALLOFFMODE_LINEAR (0)
#define LOCALVOLUMETRICFOGFALLOFFMODE_EXPONENTIAL (1)

// Generated from Game.Core.PostProcessing.LocalVolumetricFogEngineData
// PackingRules = Exact
struct LocalVolumetricFogEngineData
{
    float3 scattering;
    int falloffMode;
    float3 textureTiling;
    int invertFade;
    float3 textureScroll;
    float rcpDistFadeLen;
    float3 rcpPosFaceFade;
    float endTimesRcpDistFadeLen;
    float3 rcpNegFaceFade;
    int blendingMode;
};

// Generated from Game.Core.PostProcessing.ShaderVariablesVolumetric
// PackingRules = Exact
CBUFFER_START(ShaderVariablesVolumetric)
    float4x4 _VBufferCoordToViewDirWS[2];
    float _VBufferUnitDepthTexelSpacing;
    uint _NumVisibleLocalVolumetricFog;
    float _CornetteShanksConstant;
    uint _VBufferHistoryIsValid;
    float4 _VBufferSampleOffset;
    float _VBufferVoxelSize;
    float _HaveToPad;
    float _OtherwiseTheBuffer;
    float _IsFilledWithGarbage;
    float4 _VBufferPrevViewportSize;
    float4 _VBufferHistoryViewportScale;
    float4 _VBufferHistoryViewportLimit;
    float4 _VBufferPrevDistanceEncodingParams;
    float4 _VBufferPrevDistanceDecodingParams;
    uint _NumTileBigTileX;
    uint _NumTileBigTileY;
    uint _MaxSliceCount;
    float _MaxVolumetricFogDistance;
    float4 _CameraRight;
    float4x4 _CameraInverseViewProjection_NO;
    uint _VolumeCount;
    uint _IsObliqueProjectionMatrix;
    uint _Padding1;
    uint _Padding2;
CBUFFER_END

//
// Accessors for Game.Core.PostProcessing.LocalVolumetricFogEngineData
//
float3 GetScattering(LocalVolumetricFogEngineData value)
{
    return value.scattering;
}
int GetFalloffMode(LocalVolumetricFogEngineData value)
{
    return value.falloffMode;
}
float3 GetTextureTiling(LocalVolumetricFogEngineData value)
{
    return value.textureTiling;
}
int GetInvertFade(LocalVolumetricFogEngineData value)
{
    return value.invertFade;
}
float3 GetTextureScroll(LocalVolumetricFogEngineData value)
{
    return value.textureScroll;
}
float GetRcpDistFadeLen(LocalVolumetricFogEngineData value)
{
    return value.rcpDistFadeLen;
}
float3 GetRcpPosFaceFade(LocalVolumetricFogEngineData value)
{
    return value.rcpPosFaceFade;
}
float GetEndTimesRcpDistFadeLen(LocalVolumetricFogEngineData value)
{
    return value.endTimesRcpDistFadeLen;
}
float3 GetRcpNegFaceFade(LocalVolumetricFogEngineData value)
{
    return value.rcpNegFaceFade;
}
int GetBlendingMode(LocalVolumetricFogEngineData value)
{
    return value.blendingMode;
}

#endif
