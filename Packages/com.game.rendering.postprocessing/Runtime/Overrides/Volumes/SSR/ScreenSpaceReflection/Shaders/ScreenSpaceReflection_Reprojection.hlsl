#ifndef SCREEN_SPACE_REFLECTION_REPROJECT_INCLUDED
#define SCREEN_SPACE_REFLECTION_REPROJECT_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/DeclareMotionVectorTexture.hlsl"

float4 FragSSRReprojection(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);
    return color;
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
