#ifndef DECLARE_DEPTH_PYRAMID_INCLUDED
#define DECLARE_DEPTH_PYRAMID_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

StructuredBuffer<int2>  _DepthPyramidMipLevelOffsets;
TEXTURE2D_X(_DepthPyramid);

float LoadDepthPyramid(float2 positionSS)
{
    return LOAD_TEXTURE2D_X(_DepthPyramid, positionSS).r;
}

#endif