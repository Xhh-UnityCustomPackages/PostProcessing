#ifndef SCREEN_SPACE_REFLECTION_INCLUDED
#define SCREEN_SPACE_REFLECTION_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "ScreenSpaceReflection_Hiz.hlsl"

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


bool ScreenSpaceRayMarching(half stepDirection, half end, inout float2 P, inout float3 Q, inout float k, float2 dP, float3 dQ,
    float dk, half rayZ, bool permute, inout int depthDistance, inout int stepCount, inout float2 hitUV, inout bool intersecting)
{
    bool stop = false;
    // 缓存当前深度和位置
    half prevZMaxEstimate = rayZ;
    half rayZMax = prevZMaxEstimate, rayZMin = prevZMaxEstimate;

    [loop]
    while ((P.x * stepDirection) <= end && stepCount < _MaximumIterationCount && !stop)
    {
        // 步近  
        P += dP;  
        Q.z += dQ.z;  
        k += dk;
        stepCount += 1;

        // 得到步近前后两点的深度
        prevZMaxEstimate = rayZ;
        rayZMin = prevZMaxEstimate;
        rayZMax = (dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);//当前射线深度
        prevZMaxEstimate = rayZMax;

        //确保rayZMin < rayZMax
        UNITY_FLATTEN
        if (rayZMin > rayZMax)
        {
            swap(rayZMin, rayZMax);
        }

        hitUV = permute ? P.yx : P;//恢复正确的坐标轴
        
        float sceneZ = -LinearEyeDepth(SampleSceneDepth(hitUV * _TestTex_TexelSize.xy), _ZBufferParams);
        bool isBehind = (rayZMin <= sceneZ);//如果光线深度小于深度图深度

        intersecting = isBehind && (rayZMax >= sceneZ - layerThickness);//光线与场景相交
        depthDistance = abs(sceneZ - rayZMax);
        stop = isBehind;
    }

    return intersecting;
}


