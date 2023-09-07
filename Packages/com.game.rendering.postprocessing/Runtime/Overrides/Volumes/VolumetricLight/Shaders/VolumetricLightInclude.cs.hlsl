//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VOLUMETRICLIGHTINCLUDE_CS_HLSL
#define VOLUMETRICLIGHTINCLUDE_CS_HLSL
// Generated from Game.Core.PostProcessing.VolumetricLightInclude
// PackingRules = Exact
CBUFFER_START(VolumetricLightInclude)
    float _MaxRayLength;
    int _SampleCount;
    float _Intensity;
    float _SkyboxExtinction;
    float _ScatteringCoef;
    float _ExtinctionCoef;
    float _MieG;
    float3 _LightDirection;
    float4 _LightColor; // x: r y: g z: b w: a
    float _NoiseIntensity;
    float _NoiseScale;
    float2 _NoiseOffset;
    float3 _NoiseVelocity;
CBUFFER_END


#endif
