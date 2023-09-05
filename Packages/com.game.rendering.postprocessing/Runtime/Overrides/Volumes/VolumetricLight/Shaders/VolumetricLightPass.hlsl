#ifndef VOLUMETRICLIGHT_PASS_HLSL
#define VOLUMETRICLIGHT_PASS_HLSL

// 获取阴影值
float GetShadowAttenuation(float3 posWS)
{
    // sample cascade shadow map
    float4 shadowCoord = TransformWorldToShadowCoord(posWS);
    float atten = MainLightRealtimeShadow(shadowCoord);

    return atten;
}

//volume density
float GetDensity(float3 posWS)
{
    float density = 1;
    // if (UseNoise == 1)
    // {
    //     float noise = SAMPLE_TEXTURE3D_LOD(_NoiseTexture, sampler_NoiseTexture, float4(frac(posWS * NoiseScale + float3(_Time.y * NoiseVelocity.x, 0, _Time.y * NoiseVelocity.y)), 0), 0);
    //     noise = saturate(noise - NoiseOffset) * NoiseIntensity;
    //     density = saturate(noise);
    // }
    // ApplyHeightFog(posWS, density);
    return density;
}

//mie scattering
float MieScattering(float cosAngle, float g)
{
    float g2 = g * g;
    float phase = (1.0 / (4.0 * PI)) * (1.0 - g2) / (pow((1 + g2 - 2 * g * cosAngle), 3.0 / 2.0));
    return phase;
}


float4 RayMarch(float2 screenPos, float3 rayStart, float3 rayDir, float rayLength)
{
    float2 interleavedPos = (fmod(floor(screenPos.xy), 8.0));
    //take care this
    // float offset = highQualityRandom((_ScreenParams.y * uv.y + uv.x) * _ScreenParams.x + _RandomNumber) * _JitterOffset;
    //随机采样偏移
    float offset = 0;//SAMPLE_TEXTURE2D_LOD(_DitherTexture, sampler_DitherTexture, interleavedPos / 8.0 + float2(0.5 / 8.0, 0.5 / 8.0), 0).w;
    int stepCount = _SampleCount;
    float stepSize = rayLength / stepCount;
    float3 step = rayDir * stepSize;//步进步长
    float3 currentPosition = rayStart + offset * step;
    float4 result = 0;
    float cosAngle;
    float extinction = 0;
    float attenuation = 0;

    // #if defined(_DIRECTION)
    cosAngle = dot(-_LightDirection.xyz, -rayDir);
    // #elif defined(_SPOT) || defined(_POINT)
    //     //we don't know about density between camera and light's volume, assume 0.5
    //     extinction = length(_WorldSpaceCameraPos - currentPosition) * Extinction * 0.5;
    // #endif

    [loop]
    for (int i = 0; i < stepCount; ++i)
    {
        attenuation = GetShadowAttenuation(currentPosition);

        float density = GetDensity(currentPosition);
        float scattering = _ScatteringCoef * stepSize * density;
        extinction += _ExtinctionCoef * stepSize * density;
        float4 energy = attenuation * scattering * exp(-extinction);


        energy = attenuation * density * 0.001;

        result += energy;
        currentPosition += step;
    }


    // result *= MieScattering(cosAngle, _MieG);
    // result *= _LightColor;
    result *= _Intensity;
    result = max(0, result);
    // #if defined(_DIRECTION)
    // result.w = exp(-extinction);
    // #elif defined(_SPOT) || defined(_POINT)
    //     result.w = 0;
    // #endif
    return result;
}

#endif
