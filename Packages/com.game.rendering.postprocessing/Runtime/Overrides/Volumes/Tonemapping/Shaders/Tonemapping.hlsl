#ifndef TONEMAPPING_HLSL
#define TONEMAPPING_HLSL

float4 _GTToneParam1, _GTToneParam2;

half3 GTTonemaping(half3 input)
{
    float3 temp = (input * 1.36 + 0.047) * input;
    float3 temp2 = (input * 0.93 + 0.56) * input + 0.14;
    input = saturate(temp / temp2);
    return input;
}
//-----------------------------------------------------
float W_f(float x, float e0, float e1)
{
    if (x <= e0)
        return 0;
    if (x >= e1)
        return 1;
    float a = (x - e0) / (e1 - e0);
    return a * a * (3 - 2 * a);
}

float H_f(float x, float e0, float e1)
{
    if (x <= e0)
        return 0;
    if (x >= e1)
        return 1;
    return (x - e0) / (e1 - e0);
}

float3 GranTurismoTonemap(float3 x, float P, float a, float m, float l, float c, float b)
{
    // float P = 1; // Maximum brightness
    // float a = 1; // Contrast
    // float m = 0.22; // Linear section start
    // float l = 0.4; // Linear section length
    // float c = 1.33; // Black pow  def 1 
    // float b = 0; // Black min
    float l0 = (P - m) * l / a; //0.312
    // float L0 = m - m / a;
    // float L1 = m + (1 - m) / a;
    float3 L_x = m + a * (x - m);
    float3 T_x = m * pow(x / m, c) + b;
    float S0 = m + l0;
    float S1 = m + a * l0;
    float C2 = a * P / (P - S1);
    float S_x = P - (P - S1) * exp(-(C2 * (x - S0) / P));
    float w0_x = 1 - W_f(x, 0, m);
    float w2_x = H_f(x, m + l0, m + l0);
    float w1_x = 1 - w0_x - w2_x;
    float3 f_x = T_x * w0_x + L_x * w1_x + S_x * w2_x;
    return f_x;
}


float GranTurismoTonemap(float x)
{
    float P = 1; // Maximum brightness
    float a = 1; // Contrast
    float m = 0.22; // Linear section start
    float l = 0.4; // Linear section length
    float c = 1.33; // Black pow  def 1 
    float b = 0; // Black min
    return GranTurismoTonemap(x, P, a, m, l, c, b);
}

float3 GranTurismoTonemap(float3 x)
{
    return float3(GranTurismoTonemap(x.r), GranTurismoTonemap(x.g), GranTurismoTonemap(x.b));
}

float GranTurismoTonemapCustom(float x)
{
    float P = _GTToneParam1.x; // Maximum brightness
    float a = _GTToneParam1.y; // Contrast
    float m = _GTToneParam1.z; // Linear section start
    float l = _GTToneParam1.w; // Linear section length
    float c = _GTToneParam2.x; // Black pow  def 1 
    float b = _GTToneParam2.y; // Black min
    return GranTurismoTonemap(x, P, a, m, l, c, b);
}

float3 GranTurismoTonemapCustom(float3 x)
{
    return float3(GranTurismoTonemapCustom(x.r), GranTurismoTonemapCustom(x.g), GranTurismoTonemapCustom(x.b));
}

//-----------------------------------------------------
float3 Log2Tonemap(float3 color)
{
    // Custom tonemapping curve
    float3 logColor = log2(max(color, 0.0001));
    float3 compressed = exp2(logColor * 0.33) * 1.4938 - 0.7;

    // Select between compressed and original based on brightness
    float3 tonemapped = lerp(color, compressed, step(0.3, color * (1.0)));

    return saturate(tonemapped);
}

//-----------------------------------------------------
half3 NAESTonemap(half3 input)
{
    input = (1.36 * input + 0.047) * input / ((0.93 * input + 0.56) * input + 0.14);
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
        input = GranTurismoTonemap(input);
    #elif _TONEMAP_GT_CUSTOM
        input = GranTurismoTonemapCustom(input);
    #elif _TONEMAP_NAES
        input = NAESTonemap(input);
    #elif _TONEMAP_LOG2
        input = Log2Tonemap(input);
    #endif

    return saturate(input);
}

#endif
