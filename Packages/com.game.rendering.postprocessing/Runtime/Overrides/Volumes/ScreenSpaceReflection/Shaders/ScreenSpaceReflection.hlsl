#ifndef SCREEN_SPACE_REFLECTION_INCLUDED
#define SCREEN_SPACE_REFLECTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

// Helper structs
//
struct Ray
{
    float3 origin;
    float3 direction;
};

struct Segment
{
    float3 start;
    float3 end;

    float3 direction;
};

struct Result
{
    bool isHit;

    float2 uv;
    float3 position;

    float iterationCount;
};

//
// Uniforms
//
// TEXTURE2D(_SourceTex);
TEXTURE2D(_NoiseTex);
TEXTURE2D(_TestTex);
TEXTURE2D(_ResolveTex);

TEXTURE2D(_HistoryTex);
TEXTURE2D_FLOAT(_MotionVectorTexture);

TEXTURE2D_HALF(_GBuffer0);
TEXTURE2D_HALF(_GBuffer1);
TEXTURE2D_HALF(_GBuffer2);

// copy depth of gbuffer
TEXTURE2D_FLOAT(_MaskDepthRT);
SAMPLER(sampler_MaskDepthRT);

// minimapReflection
TEXTURE2D(_MinimapPlanarReflectTex);
SAMPLER(sampler_MinimapPlanarReflectTex);

float4 _BlitTexture_TexelSize;
float4 _TestTex_TexelSize;

float4x4 _ViewMatrixSSR;
float4x4 _InverseViewMatrixSSR;
float4x4 _InverseProjectionMatrixSSR;
float4x4 _ScreenSpaceProjectionMatrixSSR;

int _MobileMode;

float4 _Params1;     // x: vignette intensity, y: distance fade, z: maximum march distance, w: intensity
float4 _Params2;    // x: aspect ratio, y: noise tiling, z: thickness, w: maximum iteration count

// 因为SSR无法稳定获取到正确的reflectionProbe和PerObjectData, 我们需要手动在SSR里面指定天空球并解析Environment Reflection Intensity Multiplier
half4 _Inutan_GlossyEnvironmentCubeMap_HDR;

#define _Attenuation            .25
#define _VignetteIntensity      _Params1.x
#define _DistanceFade           _Params1.y
#define _MaximumMarchDistance   _Params1.z
#define _Intensity              _Params1.w
#define _AspectRatio            _Params2.x
#define _NoiseTiling            _Params2.y
#define _Bandwidth              _Params2.z
#define _MaximumIterationCount  _Params2.w

#define SSR_MINIMUM_ATTENUATION 0.275
#define SSR_ATTENUATION_SCALE (1.0 - SSR_MINIMUM_ATTENUATION)
#define SSR_VIGNETTE_SMOOTHNESS 5.
#define SSR_KILL_FIREFLIES 0

//
// Helper functions
//
float Attenuate(float2 uv)
{
    float offset = min(1.0 - max(uv.x, uv.y), min(uv.x, uv.y));

    float result = offset / (SSR_ATTENUATION_SCALE * _Attenuation + SSR_MINIMUM_ATTENUATION);
    result = saturate(result);

    return pow(result, 0.5);
}

float Vignette(float2 uv)
{
    float2 k = abs(uv - 0.5) * _VignetteIntensity;
    k.x *= _BlitTexture_TexelSize.y * _BlitTexture_TexelSize.z;
    return pow(saturate(1.0 - dot(k, k)), SSR_VIGNETTE_SMOOTHNESS);
}

float3 GetViewSpacePosition(float2 uv)
{
    float depth = SampleSceneDepth(uv);

    // 跨平台深度修正
    #if defined(UNITY_REVERSED_Z)
    #else
        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
    #endif
    float4 result = mul(_InverseProjectionMatrixSSR, float4(2.0 * uv - 1.0, depth, 1.0));
    return result.xyz / result.w;
}

