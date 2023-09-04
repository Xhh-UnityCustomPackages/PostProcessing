//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VOLUMETRICLIGHTINCLUDE_CS_HLSL
#define VOLUMETRICLIGHTINCLUDE_CS_HLSL
// Generated from Game.Core.PostProcessing.VolumetricLightInclude
// PackingRules = Exact
CBUFFER_START(VolumetricLightInclude)
    float _Density;
    float _MaxRayLength;
    int _SampleCount;
    float _Intensity;
    float2 _RandomNumber;
    float4 _MieG;
    float2 _JitterOffset;
CBUFFER_END


#endif
