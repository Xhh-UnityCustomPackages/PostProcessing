#ifndef SHADER_VARIABLES_INCLUDED
#define SHADER_VARIABLES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Params
GLOBAL_CBUFFER_START(ShaderVariablesGlobal, b1)
    float4 _ColorPyramidUvScaleAndLimitPrevFrame;
CBUFFER_END

#endif
