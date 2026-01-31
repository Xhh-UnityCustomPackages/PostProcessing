#ifndef EVALUATE_EXPOSURE_INCLUDED
#define EVALUATE_EXPOSURE_INCLUDED

#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/Exposure.hlsl"

TEXTURE2D(_AutoExposureLUT);
    
half3 ApplyExposure(half3 input)
{
    half exposure = SAMPLE_TEXTURE2D_LOD(_AutoExposureLUT, sampler_LinearClamp, 0, 0);
    return input * exposure;
}

#endif
