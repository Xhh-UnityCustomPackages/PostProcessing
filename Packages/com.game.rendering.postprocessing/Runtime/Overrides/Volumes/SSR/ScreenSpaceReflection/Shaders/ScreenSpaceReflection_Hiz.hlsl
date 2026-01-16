#ifndef SCREEN_SPACE_REFLECTION_HIZ_INCLUDED
#define SCREEN_SPACE_REFLECTION_HIZ_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/DeclarePyramidDepthTexture.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/ShaderVariablesGlobal.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/Raytracing/RaytracingSampling.hlsl"

float GetDepthSample(float2 positionSS)
{
    return LOAD_TEXTURE2D_X(_DepthPyramid, positionSS).r;
}

#define SSR_TRACE_BEHIND_OBJECTS
#define SSR_TRACE_TOWARDS_EYE
#define SAMPLES_VNDF
#define SSR_TRACE_EPS               0.000488281f // 2^-11, should be good up to 4K

#define _SsrReflectsSky         0
#define _FrameCount                 _TaaFrameInfo.y

// Specialization without Fresnel (see PathTracingBSDF.hlsl for the reference implementation)
bool SampleGGX_VNDF(float roughness_,
                    float3x3 localToWorld,
                    float3 V,
                    float2 inputSample,
                out float3 outgoingDir,
                out float weight)
{
    weight = 0.0f;

    float roughness = clamp(roughness_, MIN_GGX_ROUGHNESS, MAX_GGX_ROUGHNESS);

    float VdotH;
    float3 localV, localH;
    SampleGGXVisibleNormal(inputSample.xy, V, localToWorld, roughness, localV, localH, VdotH);

    // Compute the reflection direction
    float3 localL = 2.0 * VdotH * localH - localV;
    outgoingDir = mul(localL, localToWorld);

    if (localL.z < 0.001)
    {
        return false;
    }

    weight = GetSSRSampleWeight(localV, localL, roughness);

    if (weight < 0.001)
        return false;

    return true;
}
void GetHitInfos(uint2 positionSS, out float srcPerceptualRoughness, out float3 positionWS,
    out float weight, out float3 N, out float3 L, out float3 V,
    out float NdotL, out float NdotH, out float VdotH, out float NdotV)
{
    // float2 uv = float2(positionSS) * _ScreenParams.xy;

    float2 Xi = 0;
    // 下面会显得更加Dither
    Xi.x = GetBNDSequenceSample(positionSS, _FrameCount, 0) * DOWNSAMPLE;
    Xi.y = GetBNDSequenceSample(positionSS, _FrameCount, 1) * DOWNSAMPLE;
    
    half4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, positionSS);
    float smoothness = gbuffer2.a;
    N = normalize(UnpackNormal(gbuffer2.xyz));
    
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
    srcPerceptualRoughness = perceptualRoughness;
    
    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    float3x3 localToWorld = GetLocalFrame(N);

    float coefBias = _SsrPBRBias / roughness;
    Xi.x = lerp(Xi.x, 0.0f, roughness * coefBias);

    float deviceDepth = GetDepthSample(positionSS);
    
    float2 positionNDC = positionSS * _ScreenSize.zw + (0.5 * _ScreenSize.zw);
    positionWS = ComputeWorldSpacePosition(positionNDC, deviceDepth, UNITY_MATRIX_I_VP);
    V = GetWorldSpaceNormalizeViewDir(positionWS);
    
    #ifdef SAMPLES_VNDF
    SampleGGX_VNDF(roughness,
        localToWorld,
        V,
        Xi,
        L,
        weight);

    NdotV = dot(N, V);
    NdotL = dot(N, L);
    float3 H = normalize(V + L);
    NdotH = dot(N, H);
    VdotH = dot(V, H);
    #else
    SampleGGXDir(Xi, V, localToWorld, roughness, L, NdotL, NdotH, VdotH);

    NdotV = dot(N, V);
    float Vg = V_SmithJointGGX(NdotL, NdotV, roughness);

    weight = 4.0f * NdotL * VdotH * Vg / NdotH;
    #endif
}

