#ifndef LIGHTSHAFT_PASS_HLSL
#define LIGHTSHAFT_PASS_HLSL

#define NUM_SAMPLES 24

half4 LightShaftsOcclusionPrefilterPassFragment(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    float4 sceneColor = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

    float sceneDepth = SampleSceneDepth(uv);
    sceneDepth = Linear01Depth(sceneDepth, _ZBufferParams);

    float edgeMask = 1.0f - uv.x * (1.0f - uv.x) * uv.y * (1.0f - uv.y) * 8.0f;
    edgeMask = edgeMask * edgeMask * edgeMask * edgeMask;
    float invOcclusionDepthRange = _LightShaftParameters.x;
    //filter the occlusion mask instead of the depths
    float occlusionMask = saturate(sceneDepth * invOcclusionDepthRange);
    occlusionMask = max(occlusionMask, edgeMask * .8f);

    return float4(occlusionMask.xxx, 1);
}


float4 LightShaftsBloomPrefilterPassFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    float4 sceneColor = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    float sceneDepth = SampleSceneDepth(uv);
    sceneDepth = Linear01Depth(sceneDepth, _ZBufferParams);
    //setup a mask that is 1 at the edges of the screen and 0 at the center
    float edgeMask = 1.0f - uv.x * (1.0f - uv.x) * uv.y * (1.0f - uv.y) * 8.0f;
    edgeMask = edgeMask * edgeMask * edgeMask * edgeMask;
    float invOcclusionDepthRange = _LightShaftParameters.x;
    //only bloom colors over bloomThreshold
    float luminance = max(dot(sceneColor.rgb, float3(.3f, .59f, .11f)), 6.10352e-5);
    float adjustedLuminance = max(luminance - _BloomTintAndThreshold.a, 0.0f);
    float3 bloomColor = _LightShaftParameters.y * sceneColor.rgb / luminance * adjustedLuminance * 2.0f;
    //only allow bloom from pixels whose depth are in the far half of OcclusionDepthRange
    float bloomDistanceMask = saturate((sceneDepth - 0.5f / invOcclusionDepthRange) * invOcclusionDepthRange);
    //setup a mask that is 0 at light source and increases to 1 over distance
    float screenRatio = 1;//_CameraBufferSize.z / _CameraBufferSize.w;
    float blurOriginDistanceMask = 1.0f - saturate(length(_LightSource.xy - uv) * screenRatio * _LightShaftParameters.z);
    //calculate bloom color with masks applied
    bloomColor = saturate(bloomColor * _BloomTintAndThreshold.rgb * bloomDistanceMask * (1.0f - edgeMask) * blurOriginDistanceMask * blurOriginDistanceMask);
    return float4(bloomColor, 1);
}

float4 LightShaftsBlurFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    float3 blurredValues = 0.0;
    float passScale = pow(0.4 * NUM_SAMPLES, _RadialBlurParameters.y);
    //vectors from pixel to light source
    float2 blurVector = _LightSource.xy - uv;
    blurVector *= min(_RadialBlurParameters.z * passScale, 1);
    //divide by number of samples and scale by control factor.
    float2 delta = blurVector / NUM_SAMPLES * _ShaftsDensity;
    //set up illumination decay factor.
    float illuminationDecay = 1.0f;
    for (int i = 0; i < (_LightSource.z < 0 ? 0 : NUM_SAMPLES); i++)
    {
        float2 sampleUV = uv + delta * i;
        float3 sampleValue = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, sampleUV, 0).rgb;
        //apply sample attenuation scale/decay factors.
        sampleValue *= illuminationDecay * (_ShaftsWeight / NUM_SAMPLES);
        //accumulate combined color.
        blurredValues += sampleValue;
        //update exponential decay factor.
        illuminationDecay *= _ShaftsDecay;
    }
    return float4(blurredValues * _ShaftsExposure, 1);
}



float4 LightShaftsOcclusionBlendFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    float4 godsRayBlur = SAMPLE_TEXTURE2D_LOD(_LightShafts1, sampler_LightShafts1, uv, 0);
    float4 sceneColor = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    return float4((_LightSource.z < 0 ? sceneColor.rgb * _ShaftsExposure * _ShaftsExposure : sceneColor.rgb * godsRayBlur.x), 1);
}

float4 LightShaftsBloomBlendFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    float4 godsRayBlur = SAMPLE_TEXTURE2D_LOD(_LightShafts1, sampler_LightShafts1, uv, 0);
    float4 sceneColor = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    return float4(sceneColor.rgb + godsRayBlur.rgb, 1);
}

#endif
