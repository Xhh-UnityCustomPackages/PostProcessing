//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef LIGHTSHAFTINCLUDE_CS_HLSL
#define LIGHTSHAFTINCLUDE_CS_HLSL
// Generated from Game.Core.PostProcessing.LightShaftInclude
// PackingRules = Exact
CBUFFER_START(LightShaftInclude)
    float4 _LightSource;
    float4 _LightShaftParameters;
    float4 _RadialBlurParameters;
    float _ShaftsDensity;
    float _ShaftsWeight;
    float _ShaftsDecay;
    float _ShaftsExposure;
    float4 _BloomTintAndThreshold; // x: r y: g z: b w: a 
    float _ShaftsAtten;
CBUFFER_END


#endif
