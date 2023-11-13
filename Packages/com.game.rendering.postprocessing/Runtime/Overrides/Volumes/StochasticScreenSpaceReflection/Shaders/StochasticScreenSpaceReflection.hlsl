#ifndef STOCHASTIC_SCREEN_SPACE_REFLECTION_INCLUDED
#define STOCHASTIC_SCREEN_SPACE_REFLECTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

float4 FragResolve(Varyings input) : SV_Target
{

    float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord);


    return color * 0.5;
}

#endif