float GetSquaredDistance(float2 first, float2 second)
{
    first -= second;
    return dot(first, first);
}

float4 ProjectToScreenSpace(float3 position)
{
    return float4(
        _ScreenSpaceProjectionMatrixSSR[0][0] * position.x + _ScreenSpaceProjectionMatrixSSR[0][2] * position.z,
        _ScreenSpaceProjectionMatrixSSR[1][1] * position.y + _ScreenSpaceProjectionMatrixSSR[1][2] * position.z,
        _ScreenSpaceProjectionMatrixSSR[2][2] * position.z + _ScreenSpaceProjectionMatrixSSR[2][3],
        _ScreenSpaceProjectionMatrixSSR[3][2] * position.z
    );
}

// unity原版本，其 实现和引用的逻辑有差异
// Heavily adapted from McGuire and Mara's original implementation
// http://casual-effects.blogspot.com/2014/08/screen-space-ray-tracing.html
Result March_Deprecated(Ray ray, float2 uv)
{
    Result result;

    result.isHit = false;

    result.uv = 0.0;
    result.position = 0.0;

    result.iterationCount = 0;

    Segment segment;

    segment.start = ray.origin;

    float end = ray.origin.z + ray.direction.z * _MaximumMarchDistance;
    float magnitude = _MaximumMarchDistance;
    // 近裁面判断
    if (end > - _ProjectionParams.y)
        magnitude = (-_ProjectionParams.y - ray.origin.z) / ray.direction.z;

    segment.end = ray.origin + ray.direction * magnitude;
    // H0
    float4 r = ProjectToScreenSpace(segment.start);
    // H1
    float4 q = ProjectToScreenSpace(segment.end);
    // K0, K1
    const float2 homogenizers = rcp(float2(r.w, q.w));
    // Q0  Q1
    segment.start *= homogenizers.x;
    segment.end *= homogenizers.y;
    // P0, P1
    float4 endPoints = float4(r.xy, q.xy) * homogenizers.xxyy;
    // 至少一个像素
    endPoints.zw += step(GetSquaredDistance(endPoints.xy, endPoints.zw), 0.0001) * max(_TestTex_TexelSize.x, _TestTex_TexelSize.y);
    // delta = P1 - P0
    float2 displacement = endPoints.zw - endPoints.xy;

    bool isPermuted = false;

    if (abs(displacement.x) < abs(displacement.y))
    {
        isPermuted = true;

        displacement = displacement.yx;
        endPoints.xyzw = endPoints.yxwz;
    }
    // stepDir = sign(delta.x);
    float direction = sign(displacement.x);
    // invdx = stepDir / delta.x
    float normalizer = direction / displacement.x;
    // dQ
    segment.direction = (segment.end - segment.start) * normalizer;
    // dP,dK,dQ.z 这里和原算法一样第一个值是delta的符号
    float4 derivatives = float4(float2(direction, displacement.y * normalizer), (homogenizers.y - homogenizers.x) * normalizer, segment.direction.z);

    float stride = 1.0 - min(1.0, -ray.origin.z * 0.01);

    uv *= _NoiseTiling;
    uv.y *= _AspectRatio;

    float jitter = SAMPLE_TEXTURE2D(_NoiseTex, sampler_LinearClamp, uv + _WorldSpaceCameraPos.xz).a;
    // 这里把 thickness 当作 stepsize 在用
    stride *= _Bandwidth;
    // dP,dK,dQ.z * stride
    derivatives *= stride;
    // dQ * stride
    segment.direction *= stride;

    float2 z = 0.0;
    // P0,K0,Q0.z   +   dP,dK,dQ.z * jitter
    float4 tracker = float4(endPoints.xy, homogenizers.x, segment.start.z) + derivatives * jitter;

    #if defined(SHADER_API_OPENGL) || defined(SHADER_API_D3D11) || defined(SHADER_API_D3D12)
        UNITY_LOOP
    #else
        [unroll(10)]
    #endif
    for (int i = 0; i < _MaximumIterationCount; ++i)
    {
        if (any(result.uv < 0.0) || any(result.uv > 1.0))
        {
            result.isHit = false;
            return result;
        }
        // P += dP, k += dk, Q.z += dQ.z
        tracker += derivatives;

        // rayZMin = prevZMaxEstimate
        z.x = z.y;
        // rayZMax = Q.z + dQ.z * 0.5 / ( k + dK * 0.5)
        z.y = tracker.w + derivatives.w * 0.5;
        z.y /= tracker.z + derivatives.z * 0.5;

        // 远处会出现问题 需要打开这个宏
        #if SSR_KILL_FIREFLIES
            UNITY_FLATTEN
            if (z.y < - _MaximumMarchDistance)
            {
                result.isHit = false;
                return result;
            }
        #endif
        // ！和原算法的差异
        // 原本需要z.y<z.x 也就是y是min x是max 反过来了
        UNITY_FLATTEN
        if (z.y > z.x)
        {
            float k = z.x;
            z.x = z.y;
            z.y = k;
        }

        uv = tracker.xy;

        UNITY_FLATTEN
        if (isPermuted)
            uv = uv.yx;

        uv *= _TestTex_TexelSize.xy;

        float d = SampleSceneDepth(uv);
        float depth = -LinearEyeDepth(d, _ZBufferParams);

        UNITY_FLATTEN
        // z.x > depth - 1.8虽然分割出厚度 但还是有错误的显示
        // if ((z.y < depth && z.x > depth - 1.8 ) )
        if (z.y < depth)
        {
            result.uv = uv;
            result.isHit = true;
            result.iterationCount = i + 1;
            return result;
        }
    }

    return result;
}

