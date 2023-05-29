#ifndef SCREEN_SPACE_OCCLUSION_AO_INCLUDED
#define SCREEN_SPACE_OCCLUSION_AO_INCLUDED

#if GROUNDTRUTH_BASED_AMBIENTOCCLUSION
    #define PAD 0.5
    // GATO 是180 双方向采样 所以STEPS实际是x2
    // TODO 方向数和单方向采样数 怎么分布更优
#else
    #define PAD 1
#endif

#if QUALITY_LOWEST
    #define DIRECTIONS      2
    #define STEPS           4 * PAD
    #define SAMPLERCOUNT    4
#elif QUALITY_LOW
    #define DIRECTIONS      2
    #define STEPS           6 * PAD
    #define SAMPLERCOUNT    8
#elif QUALITY_MEDIUM
    #define DIRECTIONS      3
    #define STEPS           8 * PAD
    #define SAMPLERCOUNT    12
#elif QUALITY_HIGH
    #define DIRECTIONS      4
    #define STEPS           10 * PAD
    #define SAMPLERCOUNT    20
#elif QUALITY_HIGHEST
    #define DIRECTIONS      4
    #define STEPS           12 * PAD
    #define SAMPLERCOUNT    32
#else
    #define DIRECTIONS      1
    #define STEPS           1
    #define SAMPLERCOUNT    1
#endif

#include "./ScreenSpaceOcclusionCommon.hlsl"

// ---------------------------------------------------------------------------------------------------------------

// Constants
// kContrast determines the contrast of occlusion. This allows users to control over/under
// occlusion. At the moment, this is not exposed to the editor because it's rarely useful.
// The range is between 0 and 1.
static const half kContrast = half(0.5);
// The constants below are used in the AO estimator. Beta is mainly used for suppressing
// self-shadowing noise, and Epsilon is used to prevent calculation underflow. See the paper
// (Morgan 2011 https://casual-effects.com/research/McGuire2011AlchemyAO/index.html)
// for further details of these constants.
static const half kBeta = half(0.002);
static const half kEpsilon = half(0.0001);

// Sample point picker
float3 PickSamplePoint(float2 uv, float index)
{
    // Uniformaly distributed points on a unit sphere
    // http://mathworld.wolfram.com/SpherePointPicking.html
    const half gn = half(InterleavedGradientNoise(uv * _Scaled_TexelSize.zw, index));

    float u = frac(UVRandom(0.0, index + uv.x * 1e-10) + gn) * 2.0 - 1.0;
    float theta = (UVRandom(1.0, index + uv.x * 1e-10) + gn) * TWO_PI;

    float3 v = float3(CosSin(theta) * sqrt(1.0 - u * u), u);
    return float3(CosSin(theta) * sqrt(1.0 - u * u), u);
}

// ---------------------------------------------------------------------------------------------------------------