half4 ScreenSpaceReflection(uint2 positionSS)
{
    // 多一次采样 可以过滤掉不需要SSR的计算
    bool doesntReceiveSSR = false;
    // uint stencilValue = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, positionSS.xy));
    // doesntReceiveSSR = (stencilValue & STENCIL_USAGE_IS_SSR) == 0;
    if (doesntReceiveSSR)
    {
        return half4(0, 0, 0, 0);
    }
    
    float weight;
    float NdotL, NdotH, VdotH, NdotV;
    float3 R, V, N;
    float3 positionWS;
    float perceptualRoughness;
    GetHitInfos(positionSS, perceptualRoughness, positionWS, weight, N, R, V, NdotL, NdotH, VdotH, NdotV);
    
    float3 camPosWS = GetCurrentViewPosition();
    // Apply normal bias with the magnitude dependent on the distance from the camera.
    // Unfortunately, we only have access to the shading normal, which is less than ideal...
    positionWS = camPosWS + (positionWS - camPosWS) * (1 - 0.001 * rcp(max(dot(N, V), FLT_EPS)));
    float deviceDepth = ComputeNormalizedDeviceCoordinatesWithZ(positionWS, UNITY_MATRIX_VP).z;
    
    bool killRay = deviceDepth == UNITY_RAW_FAR_CLIP_VALUE;
    // Ref. #1: Michal Drobot - Quadtree Displacement Mapping with Height Blending.
    // Ref. #2: Yasin Uludag  - Hi-Z Screen-Space Cone-Traced Reflections.
    // Ref. #3: Jean-Philippe Grenier - Notes On Screen Space HIZ Tracing.
    // Warning: virtually all the code below assumes reverse Z.

    // We start tracing from the center of the current pixel, and do so up to the far plane.
    float3 rayOrigin = float3(positionSS + 0.5, deviceDepth);
    
    float3 reflPosWS  = positionWS + R;
    float3 reflPosNDC = ComputeNormalizedDeviceCoordinatesWithZ(reflPosWS, UNITY_MATRIX_VP); // Jittered
    float3 reflPosSS  = float3(reflPosNDC.xy * _ScreenSize.xy, reflPosNDC.z);
    float3 rayDir     = reflPosSS - rayOrigin;
    float3 rcpRayDir  = rcp(rayDir);
    int2   rayStep    = int2(rcpRayDir.x >= 0 ? 1 : 0,
                             rcpRayDir.y >= 0 ? 1 : 0);
    float3 raySign  = float3(rcpRayDir.x >= 0 ? 1 : -1,
                             rcpRayDir.y >= 0 ? 1 : -1,
                             rcpRayDir.z >= 0 ? 1 : -1);
    bool   rayTowardsEye  =  rcpRayDir.z >= 0;
    
    // Note that we don't need to store or read the perceptualRoughness value
    // if we mark stencil during the G-Buffer pass with pixels which should receive SSR,
    // and sample the color pyramid during the lighting pass.
    killRay = killRay || (reflPosSS.z <= 0);
    killRay = killRay || (dot(N, V) <= 0);
    killRay = killRay || (perceptualRoughness > _SsrRoughnessFadeEnd);
    #ifndef SSR_TRACE_TOWARDS_EYE
    killRay = killRay || rayTowardsEye;
    #endif
    
    if (killRay)
    {
        return float4(0, 0, 0, 0);
    }
        
    // Extend and clip the end point to the frustum.
    float tMax;
    {
        // Shrink the frustum by half a texel for efficiency reasons.
        const float halfTexel = 0.5;

        float3 bounds;
        bounds.x = (rcpRayDir.x >= 0) ? _ScreenSize.x - halfTexel : halfTexel;
        bounds.y = (rcpRayDir.y >= 0) ? _ScreenSize.y - halfTexel : halfTexel;
        // If we do not want to intersect the skybox, it is more efficient to not trace too far.
        float maxDepth = _SsrReflectsSky != 0 ? -0.00000024 : 0.00000024; // 2^-22
        bounds.z = (rcpRayDir.z >= 0) ? 1 : maxDepth;

        float3 dist = bounds * rcpRayDir - (rayOrigin * rcpRayDir);
        tMax = Min3(dist.x, dist.y, dist.z);
    }
    
    // Clamp the MIP level to give the compiler more information to optimize.
    const int maxMipLevel = min(_SsrDepthPyramidMaxMip, 14);
    
    // Start ray marching from the next texel to avoid self-intersections.
    float t;
    {
        // 'rayOrigin' is the exact texel center.
        float2 dist = abs(0.5 * rcpRayDir.xy);
        t = min(dist.x, dist.y);
    }
    
    float3 rayPos;

    int  mipLevel  = 0;
    int  iterCount = 0;
    bool hit       = false;
    bool miss      = false;
    bool belowMip0 = false; // This value is set prior to entering the cell
    
    while (!(hit || miss) && t <= tMax && iterCount < _MaximumIterationCount)
    {
        rayPos = rayOrigin + t * rayDir;
        
        // Ray position often ends up on the edge. To determine (and look up) the right cell,
        // we need to bias the position by a small epsilon in the direction of the ray.
        float2 sgnEdgeDist = round(rayPos.xy) - rayPos.xy;
        float2 satEdgeDist = clamp(raySign.xy * sgnEdgeDist + SSR_TRACE_EPS, 0, SSR_TRACE_EPS);
        rayPos.xy += raySign.xy * satEdgeDist;

        int2 mipCoord  = (int2)rayPos.xy >> mipLevel;
        int2 mipOffset = _DepthPyramidMipLevelOffsets[mipLevel];
        // Bounds define 4 faces of a cube:
        // 2 walls in front of the ray, and a floor and a base below it.
        float4 bounds;

        bounds.xy = (mipCoord + rayStep) << mipLevel;
        bounds.z  = LOAD_TEXTURE2D_X(_DepthPyramid, mipOffset + mipCoord).r;
        
        // We define the depth of the base as the depth value as:
        // b = DeviceDepth((1 + thickness) * LinearDepth(d))
        // b = ((f - n) * d + n * (1 - (1 + thickness))) / ((f - n) * (1 + thickness))
        // b = ((f - n) * d - n * thickness) / ((f - n) * (1 + thickness))
        // b = d / (1 + thickness) - n / (f - n) * (thickness / (1 + thickness))
        // b = d * k_s + k_b
        bounds.w = bounds.z * _SsrThicknessScale + _SsrThicknessBias;

        float4 dist      = bounds * rcpRayDir.xyzz - (rayOrigin.xyzz * rcpRayDir.xyzz);
        float  distWall  = min(dist.x, dist.y);
        float  distFloor = dist.z;
        float  distBase  = dist.w;
        
        // Note: 'rayPos' given by 't' can correspond to one of several depth values:
        // - above or exactly on the floor
        // - inside the floor (between the floor and the base)
        // - below the base
        #if 0
        bool belowFloor  = (raySign.z * (t - distFloor)) <  0;
        bool aboveBase   = (raySign.z * (t - distBase )) >= 0;
        #else
        bool belowFloor  = rayPos.z  < bounds.z;
        bool aboveBase   = rayPos.z >= bounds.w;
        #endif
        bool insideFloor = belowFloor && aboveBase;
        bool hitFloor    = (t <= distFloor) && (distFloor <= distWall);
        
        // Game rules:
        // * if the closest intersection is with the wall of the cell, switch to the coarser MIP, and advance the ray.
        // * if the closest intersection is with the heightmap below,  switch to the finer   MIP, and advance the ray.
        // * if the closest intersection is with the heightmap above,  switch to the finer   MIP, and do NOT advance the ray.
        // Victory conditions:
        // * See below. Do NOT reorder the statements!

        #ifdef SSR_TRACE_BEHIND_OBJECTS
        miss      = belowMip0 && insideFloor;
        #else
        miss      = belowMip0;
        #endif
        hit       = (mipLevel == 0) && (hitFloor || insideFloor);
        belowMip0 = (mipLevel == 0) && belowFloor;
        
        // 'distFloor' can be smaller than the current distance 't'.
        // We can also safely ignore 'distBase'.
        // If we hit the floor, it's always safe to jump there.
        // If we are at (mipLevel != 0) and we are below the floor, we should not move.
        t = hitFloor ? distFloor : (((mipLevel != 0) && belowFloor) ? t : distWall);
        rayPos.z = bounds.z; // Retain the depth of the potential intersection

        // Warning: both rays towards the eye, and tracing behind objects has linear
        // rather than logarithmic complexity! This is due to the fact that we only store
        // the maximum value of depth, and not the min-max.
        mipLevel += (hitFloor || belowFloor || rayTowardsEye) ? -1 : 1;
        mipLevel  = clamp(mipLevel, 0, maxMipLevel);
        
        iterCount++;
    }
    
    // Treat intersections with the sky as misses.
    miss = miss || ((_SsrReflectsSky == 0) && (rayPos.z == 0));
    hit  = hit && !miss;

    if (hit)
    {
        // Note that we are using 'rayPos' from the penultimate iteration, rather than
        // recompute it using the last value of 't', which would result in an overshoot.
        // It also needs to be precisely at the center of the pixel to avoid artifacts.
        float2 hitPositionNDC = floor(rayPos.xy) * _ScreenSize.zw + 0.5 * _ScreenSize.zw; // Should we precompute the half-texel bias? We seem to use it a lot.
        return float4(hitPositionNDC.xy, 0, 1);
    }
    
    return float4(0, 0, 0, 0);
}


#endif // SCREEN_SPACE_REFLECTION_INCLUDED
