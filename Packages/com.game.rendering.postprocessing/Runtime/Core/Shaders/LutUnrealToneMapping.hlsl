#ifndef LUT_UNREAL_TONE_MAPPING_INCLUDED
#define LUT_UNREAL_TONE_MAPPING_INCLUDED

float _BlueCorrection;
float _ExpandGamut;

float _FilmSlope;
float _FilmToe;
float _FilmShoulder;
float _FilmBlackClip;
float _FilmWhiteClip;

// --------------------------------------------------------------------------
// Bizarre matrix but this expands sRGB to between P3 and AP1
// CIE 1931 chromaticities:	x		y
//				Red:		0.6965	0.3065
//				Green:		0.245	0.718
//				Blue:		0.1302	0.0456
//				White:		0.3127	0.329
static const float3x3 Wide_2_XYZ_MAT = 
{
    0.5441691,  0.2395926,  0.1666943,
    0.2394656,  0.7021530,  0.0583814,
    -0.0023439,  0.0361834,  1.0552183,
};

float3x3 GetWideMat()
{
    float3x3 Wide_2_AP1 = mul(XYZ_2_AP1_MAT, Wide_2_XYZ_MAT);
    float3x3 ExpandMat = mul(Wide_2_AP1, AP1_2_sRGB);

    return ExpandMat;
}

static const float3x3 BlueCorrect =
{
    0.9404372683, -0.0183068787, 0.0778696104,
    0.0083786969,  0.8286599939, 0.1629613092,
    0.0005471261, -0.0008833746, 1.0003362486
};

static const float3x3 BlueCorrectInv =
{
    1.06318,     0.0233956, -0.0865726,
    -0.0106337,   1.20632,   -0.19569,
    -0.000590887, 0.00105248, 0.999538
};

float3x3 GetBlurCorrectAP1()
{
    return mul(AP0_2_AP1_MAT, mul(BlueCorrect, AP1_2_AP0_MAT));
}

float3x3 GetBlurCorrectInvAP1()
{
    return mul(AP0_2_AP1_MAT, mul(BlueCorrectInv, AP1_2_AP0_MAT));
}
// --------------------------------------------------------------------------


float3 ExpandBrightToFakeWideGamut(float3 ColorAP1)
{
    // Expand bright saturated colors outside the sRGB gamut to fake wide gamut rendering.
    float LumaAP1 = dot(ColorAP1, AP1_RGB2Y);
    float3 ChromaAP1 = ColorAP1 / LumaAP1;

    float ChromaDistSqr = dot(ChromaAP1 - 1, ChromaAP1 - 1);
    float ExpandAmount = (1 - exp2(-4 * ChromaDistSqr)) * (1 - exp2(-4 * _ExpandGamut * LumaAP1 * LumaAP1));

    float3 ColorExpand = mul(GetWideMat(), ColorAP1);
    ColorAP1 = lerp(ColorAP1, ColorExpand, ExpandAmount);

    return ColorAP1;
}