// s2016_pbs_activision_occlusion
float GetGTAO(float2 uv, float3 positionVS, float3 normalVS, float stepPixels, float2 noiseDirectionsOffsets, float firstStep, float perAngle)
{
    normalVS.y *= -1;
    // 180度切片 内部双方向计算
    perAngle *= 0.5;

    half3 viewDir = -normalize(positionVS);
    // 相当于距离屏幕中心距离
    half vdirXYDot = dot(viewDir.xy, viewDir.xy);

    // ------------------------------------------------------------
    float totalAO = 0;

    UNITY_UNROLL
    for (int d = 0; d < DIRECTIONS; d++)
    {
        // 随机一个切片角度
        float angle = perAngle * (float(d) + noiseDirectionsOffsets.x);
        // 切片平面方向
        float3 sliceDir = float3(CosSin(angle), 0);
        // 墙面掠射角会出现过黑的情况 视角边缘掠射角相对更明显 所以用这样一个值去增加可见性做平衡
        // 来源于 AmplifyOcclusion
        // TODO 但是像UE4中和PPT中 如果使用acosFast代替acos 掠射角过黑会更加严重 这个trick也并不能解决
        half wallDarkeningCorrection = dot(normalVS, cross(viewDir, sliceDir)) * vdirXYDot;
        wallDarkeningCorrection = wallDarkeningCorrection * wallDarkeningCorrection;

        // 求切片平面两侧和视角方向最大的可见夹角
        // 默认值cos=-1为180度 可见性最大
        half2 h = half2(-1.0, -1.0);
        UNITY_UNROLL
        for (int s = 0; s < STEPS; s++)
        {
            // slice向外步进，像素换成uv
            float2 uvOffset = (firstStep + s * stepPixels) * sliceDir * _Scaled_TexelSize.xy;
            // 两个方向的uv
            float4 stepUV = uv.xyxy + float4(uvOffset.xy, -uvOffset.xy);
            // 两边采样点到视角中心的方向d PPT 54
            half3 ds = ReconstructPositionVS(stepUV.xy) - positionVS;
            half3 dt = ReconstructPositionVS(stepUV.zw) - positionVS;
            // 方向d的长度
            half2 dsdt = half2(dot(ds, ds), dot(dt, dt));
            half2 dsdtLength = rsqrt(dsdt + 0.0001);
            // dot(d, v) / |d| = 方向d和视角方向v的cos值 (两个向量都归一化过)
            // cos越大，夹角越小，所以要找最大的 PPT 54
            half2 H = half2(dot(ds, viewDir), dot(dt, viewDir)) * dsdtLength.xy;
            // |d| * 2 / r^2 作为lerp系数
            half2 falloff = saturate(dsdt.xy * (2.0 * _InvRadius2));
            // 每次步进得到的h，都要和上一次的进行lerp（不信任单次采样位置的深度）（为了解决比较薄的物体AO过重的问题）
            H = lerp(H, h, falloff);
            // 如果lerp后的H还是比之前的h大，就用H；否则根据一个厚度值lerp
            // 总的来说步进过程中，根据每次的h进行混合，试图估算附近的厚度
            h.xy = (H.xy > h.xy) ? H.xy : lerp(H.xy, h.xy, _Thickness);
        }

        // slice平面法线 PPT 62 Sn
        half3 normalSlicePlane = normalize(cross(sliceDir, viewDir));

        // x = normalSlicePlane * dot(normalVS, normalSlicePlane) = 法线在Sn上的投影 指向Sn的方向
        // Np + x = N 得到法线在slice平面的投影 Np
        half3 normalProj = normalVS - normalSlicePlane * dot(normalVS, normalSlicePlane);
        // |Np|
        half normalProjLength = length(normalProj) + 0.0001;

        // 注意这里开始n指角度 法线Np与视角方向夹角n (PPT 61) 的cos
        half cos_n = clamp(dot(normalProj, viewDir) * rcp(normalProjLength), -1.0, 1.0);

        // slice平面的切线用来决定n的符号
        half3 tangentSlicePlane = cross(viewDir, normalSlicePlane);

        // TODO acosFast会导致掠射角过黑
        // half n = -sign(dot(normalProj, tangentSlicePlane)) * acos(cos_n);
        // float sin_n = sin(n);
        // UE4 版本n和sin_n求法 少算一遍sin
        // Np和slice切线的夹角 - slice切线和viewDir的夹角(90度) = n
        float cos_nt = dot(normalProj, tangentSlicePlane) * rcp(normalProjLength);
        half n = acos(cos_nt) - HALF_PI;
        float sin_n = -cos_nt;

        // h前面是cos，转成角度，h1前面要一个负号
        h = acos(clamp(h, -1.0, 1.0));
        // h位于半球切面下面时候进行约束  PPT 58
        h.x = n + max(-h.x - n, -HALF_PI);
        h.y = n + min(h.y - n, HALF_PI);

        // cosine weighting 解析公式 PPT 61
        half2 innerIntegral = 0.25 * (-cos(2 * h - n) + cos_n + 2 * h * sin_n);
        // |Np| * 内层积分解析解 PPT 62
        totalAO += (normalProjLength + wallDarkeningCorrection) * (innerIntegral.x + innerIntegral.y + _AngleBias);
    }

    // 外层积分数值解
    totalAO /= (half)DIRECTIONS;

    return totalAO;
}

// Image-space horizon-based ambient occlusion 2008
float GetHBAO(float2 uv, float3 positionVS, float3 normalVS, float stepPixels, float2 noiseDirectionsOffsets, float firstStep, float perAngle)
{
    float totalAO = 0;

    UNITY_UNROLL
    for (int d = 0; d < DIRECTIONS; ++d)
    {
        // 随机一个切片角度
        float angle = perAngle * (float(d) + noiseDirectionsOffsets.x);
        // 切片平面方向
        float2 sliceDir = CosSin(angle);
        // 一个方向 多次采样
        UNITY_UNROLL
        for (int s = 0; s < STEPS; ++s)
        {
            float2 uvOffset = (firstStep + s * stepPixels) * sliceDir * _Scaled_TexelSize.xy;
            float2 stepUV = uv + uvOffset;
            float3 stepPositionVS = ReconstructPositionVS(stepUV);

            // 视角空间下 采样点到中心点的方向
            float3 viewDir = stepPositionVS - positionVS;

            // 目标 = sin(viewDir和深度平面夹角) + sin(切线方向和深度平面夹角)
            // 简化为 NdotV = cos(viewDir和法线夹角) = sin(viewDir和深度平面夹角 + 切线方向和深度平面夹角)

            // https://github.com/NVIDIAGameWorks/HBAOPlus
            // To avoid over-occlusion artifacts, HBAO+ uses a simpler AO approximation than HBAO,
            // similar to "Scalable Ambient Obscurance" [McGuire et al. 2012] [Bukowski et al. 2012].
            float VdotV = dot(viewDir, viewDir);
            float NdotV = dot(normalVS, viewDir) * rsqrt(VdotV); // normalize

            // 衰减 = 1 - (length(viewDir) / r) ^ 2
            float atten = saturate(1 - VdotV * _InvRadius2);

            // Use saturate(x) instead of max(x, 0.f) because that is faster
            // bias解决Low-Tessellation问题
            totalAO += saturate(NdotV - _AngleBias) * atten;
        }
    }
    // _AOMultiplier = 2 / (1 - bias)
    totalAO *= (_AOMultiplier * rcp(STEPS * DIRECTIONS));
    return 1 - totalAO;
}


