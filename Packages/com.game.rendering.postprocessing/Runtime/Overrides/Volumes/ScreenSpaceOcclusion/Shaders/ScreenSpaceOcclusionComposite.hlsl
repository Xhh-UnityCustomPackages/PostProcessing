#ifndef SCREEN_SPACE_OCCLUSION_COMPOSITE_INCLUDED
#define SCREEN_SPACE_OCCLUSION_COMPOSITE_INCLUDED

#include "ScreenSpaceOcclusionCommon.hlsl"

float4 FragComposite(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    float4 source = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
    #if DEBUG_VIEWNORMAL
        return source;
    #endif

    #if SCALABLE_AMBIENT_OBSCURANCE
        _Intensity *= 0.28125;
    #endif

    float ao = saturate(pow(abs(source.b), _Intensity));

    return ao;
}

#endif // SCREEN_SPACE_OCCLUSION_COMPOSITE_INCLUDED
