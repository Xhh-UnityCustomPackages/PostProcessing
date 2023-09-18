#ifndef TONEMAPPING_HLSL
#define TONEMAPPING_HLSL

half3 GTTonemaping(half3 input)
{
    float3 temp = (input * 1.36 + 0.047) * input;
    float3 temp2 = (input * 0.93 + 0.56) * input + 0.14;
    input = saturate(temp / temp2);
    return input;
}

half3 ApplyTonemaping(half3 input)
{
    #if _TONEMAP_ACES
        float3 aces = unity_to_ACES(input);
        input = AcesTonemap(aces);
    #elif _TONEMAP_NEUTRAL
        input = NeutralTonemap(input);
    #elif _TONEMAP_GT
        input = GTTonemaping(input);
    #endif

    return saturate(input);
}

#endif
