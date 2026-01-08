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
    #elif SPLIT_SCREEN_SPACE_REFLECTION
    
    if (uv.x < SEPARATION_POS - _BlitTexture_TexelSize.x * 1)
    {
        return sourceColor;
    }
    else if (uv.x < SEPARATION_POS + _BlitTexture_TexelSize.x * 1)
    {
        return 1.0;
    }
    
    #endif
    
    
    return sourceColor + resolve;
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
