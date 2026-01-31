#ifndef EVALUATE_SCREEN_SPACE_GLOBAL_ILLUMINATION_INCLUDED
#define EVALUATE_SCREEN_SPACE_GLOBAL_ILLUMINATION_INCLUDED

#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/Exposure.hlsl"

TEXTURE2D(_IndirectDiffuseTexture);

half4 SampleScreenSpaceGlobalIllumination(float2 normalizedScreenSpaceUV) 
{
    float2 positionSS = normalizedScreenSpaceUV * _ScreenSize.xy;
    float4 ssgiLighting = LOAD_TEXTURE2D(_IndirectDiffuseTexture, positionSS);
    ssgiLighting.rgb *= GetInverseCurrentExposureMultiplier();
    return ssgiLighting;
}

#endif