// 根据原算法改正后
Result March(Ray ray, float2 uv, float3 normalVS)
{
    Result result;
    result.isHit = false;
    result.position = 0.0;
    result.iterationCount = 0;
    result.uv = 0.0;

    UNITY_BRANCH
    if (ray.origin.z > 0)
    {
        return result;
    }


    half2 hitPixel = half2(0, 0);

    // 外面的thickness被当作了步长在用, 实际的thickness写死了
    float layerThickness = 0.05;
    half RayBump = max(-0.0002 * _Bandwidth * ray.origin.z, 0.001);
    half3 csOrigin = ray.origin + normalVS * RayBump;

    half rayLength = ((csOrigin.z + ray.direction.z * _MaximumMarchDistance) > - _ProjectionParams.y) ? ((-_ProjectionParams.y - csOrigin.z) / ray.direction.z) : _MaximumMarchDistance;
    half3 csEndPoint = ray.direction * rayLength + csOrigin;
    half4 H0 = ProjectToScreenSpace(csOrigin);
    half4 H1 = ProjectToScreenSpace(csEndPoint);
    half k0 = 1 / H0.w;
    half k1 = 1 / H1.w;
    half2 P0 = H0.xy * k0;
    half2 P1 = H1.xy * k1;
    half3 Q0 = csOrigin * k0;
    half3 Q1 = csEndPoint * k1;

    P1 = (GetSquaredDistance(P0, P1) < 0.0001) ? P0 + half2(_TestTex_TexelSize.x, _TestTex_TexelSize.y) : P1;
    half2 delta = P1 - P0;
    bool permute = false;

    UNITY_FLATTEN
    if (abs(delta.x) < abs(delta.y))
    {
        permute = true;
        delta = delta.yx;
        P1 = P1.yx;
        P0 = P0.yx;
    }

    half stepDirection = sign(delta.x);
    half invdx = stepDirection / delta.x;
    half2 dP = half2(stepDirection, invdx * delta.y);
    half3 dQ = (Q1 - Q0) * invdx;
    half dk = (k1 - k0) * invdx;

    // jitter
    uv *= _NoiseTiling;
    uv.y *= _AspectRatio;

    float jitter = SAMPLE_TEXTURE2D(_NoiseTex, sampler_LinearClamp, uv + _WorldSpaceCameraPos.xz).a;

    dP *= _Bandwidth;
    dQ *= _Bandwidth;
    dk *= _Bandwidth;
    P0 += dP * jitter;
    Q0 += dQ * jitter;
    k0 += dk * jitter;

    half3 Q = Q0;
    half k = k0;
    half prevZMaxEstimate = csOrigin.z;
    int stepCount = 0;
    half rayZMax = prevZMaxEstimate, rayZMin = prevZMaxEstimate;
    half sceneZ = 100000;
    half end = P1.x * stepDirection;

    bool intersecting = (rayZMax >= sceneZ - layerThickness) && (rayZMin <= sceneZ);
    half2 P = P0;
    int originalStepCount = 0;

    bool stop = intersecting;

    // TODO
    #if defined(SHADER_API_OPENGL) || defined(SHADER_API_D3D11) || defined(SHADER_API_D3D12)
        UNITY_LOOP
    #else
        [unroll(10)]
    #endif
    for (; (P.x * stepDirection) <= end && stepCount < _MaximumIterationCount && !stop; P += dP, Q.z += dQ.z, k += dk, stepCount += 1)
    {
        rayZMin = prevZMaxEstimate;
        rayZMax = (dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);
        prevZMaxEstimate = rayZMax;

        UNITY_FLATTEN
        if (rayZMin > rayZMax)
        {
            half temp = rayZMin;
            rayZMin = rayZMax;
            rayZMax = temp;
        }

        hitPixel = permute ? P.yx : P;

        sceneZ = SampleSceneDepth(hitPixel * _TestTex_TexelSize.xy);
        sceneZ = -LinearEyeDepth(sceneZ, _ZBufferParams);
        bool isBehind = (rayZMin <= sceneZ);

        intersecting = isBehind && (rayZMax >= sceneZ - layerThickness);

        stop = isBehind;
    }
    P -= dP, Q.z -= dQ.z, k -= dk;

    UNITY_FLATTEN
    if (intersecting)
    {
        result.iterationCount = stepCount;
        result.uv = hitPixel * _TestTex_TexelSize.xy;
        result.isHit = true;
    }

    return result;
}

