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
CBUFFER_END


#endif