// Distance-based AO estimator based on Morgan 2011
// "Alchemy screen-space ambient obscurance algorithm"
float GetSAO(float2 uv, float3 positionVS, float3 normalVS)
{
    float totalAO = 0;
    _Radius *= 0.375;

    half rcpCount = rcp((half)SAMPLERCOUNT);
    UNITY_UNROLL
    for (int s = 0; s < SAMPLERCOUNT; ++s)
    {
        // 球内随机点 作为向量
        float3 deltaDir = PickSamplePoint(uv, s);
        // Make them distributed between [0, radius]
        deltaDir *= sqrt((s + 1.0) * rcpCount) * _Radius;

        // 限制在法线正向的半球空间内
        // deltaDir = dot(deltaDir, -normalVS) < 0 ? deltaDir : -deltaDir
        deltaDir = faceforward(deltaDir, -normalVS, deltaDir);

        // 视角空间下 基准位置应用这个随机向量 得到一个随机位置
        float3 posVS = positionVS + deltaDir;

        // 视角空间坐标 转换成uv
        float3 posClip = mul((float3x3)_CameraProjMatrix, posVS);
        float2 p_uv = clamp((posClip.xy * rcp(posVS.z) + 1.0) * 0.5, 0.0, 1.0);
        p_uv.y = 1 - p_uv.y;

        // 用随机获得的这个uv 找对对应深度位置的 视角空间坐标
        float3 p_posVS = ReconstructPositionVS(p_uv);

        // MARK 主要是找邻近点的方式比较粗暴
        float3 p_dir = p_posVS - positionVS;
        // 见论文公式
        float a1 = max(dot(p_dir, normalVS) - kBeta * positionVS.z, 0.0);
        float a2 = dot(p_dir, p_dir) + kEpsilon;
        totalAO += a1 * rcp(a2);
    }

    // Intensity normalization
    totalAO *= _Radius;

    // 预先pow和模糊后pow不一样
    totalAO = PositivePow(totalAO * 2 * (1.0 - _AngleBias) * rcpCount, kContrast);

    return 1 - totalAO;
}

float4 FragAO(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    
    float3 positionVS = ReconstructPositionVS(uv);
    //首先重建视空间法线
    float3 normalVS = ReconstructNormalVS(uv, _Scaled_TexelSize.xy, positionVS);

    #ifdef DEBUG_VIEWNORMAL
        return float4(normalVS * 0.5 + 0.5, 1);
    #endif


    #if GROUNDTRUTH_BASED_AMBIENTOCCLUSION || HORIZON_BASED_AMBIENTOCCLUSION
        // 单次步长（像素数）
        float stepPixels = min(_RadiusToScreen * rcp(positionVS.z), _MaxRadiusPixels) * rcp(STEPS + 1.0);

        float2 noiseDirectionsOffsets = GetSpatialDirectionsOffsets(uv * _Scaled_TexelSize.zw);
        float firstStep = (frac(noiseDirectionsOffsets.y) * stepPixels + 1.0);
        
        // 360度切片
        float perAngle = 2.0 * PI * rcp(DIRECTIONS);
    #endif

    #if GROUNDTRUTH_BASED_AMBIENTOCCLUSION
        float totalAO = GetGTAO(uv, positionVS, normalVS, stepPixels, noiseDirectionsOffsets, firstStep, perAngle);
    #elif HORIZON_BASED_AMBIENTOCCLUSION
        float totalAO = GetHBAO(uv, positionVS, normalVS, stepPixels, noiseDirectionsOffsets, firstStep, perAngle);
    #else// SCALABLE_AMBIENT_OBSCURANCE
        float totalAO = GetSAO(uv, positionVS, normalVS);
    #endif

    float fallOffStart = _MaxDistance - _DistanceFalloff;
    float distFactor = saturate((positionVS.z - fallOffStart) * rcp(_MaxDistance - fallOffStart));
    totalAO = lerp(saturate(totalAO), 1, distFactor);
    return float4(EncodeFloatRG(saturate(positionVS.z * _ProjectionParams.w)), totalAO, 1);
}


#endif // SCREEN_SPACE_OCCLUSION_AO_INCLUDED