//
// Fragment shaders
//
float4 FragTest(Varyings input) : SV_Target
{
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, input.texcoord, 0);

    UNITY_BRANCH
    if (dot(gbuffer2.xyz, 1.0) == 0.0)
        return 0.0;

    // 多一次采样 可以过滤掉角色部分的射线计算
    uint materialFlags = UnpackMaterialFlags(SAMPLE_TEXTURE2D_LOD(_GBuffer0, sampler_PointClamp, input.texcoord, 0).a);
    UNITY_BRANCH
    // if (IsMaterialFlagCharacter(materialFlags)) return 0;

    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float3 normalVS = mul((float3x3)_ViewMatrixSSR, normalWS);

    Ray ray;

    ray.origin = GetViewSpacePosition(input.texcoord);

    UNITY_BRANCH
    if (ray.origin.z < - _MaximumMarchDistance)
        return 0.0;

    ray.direction = normalize(reflect(normalize(ray.origin), normalVS));

    UNITY_BRANCH
    if (ray.direction.z > 0.0)
        return 0.0;

    Result result;

    #if _OLD_METHOD
        result = March_Deprecated(ray, input.texcoord);
    #else
        // 使用修正后算法
        result = March(ray, input.texcoord, normalVS);
    #endif

    float confidence = (float)result.iterationCount / (float)_MaximumIterationCount;

    return float4(result.uv, confidence, (float)result.isHit);
}

