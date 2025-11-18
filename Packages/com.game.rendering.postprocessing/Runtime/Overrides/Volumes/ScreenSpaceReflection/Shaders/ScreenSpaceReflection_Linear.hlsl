#ifndef SCREEN_SPACE_REFLECTION_INEAR_INCLUDED
#define SCREEN_SPACE_REFLECTION_INEAR_INCLUDED

#include  "ScreenSpaceReflectionInput.hlsl"

bool RayIterations(inout half2 P,
                   inout half stepDirection, inout half end, inout int stepCount, inout int maxSteps,
                   inout bool intersecting,
                   inout half sceneZ, inout half2 dP, inout half3 Q, inout half3 dQ, inout half k, inout half dk,
                   inout half rayZMin, inout half rayZMax, inout half prevZMaxEstimate, inout bool permute,
                   inout half2 hitPixel,
                   half2 invSize, half layerThickness)
{
    bool stop = intersecting;
    
    UNITY_LOOP
    for (; (P.x * stepDirection) <= end && stepCount < maxSteps && !stop;)
    {
        // 步近  
        P += dP;  
        Q.z += dQ.z;  
        k += dk;
        stepCount += 1;
        
        rayZMin = prevZMaxEstimate;
        rayZMax = (dQ.z * 0.5 + Q.z) / (dk * 0.5 + k);
        prevZMaxEstimate = rayZMax;

        //确保rayZMin < rayZMax
        if (rayZMin > rayZMax)
        {
            Swap(rayZMin, rayZMax);
        }

        hitPixel = permute ? P.yx : P;
        sceneZ = -LinearEyeDepth(SampleSceneDepth(hitPixel * invSize), _ZBufferParams);
        bool isBehind = (rayZMin <= sceneZ);//如果光线深度小于深度图深度
        intersecting = isBehind && (rayZMax >= sceneZ - layerThickness);
        stop = isBehind;
    }

    P -= dP, Q.z -= dQ.z, k -= dk;

    return intersecting;
}

Result Linear2D_Trace(half3 csOrigin,
                    half3 csDirection,
                    half4 csZBufferSize,//Test Tex大小
                    half jitter,
                    float3 normalVS,
                    int maxSteps,
                    half layerThickness,
                    half traceDistance,
                    int stepSize,
                    in out half3 csHitPoint
                    )
{
    Result result;
    result.isHit = false;
    result.position = 0.0;
    result.iterationCount = 0;
    result.uv = 0.0;

    //如果射线起点在相机后方 直接未命中
    UNITY_BRANCH
    if (csOrigin.z > 0)
    {
        return result;
    }

    
    half RayBump = max(-0.0002 * stepSize * csOrigin.z, 0.001);
    csOrigin = csOrigin + normalVS * RayBump;//射线起始坐标 沿着法线方向稍微偏移一下 避免自相交
    
    //确保射线不会超出近平面
    half nearPlaneZ = -0.01;//_ProjectionParams.y
    half rayLength = ((csOrigin.z + csDirection.z * traceDistance) > nearPlaneZ)
                         ? ((nearPlaneZ - csOrigin.z) / csDirection.z)
                         : traceDistance;

    half3 csEndPoint = csDirection * rayLength + csOrigin;
    //3D射线投影到2D屏幕空间
    half4 H0 = TransformViewToHScreen(csOrigin, csZBufferSize.zw);
    half4 H1 = TransformViewToHScreen(csEndPoint, csZBufferSize.zw);

    half k0 = 1 / H0.w;
    half k1 = 1 / H1.w;
    half2 P0 = H0.xy * k0;              //屏幕空间起点
    half2 P1 = H1.xy * k1;              //屏幕空间终点
    half3 Q0 = csOrigin * k0;           //View空间起点 (齐次化)
    half3 Q1 = csEndPoint * k1;         //View空间终点 (齐次化)

    /*
    half yMax = csZBufferSize.y - 0.5;
    half yMin = 0.5;
    half xMax = csZBufferSize.x - 0.5;
    half xMin = 0.5;
    half alpha = 0;

    if (P1.y > yMax || P1.y < yMin)
    {
        half yClip = (P1.y > yMax) ? yMax : yMin;
        half yAlpha = (P1.y - yClip) / (P1.y - P0.y);
        alpha = yAlpha;
    }
    if (P1.x > xMax || P1.x < xMin)
    {
        half xClip = (P1.x > xMax) ? xMax : xMin;
        half xAlpha = (P1.x - xClip) / (P1.x - P0.x);
        alpha = max(alpha, xAlpha);
    }

    P1 = lerp(P1, P0, alpha);
    k1 = lerp(k1, k0, alpha);
    Q1 = lerp(Q1, Q0, alpha);
    */
    P1 = (GetSquaredDistance(P0, P1) < 0.0001) ? P0 + half2(_SSR_TestTex_TexelSize.x, _SSR_TestTex_TexelSize.y) : P1;
    // P1 = (GetSquaredDistance(P0, P1) < 0.0001) ? P0 + half2(0.01, 0.01) : P1;
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
    half2 dP = half2(stepDirection, invdx * delta.y);       //屏幕空间步进
    half3 dQ = (Q1 - Q0) * invdx;                           //View空间步进
    half dk = (k1 - k0) * invdx;                            //齐次坐标步进

    dP *= stepSize;
    dQ *= stepSize;
    dk *= stepSize;
    P0 += dP * jitter;
    Q0 += dQ * jitter;
    k0 += dk * jitter;

    half3 Q = Q0;
    half k = k0;
    half prevZMaxEstimate = csOrigin.z;
    half rayZMax = prevZMaxEstimate, rayZMin = prevZMaxEstimate;

    half sceneZ = 10000;
    half end = P1.x * stepDirection;
    bool intersecting = IntersectsDepthBuffer(rayZMin, rayZMax, sceneZ, layerThickness);
    half2 P = P0;
    int originalStepCount = 0;

    float2 hitPixel = half2(0, 0);

    RayIterations(P, stepDirection, end, originalStepCount,
                  maxSteps,
                  intersecting, sceneZ, dP, Q, dQ, k, dk, rayZMin, rayZMax, prevZMaxEstimate, permute, hitPixel,
                  csZBufferSize.xy, layerThickness);

    int stepCount = originalStepCount;
    Q.xy += dQ.xy * stepCount;
    csHitPoint = Q * (1 / k);


    UNITY_FLATTEN
    if (intersecting)
    {
        result.iterationCount = stepCount;
        result.uv = hitPixel * csZBufferSize.xy;
        result.isHit = true;
    }
    return result;
}


