#ifndef SCREEN_SPACE_REFLECTION_INCLUDED
#define SCREEN_SPACE_REFLECTION_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"

float4 FragSSRComposite(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    float4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

    float4 resolve = SAMPLE_TEXTURE2D(_SsrLightingTexture, sampler_LinearClamp, uv);
    #if DEBUG_SCREEN_SPACE_REFLECTION
    return resolve;
    #endif
    
    
    return sourceColor + resolve;
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