float4 FragReproject(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    // 没有使用jitter 不考虑sceneview 依赖MotionVector
    float2 motionVector = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_LinearClamp, uv).xy;
    float2 prevUV = uv - motionVector;

    float2 k = _BlitTexture_TexelSize.xy;

    float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);

    // 0 1 2
    // 3
    float4x4 top = float4x4(
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(0.0, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, 0.0))
    );

    //     0
    // 1 2 3
    float4x4 bottom = float4x4(
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, 0.0)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(0.0, k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, k.y))
    );

    // 简单的minmax
    float4 minimum = min(min(min(min(min(min(min(min(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);
    float4 maximum = max(max(max(max(max(max(max(max(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);

    float4 history = SAMPLE_TEXTURE2D(_HistoryTex, sampler_LinearClamp, prevUV);
    // 简单的clamp
    history = clamp(history, minimum, maximum);

    // alpha通道在移动端不一定有 简单的blend
    float blend = saturate(smoothstep(0.002 * _BlitTexture_TexelSize.z, 0.0035 * _BlitTexture_TexelSize.z, length(motionVector)));
    blend *= 0.85;

    float weight = clamp(lerp(0.95, 0.7, blend * 100.0), 0.7, 0.95);

    return lerp(color, history, weight);
}

float4 FragResolve(Varyings input) : SV_Target
{
    float4 test = SAMPLE_TEXTURE2D(_TestTex, sampler_PointClamp, input.texcoord);

    // 兼容HDR R11G11B10格式 alpha通道isHit替代判断
    test.w = test.z > 0;

    UNITY_BRANCH
    if (test.w == 0.0)
    {
        // 屏幕空间未追踪到信息的区域
        return float4(0, 0, 0, 1);
    }

    float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, test.xy);

    float confidence = test.w * Attenuate(test.xy) * Vignette(test.xy);

    color.rgb *= confidence;
    // 这里渐变必须要A通道参与模糊 只能降低到LDR
    color.a = test.z;

    return color;
}

// 因为SSR无法稳定获取到正确的reflectionProbe和PerObjectData, 我们需要手动在SSR里面指定天空球并解析Environment Reflection Intensity Multiplier
half3 GlossyEnvironmentReflectionSSR(half3 reflectVector, float3 positionWS, half perceptualRoughness, half occlusion)
{
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, reflectVector, mip));

    half3 irradiance = 0;

    #if defined(UNITY_USE_NATIVE_HDR) || defined(UNITY_DOTS_INSTANCING_ENABLED)
        irradiance = encodedIrradiance.rbg;
    #else
        irradiance = DecodeHDREnvironment(encodedIrradiance, _Inutan_GlossyEnvironmentCubeMap_HDR);
    #endif // UNITY_USE_NATIVE_HDR || UNITY_DOTS_INSTANCING_ENABLED
    return irradiance * occlusion;
}

float4 FragComposite(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    // 由于没有Gbuffer之前的predepth阶段 只能依靠存储两个阶段的深度 过滤出Gbuffer中没有的物体 才能混合SSR
    // TODO 模糊阶段会把Gbuffer后的像素混进去 导致边缘会有溢出 可能要考虑提前mask
    // float preDepth = SAMPLE_TEXTURE2D(_MaskDepthRT, sampler_MaskDepthRT, uv).r;
    float depth = SampleSceneDepth(uv);

    float mask = 1;//1 - (depth > preDepth);

    float4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    half3 clampedSourceColor = saturate(sourceColor.rgb); // 关闭SSR的情况下直接使用场景颜色叠上去, 但注意场景颜色是HDR, 强行限制一下\
    UNITY_BRANCH
    if (Linear01Depth(depth, _ZBufferParams) > 0.999)
        return sourceColor;

    half4 gbuffer0 = SAMPLE_TEXTURE2D_LOD(_GBuffer0, sampler_PointClamp, uv, 0);
    half4 gbuffer1 = SAMPLE_TEXTURE2D_LOD(_GBuffer1, sampler_PointClamp, uv, 0);
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);

    uint materialFlags = UnpackMaterialFlags(gbuffer0.a);

    // 植物 固定occ为1，ssrIndirectSpecAdjust为0
    // UNITY_BRANCH
    // if (IsMaterialFlagFoliage(materialFlags))
    // {
    //     gbuffer1.gba = float3(0, 0, 1);
    // }

    // 自定义环境光强度(这里就是SSR强度), 是CustomLit传进来的
    float envCustomIntensity = 1;
    // UNITY_BRANCH
    // if (IsMaterialFlagIDCustomEnvSpecIntensity(materialFlags))
    // {
    //     envCustomIntensity = gbuffer1.b * 10;
    // }

    BRDFData brdfData = BRDFDataFromGbuffer(gbuffer0, gbuffer1, gbuffer2);
    // Lit.shader定制
    half ssrIndirectSpecAdjust = gbuffer1.g;

    half3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float3 positionVS = GetViewSpacePosition(uv);
    float3 viewDirectionWS = -mul((float3x3)_InverseViewMatrixSSR, normalize(positionVS));
    float3 positionWS = mul(_InverseViewMatrixSSR, float4(positionVS, 1.0)).xyz;

    // GlobalIllumination
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = abs(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV) * (1.0 - NoV);

    // TODO 简化版本 没有做mipmap模糊 无法根据粗糙度采样
    float4 resolve = 0;
    // UNITY_BRANCH
    // if (_MobileMode == 0)
    resolve = SAMPLE_TEXTURE2D(_ResolveTex, sampler_LinearClamp, uv);
    // UNITY_BRANCH
    // if (_MobileMode == 3)
    //     resolve = SAMPLE_TEXTURE2D(_MinimapPlanarReflectTex, sampler_MinimapPlanarReflectTex, uv);
    float confidence = saturate(2.0 * dot(-viewDirectionWS, normalize(reflectVector)));

    // 老版本的_CameraReflectionsTexture直接存的indirectSpecular 这里只能把计算LitGBufferPass中的计算分离到这儿
    // 开启SSR 就关闭Gbuffer生成部分的indirectSpecular部分
    // UniversalGBuffer 相关Pass需要 #pragma multi_compile_fragment _ _SCREEN_SPACE_REFLECTION
    half3 indirectSpecular = GlossyEnvironmentReflectionSSR(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h);
    float distanceFade = _DistanceFade;
    // GlobalRenderSettings里的自定义环境反射球cubemap
    // if (IsMaterialFlagIDCustomReflectCubemap(materialFlags))
    // {
    //     indirectSpecular = GlossyEnvironmentReflectionByCustomCubemap(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h) * _GLOBAL_ENVCUSTOM_CUBEMAPINTENSITY;
    //     distanceFade = _GLOBAL_ENVCUSTOM_CUBEMAPINTENSITY;
    // }

    #if DEBUG_SCREEN_SPACE_REFLECTION
        indirectSpecular = 0;
    #endif

    // https://www.desmos.com/calculator/k3hodgy8ry TODO 这个结果是个比较奇怪的曲线
    float fade = resolve.a * resolve.a * 3;
    // UNITY_BRANCH
    // if (_MobileMode == 2) fade = 1; // 简化计算
    distanceFade = saturate(distanceFade + ssrIndirectSpecAdjust);
    // fade是低频部分 理论上相当于一个锐化操作
    fade = (1.0 - saturate(fade * smoothstep(0.5, 1.0, fade) * distanceFade)) * confidence;
    // UNITY_BRANCH
    // if (_MobileMode == 2) resolve.rgb = clampedSourceColor.rgb * clampedSourceColor.rgb;
    // return float4(mask * lerp(indirectSpecular, resolve.rgb, fade), 1);
    // IBL的间接光和SSR的进行过度
    indirectSpecular = lerp(indirectSpecular, resolve.rgb, fade);
    // 只是菲尼尔项
    half3 indirectSpecularSSR = EnvironmentBRDF(brdfData, 0, indirectSpecular, fresnelTerm);
    indirectSpecularSSR = max(0, indirectSpecularSSR) * gbuffer1.a * mask * _Intensity;  // occ 和 gbuffer后面物体mask

    #if DEBUG_SCREEN_SPACE_REFLECTION || DEBUG_INDIRECT_SPECULAR
        return half4(indirectSpecularSSR, 1);
    #endif

    // 自定义环境光强度(这里就是SSR强度), 是CustomLit传进来的
    indirectSpecularSSR *= envCustomIntensity;

    sourceColor.rgb += indirectSpecularSSR;

    return sourceColor;
}

float4 FragMobilePlanarReflection(Varyings input) : SV_Target
{
    // 由于没有Gbuffer之前的predepth阶段 只能依靠存储两个阶段的深度 过滤出Gbuffer中没有的物体 才能混合SSR
    // TODO 模糊阶段会把Gbuffer后的像素混进去 导致边缘会有溢出 可能要考虑提前mask
    float uv = input.texcoord;
    float preDepth = SAMPLE_TEXTURE2D(_MaskDepthRT, sampler_MaskDepthRT, uv).r;
    float depth = SampleSceneDepth(uv);

    float mask = 1;//1 - (depth > preDepth);

    float4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    half3 clampedSourceColor = saturate(sourceColor.rgb); // 关闭SSR的情况下直接使用场景颜色叠上去, 但注意场景颜色是HDR, 强行限制一下
    UNITY_BRANCH
    if (Linear01Depth(depth, _ZBufferParams) > 0.999)
        return float4(1, 0, 0, 1);

    // built-in
    // gbuffer0 rgb: albedo * OneMinusReflectivityFromMetallic(metallic) a: occ
    // gbuffer1 rgb: lerp (unity_ColorSpaceDielectricSpec.rgb, albedo, metallic) a: smoothness
    // gbuffer2 rgb: normal

    // urp
    // gbuffer0 rgb: albedo
    // gbuffer1 r: 1-OneMinusReflectivityMetallic(metallic) a: occ
    // gbuffer2 rgb: normal a: smoothness

    half4 gbuffer0 = SAMPLE_TEXTURE2D_LOD(_GBuffer0, sampler_PointClamp, uv, 0);
    half4 gbuffer1 = SAMPLE_TEXTURE2D_LOD(_GBuffer1, sampler_PointClamp, uv, 0);
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);

    uint materialFlags = UnpackMaterialFlags(gbuffer0.a);

    // 植物 固定occ为1，ssrIndirectSpecAdjust为0
    // UNITY_BRANCH
    // if (IsMaterialFlagFoliage(materialFlags))
    // {
    //     gbuffer1.gba = float3(0, 0, 1);
    // }

    // 自定义环境光强度(这里就是SSR强度), 是CustomLit传进来的
    float envCustomIntensity = 1;
    // UNITY_BRANCH
    // if (IsMaterialFlagIDCustomEnvSpecIntensity(materialFlags))
    // {
    //     envCustomIntensity = gbuffer1.b * 10;
    // }

    BRDFData brdfData = BRDFDataFromGbuffer(gbuffer0, gbuffer1, gbuffer2);
    // Lit.shader定制
    half ssrIndirectSpecAdjust = gbuffer1.g;

    half3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float3 positionVS = GetViewSpacePosition(uv);
    float3 viewDirectionWS = -mul((float3x3)_InverseViewMatrixSSR, normalize(positionVS));
    float3 positionWS = mul(_InverseViewMatrixSSR, float4(positionVS, 1.0)).xyz;

    // GlobalIllumination
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = abs(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV) * (1.0 - NoV);

    // TODO 简化版本 没有做mipmap模糊 无法根据粗糙度采样
    float4 resolve = SAMPLE_TEXTURE2D(_ResolveTex, sampler_LinearClamp, uv);

    float confidence = saturate(2.0 * dot(-viewDirectionWS, normalize(reflectVector)));

    // 老版本的_CameraReflectionsTexture直接存的indirectSpecular 这里只能把计算LitGBufferPass中的计算分离到这儿
    // 开启SSR 就关闭Gbuffer生成部分的indirectSpecular部分
    // UniversalGBuffer 相关Pass需要 #pragma multi_compile_fragment _ _SCREEN_SPACE_REFLECTION
    half3 indirectSpecular = GlossyEnvironmentReflectionSSR(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h);
    float distanceFade = _DistanceFade;
    // GlobalRenderSettings里的自定义环境反射球cubemap
    // if (IsMaterialFlagIDCustomReflectCubemap(materialFlags))
    // {
    //     indirectSpecular = GlossyEnvironmentReflectionByCustomCubemap(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h) * _GLOBAL_ENVCUSTOM_CUBEMAPINTENSITY;
    //     distanceFade = _GLOBAL_ENVCUSTOM_CUBEMAPINTENSITY;
    // }

    #if DEBUG_SCREEN_SPACE_REFLECTION
        indirectSpecular = 0;
    #endif

    float fade = 1;
    distanceFade = saturate(distanceFade + ssrIndirectSpecAdjust);
    // fade是低频部分 理论上相当于一个锐化操作
    fade = (1.0 - saturate(fade * smoothstep(0.5, 1.0, fade) * distanceFade)) * confidence;
    {
        resolve.rgb = lerp(clampedSourceColor.rgb * clampedSourceColor.rgb, resolve.rgb * 2, 1 * step(0.999, abs(dot(normalWS, float3(0, 1, 0)))) * fresnelTerm * fresnelTerm * fresnelTerm);
    }
    // IBL的间接光和SSR的进行过度
    indirectSpecular = lerp(indirectSpecular, resolve.rgb, fade);
    // 只是菲尼尔项
    half3 indirectSpecularSSR = EnvironmentBRDF(brdfData, 0, indirectSpecular, fresnelTerm);
    indirectSpecularSSR = max(0, indirectSpecularSSR) * gbuffer1.a * mask * _Intensity;  // occ 和 gbuffer后面物体mask

    #if DEBUG_SCREEN_SPACE_REFLECTION || DEBUG_INDIRECT_SPECULAR
        return half4(indirectSpecularSSR, 1);
    #endif

    // 自定义环境光强度(这里就是SSR强度), 是CustomLit传进来的
    indirectSpecularSSR *= envCustomIntensity;

    sourceColor.rgb += indirectSpecularSSR;

    return sourceColor;
}

float4 FragMobileAntiFlicker(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    // 没有使用jitter 不考虑sceneview 依赖MotionVector
    float2 motionVector = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_LinearClamp, uv).xy;
    float2 prevUV = uv - motionVector;

    float2 k = _BlitTexture_TexelSize.xy;

    float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);

    // 0 1 2
    // 3
    float4x4 top = float4x4(
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(0.0, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, -k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, 0.0))
    );

    //     0
    // 1 2 3
    float4x4 bottom = float4x4(
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, 0.0)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(-k.x, k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(0.0, k.y)),
        SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + float2(k.x, k.y))
    );

    // 简单的minmax
    float4 minimum = min(min(min(min(min(min(min(min(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);
    float4 maximum = max(max(max(max(max(max(max(max(top[0], top[1]), top[2]), top[3]), bottom[0]), bottom[1]), bottom[2]), bottom[3]), color);

    float4 history = SAMPLE_TEXTURE2D(_HistoryTex, sampler_LinearClamp, prevUV);
    // 简单的clamp
    history = clamp(history, minimum, maximum);

    // alpha通道在移动端不一定有 简单的blend
    float blend = saturate(smoothstep(0.002 * _BlitTexture_TexelSize.z, 0.0035 * _BlitTexture_TexelSize.z, length(motionVector)));
    blend *= 0.85;

    float weight = clamp(lerp(0.95, 0.7, blend * 100.0), 0.7, 0.95);

    return lerp(color, history, weight);
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