// 根据原算法改正后
//Efficient GPU Screen-Space Ray Tracing https://zhuanlan.zhihu.com/p/686833098
//DDA (Digital Differential Analyzer)光线步进算法
Result March(Ray ray, float2 uv, float3 normalVS)
{
    Result result;
    result.isHit = false;
    result.position = 0.0;
    result.iterationCount = 0;
    result.uv = 0.0;

    //如果射线起点在相机后方 直接未命中
    UNITY_BRANCH
    if (ray.origin.z > 0)
    {
        return result;
    }
    
    half RayBump = max(-0.0002 * _Bandwidth * ray.origin.z, 0.001);
    half3 originVS = ray.origin + normalVS * RayBump;//射线起始坐标 沿着法线方向稍微偏移一下 避免自相交

    //确保射线不会超出近平面
    half rayLength = ((originVS.z + ray.direction.z * _MaximumMarchDistance) > - _ProjectionParams.y) ? ((-_ProjectionParams.y - originVS.z) / ray.direction.z) : _MaximumMarchDistance;
    half3 endPointVS = ray.direction * rayLength + originVS;

    //3D射线投影到2D屏幕空间
    float4 H0 = ProjectToScreenSpace(originVS);
    float4 H1 = ProjectToScreenSpace(endPointVS);
    half k0 = 1 / H0.w;
    half k1 = 1 / H1.w;
    float2 P0 = H0.xy * k0;      //屏幕空间起点
    float2 P1 = H1.xy * k1;      //屏幕空间终点
    float3 Q0 = originVS * k0;   //View空间起点 (齐次化)
    float3 Q1 = endPointVS * k1; //View空间终点 (齐次化)

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

    // 计算屏幕坐标、齐次视坐标、inverse-w的线性增量  
    half stepDirection = sign(delta.x);
    half invdx = stepDirection / delta.x;
    half2 dP = half2(stepDirection, invdx * delta.y);//屏幕空间步进
    half3 dQ = (Q1 - Q0) * invdx;//View空间步进
    half dk = (k1 - k0) * invdx;//齐次坐标步进

    dP *= _Bandwidth;
    dQ *= _Bandwidth;
    dk *= _Bandwidth;
    
    // jitter
    {
        #if JITTER_BLURNOISE
        uv *= _NoiseTiling;
        uv.y *= _AspectRatio;

        float jitter = SAMPLE_TEXTURE2D(_NoiseTex, sampler_LinearClamp, uv + _WorldSpaceCameraPos.xz).a;
        #elif JITTER_DITHER
        float2 ditherUV = fmod(P0, 4);  
        float jitter = dither[ditherUV.x * 4 + ditherUV.y];
        #else
        float jitter = 0;
        #endif
        
        P0 += dP * jitter;
        Q0 += dQ * jitter;
        k0 += dk * jitter;
    }

    half2 P = P0;
    half3 Q = Q0;
    half k = k0;

    // 缓存当前深度和位置
    float rayZ = originVS.z;
    half end = P1.x * stepDirection;

    int stepCount = 0;
    bool intersecting = false;
    half2 hitPixel = half2(0, 0);
    float depthDistance = 0.0;

    #if BINARY_SEARCH
    {
        //使用二分搜索来加速
        const int BINARY_COUNT = 3;
        bool stopBinary = false;
    
        UNITY_LOOP
        for (int i = 0; i < BINARY_COUNT && !stopBinary; i++)
        {
            if (ScreenSpaceRayMarching(stepDirection, end, P, Q, k, dP, dQ, dk, rayZ, permute, depthDistance, stepCount, hitPixel, intersecting))
            {
                if (depthDistance < layerThickness)
                {
                    intersecting = true;
                    stopBinary = true;
                }
                P -= dP;
                Q -= dQ;
                k -= dk;
                rayZ = Q / k;
    
                //步长减少
                dP *= 0.5;
                dQ *= 0.5;
                dk *= 0.5;
            }
        }
    }
    #endif
    
    {
        // intersecting = ScreenSpaceRayMarching(stepDirection, end, P, Q, k, dP, dQ, dk, rayZ, permute, depthDistance, stepCount, hitPixel, intersecting);
    }

    #if HIZ
    {
        // Hiz
        intersecting = ScreenSpaceRayMarchingHiz(stepDirection, end, P, Q, k, dP, dQ, dk, rayZ, permute, depthDistance, stepCount, hitPixel, intersecting);
    }
    #endif
    
    

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
    // uint materialFlags = UnpackMaterialFlags(SAMPLE_TEXTURE2D_LOD(_GBuffer0, sampler_PointClamp, input.texcoord, 0).a);
    // UNITY_BRANCH
    // if (IsMaterialFlagCharacter(materialFlags)) return 0;
    
    Ray ray;
    ray.origin = GetViewSpacePosition(input.texcoord);

    UNITY_BRANCH
    if (ray.origin.z < - _MaximumMarchDistance)
        return 0.0;

    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float3 normalVS = mul((float3x3)_ViewMatrixSSR, normalWS);
    ray.direction = normalize(reflect(normalize(ray.origin), normalVS));

    UNITY_BRANCH
    if (ray.direction.z > 0.0)
        return 0.0;

    Result result = March(ray, input.texcoord, normalVS);

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

    // uint materialFlags = UnpackMaterialFlags(gbuffer0.a);

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

    #if DEBUG_SCREEN_SPACE_REFLECTION
        indirectSpecular = 0;
    #endif

    half smoothness = gbuffer2.a;
    // https://www.desmos.com/calculator/k3hodgy8ry TODO 这个结果是个比较奇怪的曲线
    float fade = resolve.a * resolve.a * 3;
    // UNITY_BRANCH
    // if (_MobileMode == 2) fade = 1; // 简化计算
    distanceFade = saturate(distanceFade + smoothness);
    // fade是低频部分 理论上相当于一个锐化操作
    // fade = (1.0 - saturate(fade * smoothstep(0.5, 1.0, fade) * distanceFade)) * confidence;
    fade = distanceFade * confidence;

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
    float2 uv = input.texcoord;
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