float4 FragTestLinear(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    float rawDepth = SampleSceneDepth(uv);

    UNITY_BRANCH
    if (IsInfinityFar(rawDepth))
    {
        return float4(input.texcoord, 0, 0);
    }
    
    // 多一次采样 可以过滤掉角色部分的射线计算
    // uint materialFlags = UnpackMaterialFlags(SAMPLE_TEXTURE2D_LOD(_GBuffer0, sampler_PointClamp, input.texcoord, 0).a);
    // UNITY_BRANCH
    // if (IsMaterialFlagCharacter(materialFlags)) return 0;
    
    Ray ray;
    ray.origin = GetViewSpacePosition(rawDepth, uv);

    //太远的点也直接跳过
    UNITY_BRANCH
    if (ray.origin.z < - _MaximumMarchDistance)
        return 0.0;
    float3 normalWS = GetNormalWS(uv);
    float3 normalVS = mul((float3x3)_ViewMatrixSSR, normalWS);
    float3 reflectionDirectionVS = normalize(reflect(normalize(ray.origin), normalVS));
    ray.direction = reflectionDirectionVS;

    UNITY_BRANCH
    if (ray.direction.z > 0.0)
        return 0.0;

    #if JITTER_BLURNOISE
    uv *= _NoiseTiling;
    uv.y *= _AspectRatio;

    float jitter = SAMPLE_TEXTURE2D(_NoiseTex, sampler_LinearClamp, uv + _WorldSpaceCameraPos.xz).a;
    #elif JITTER_DITHER
    float2 ditherUV = input.texcoord * _ScreenParams.xy;
    uint ditherIndex = (uint(ditherUV.x) % 4) * 4 + uint(ditherUV.y) % 4;
    float jitter = 1.0f + (1.0f - dither[ditherIndex]);
    #else
    float jitter = 0;
    #endif
    
    float3 hitPointVS = ray.origin;
    Result result = Linear2D_Trace(ray.origin,
                                   ray.direction,
                                   _SSR_TestTex_TexelSize,
                                   jitter,
                                   normalVS,
                                   _MaximumIterationCount,
                                   Thickness,
                                   _MaximumMarchDistance,
                                   _Bandwidth,
                                   hitPointVS
                                   );

    float confidence = (float)result.iterationCount / (float)_MaximumIterationCount;

    return float4(result.uv, confidence, (float)result.isHit);
}


#endif // SCREEN_SPACE_REFLECTION_INCLUDED