half3 ToneMappingCurve(float3 WorkingColor)
{
    const half ToeScale			= 1 + _FilmBlackClip - _FilmToe;
    const half ShoulderScale	= 1 + _FilmWhiteClip - _FilmShoulder;
    
    const float InMatch = 0.18;
    const float OutMatch = 0.18;

    float ToeMatch;
    if(_FilmToe > 0.8)
    {
        // 0.18 will be on straight segment
        ToeMatch = (1 - _FilmToe - OutMatch) / _FilmSlope + log10(InMatch);
    }
    else
    {
        // 0.18 will be on toe segment

        // Solve for ToeMatch such that input of InMatch gives output of OutMatch.
        const float bt = (OutMatch + _FilmBlackClip) / ToeScale - 1;
        ToeMatch = log10(InMatch) - 0.5 * log((1 + bt) / (1 - bt)) * (ToeScale / _FilmSlope);
    }

    float StraightMatch = (1 - _FilmToe) / _FilmSlope - ToeMatch;
    float ShoulderMatch = _FilmShoulder / _FilmSlope - StraightMatch;
    
    half3 LogColor = log10(WorkingColor);
    half3 StraightColor = _FilmSlope * (LogColor + StraightMatch);
    
    half3 ToeColor		= (   -_FilmBlackClip) + (2 *      ToeScale) / (1 + exp((-2 * _FilmSlope /      ToeScale) * (LogColor -      ToeMatch)));
    half3 ShoulderColor	= (1 + _FilmWhiteClip) - (2 * ShoulderScale) / (1 + exp(( 2 * _FilmSlope / ShoulderScale) * (LogColor - ShoulderMatch)));

    ToeColor		= LogColor <      ToeMatch ?      ToeColor : StraightColor;
    ShoulderColor	= LogColor > ShoulderMatch ? ShoulderColor : StraightColor;

    half3 t = saturate((LogColor - ToeMatch) / (ShoulderMatch - ToeMatch));
    t = ShoulderMatch < ToeMatch ? 1 - t : t;
    t = (3 - 2 * t ) * t * t;
    half3 ToneColor = lerp(ToeColor, ShoulderColor, t);

    return ToneColor;
}

// https://www.desmos.com/calculator/h8rbdpawxj?lang=zh-CN
half3 ToneMappingCurveApprox(float3 WorkingColor)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    float3 x = WorkingColor * 0.7f;
    half3 ToneColor = (x * (a * x + b)) / (x * (c * x + d) + e);
    return ToneColor;
}

float3 FilmToneMap(half3 ColorAP1)
{
    // Blue correction
    ColorAP1 = lerp(ColorAP1, mul(GetBlurCorrectAP1(), ColorAP1), _BlueCorrection);

    // Tonemapped color in the AP1 gamut
    float3 ColorAP0 = ACEScg_to_ACES(ColorAP1);


    // --- Glow module --- //
    float saturation = rgb_2_saturation(ColorAP0);
    float ycIn = rgb_2_yc(ColorAP0);
    float s = sigmoid_shaper((saturation - 0.4) / 0.2);
    float addedGlow = 1 + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);
    ColorAP0 *= addedGlow;

    // --- Red modifier --- //
    float hue = rgb_2_hue(ColorAP0);
    float centeredHue = center_hue(hue, RRT_RED_HUE);
    float hueWeight;
    {
        hueWeight = smoothstep(0.0, 1.0, 1.0 - abs(2.0 * centeredHue / RRT_RED_WIDTH));
        hueWeight *= hueWeight;
    }

    ColorAP0.r += hueWeight * saturation * (RRT_RED_PIVOT - ColorAP0.r) * (1. - RRT_RED_SCALE);


    // --- ACES to RGB rendering space --- //
    float3 WorkingColor = max(0.0, ACES_to_ACEScg(ColorAP0));

    // Pre desaturate
    WorkingColor = lerp(dot(WorkingColor, AP1_RGB2Y).xxx, WorkingColor, RRT_SAT_FACTOR.xxx);

    #if _UNREALAPPROX
        half3 ToneColor = ToneMappingCurveApprox(WorkingColor);
    #else
        half3 ToneColor = ToneMappingCurve(WorkingColor);
    #endif
    
    // Post desaturate
    ToneColor = lerp(dot(float3(ToneColor), AP1_RGB2Y).xxx, ToneColor, ODT_SAT_FACTOR.xxx);

    // positive AP1 values
    ColorAP1 = max(0, ToneColor);

    // Uncorrect blue to maintain white point
    ColorAP1 = lerp(ColorAP1, mul(GetBlurCorrectInvAP1(), ColorAP1), _BlueCorrection);

    // Convert from AP1 to sRGB and clip out-of-gamut values
    return max(0, mul(AP1_2_sRGB, ColorAP1));
}


#endif // LUT_UNREAL_TONE_MAPPING_INCLUDED
