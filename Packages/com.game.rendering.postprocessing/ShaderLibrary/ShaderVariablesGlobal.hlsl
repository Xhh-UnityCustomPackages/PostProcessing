#ifndef SHADER_VARIABLES_INCLUDED
#define SHADER_VARIABLES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Params
// GLOBAL_CBUFFER_START(ShaderVariablesGlobal, b1)
    float4x4 _GlobalViewMatrix;
    float4x4 _GlobalViewProjMatrix;
    float4x4 _GlobalInvViewProjMatrix;
    float4x4 _GlobalPrevInvViewProjMatrix;

    float4 _TaaFrameInfo;
    float4 _ColorPyramidUvScaleAndLimitPrevFrame;
// CBUFFER_END

#endif
