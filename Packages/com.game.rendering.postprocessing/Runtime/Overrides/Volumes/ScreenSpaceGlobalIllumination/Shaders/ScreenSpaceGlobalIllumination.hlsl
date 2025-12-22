#ifndef SCREEN_SPACE_GLOBAL_ILLUMINATION_INCLUDED
#define SCREEN_SPACE_GLOBAL_ILLUMINATION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"


TEXTURE2D_HALF(_GBuffer2);


//Denoise is weighted based on worldspace distance and alignment of normals
float3 edgeDenoise(float2 uv)
{
    float4 fragSample = SAMPLE_TEXTURE2D(_GBuffer2, sampler_PointClamp, uv);
    float3 fragNormal = fragSample.xyz;

    #if UNITY_REVERSED_Z
        float depth = SampleSceneDepth(uv);
    #else
        //  调整 Z 以匹配 OpenGL 的 NDC ([-1, 1])
        float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
    #endif

    float3 posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

    int kSize2 = 4;
    float jank = 3.0;


    float weight = 0.0;
    float3 col = 0;
    float kSize = kSize2;
    for (float i = -kSize; i <= kSize; i++)
    {
        for (float j = -kSize; j <= kSize; j++)
        {
            float2 sampleUV = (uv + float2(i, j) * jank * _BlitTexture_TexelSize.xy) ;
            //GBuffer normal depth
            float4 dSample = SAMPLE_TEXTURE2D(_GBuffer2, sampler_PointClamp, sampleUV);
            float3 sampleNorm = dSample.xyz;


            #if UNITY_REVERSED_Z
                float sampleDepth = SampleSceneDepth(sampleUV);
            #else
                //  调整 Z 以匹配 OpenGL 的 NDC ([-1, 1])
                float sampleDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(sampleUV));
            #endif


            float3 samplePos = ComputeWorldSpacePosition(uv, sampleDepth, UNITY_MATRIX_I_VP);
            float normAlignment = clamp(dot(sampleNorm, fragNormal), 0.0, 1.0);
            float delta = distance(samplePos, posWS) * 0.1;
            float3 sampleCol = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, sampleUV);
            float sampleWeight = normAlignment / (delta + 1e-2);
            weight += sampleWeight;
            col += sampleCol * sampleWeight;
        }
    }
    return col / weight;
}

half4 Frag(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    float3 color = edgeDenoise(uv);
    return half4(color, 1);
    // return SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);

}

#endif  // SCREEN_SPACE_GLOBAL_ILLUMINATION_INCLUDED