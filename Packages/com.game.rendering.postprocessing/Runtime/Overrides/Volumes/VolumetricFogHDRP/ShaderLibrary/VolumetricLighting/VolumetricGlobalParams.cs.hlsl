//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VOLUMETRICGLOBALPARAMS_CS_HLSL
#define VOLUMETRICGLOBALPARAMS_CS_HLSL
// Generated from Game.Core.PostProcessing.VolumetricGlobalParams
// PackingRules = Exact
GLOBAL_CBUFFER_START(VolumetricGlobalParams, b0)
    float4 _PlanetCenterRadius;
    float4 _PlanetUpAltitude;
    int _FogEnabled;
    int _PBRFogEnabled;
    int _EnableVolumetricFog;
    float _MaxFogDistance;
    float4 _FogColor;
    float _FogColorMode;
    float _GlobalMipBiasPow2;
    uint _RayTracingCheckerIndex;
    float4 _MipFogParameters;
    float4 _HeightFogBaseScattering;
    float _HeightFogBaseExtinction;
    float _HeightFogBaseHeight;
    float _GlobalFogAnisotropy;
    int _VolumetricFilteringEnabled;
    float2 _HeightFogExponents;
    int _FogDirectionalOnly;
    float _FogGIDimmer;
    float4 _VBufferViewportSize;
    float4 _VBufferLightingViewportScale;
    float4 _VBufferLightingViewportLimit;
    float4 _VBufferDistanceEncodingParams;
    float4 _VBufferDistanceDecodingParams;
    uint _VBufferSliceCount;
    float _VBufferRcpSliceCount;
    float _VBufferRcpInstancedViewCount;
    float _VBufferLastSliceDist;
    float4 _ShadowAtlasSize;
    float4 _CascadeShadowAtlasSize;
    float4 _AreaShadowAtlasSize;
    float4 _CachedShadowAtlasSize;
    float4 _CachedAreaShadowAtlasSize;
CBUFFER_END


#endif
