#ifndef SCREEN_SPACE_REFLECTION_HIZ_INCLUDED
#define SCREEN_SPACE_REFLECTION_HIZ_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "ScreenSpaceReflection_Linear.hlsl"

//Hiz
TEXTURE2D(_HizDepthTexture);                SAMPLER(sampler_HizDepthTexture);
int _HizDepthTextureMipLevel;

float4 _HizDepthTexture_TexelSize;

#define HIZ_MAX_LEVEL 9
#define HIZ_STOP_LEVEL 0
#define HIZ_START_LEVEL 1

inline float3 intersectCellBoundary(float3 o, float3 d, float2 cellIndex, float2 cellCount, float2 crossStep,
                                    float2 crossOffset)
{
    float2 cell_size = 1.0 / cellCount;
    float2 planes = cellIndex / cellCount + cell_size * crossStep;
    float2 solutions = (planes - o) / d.xy;
    float3 intersection_pos = o + d * min(solutions.x, solutions.y);

    intersection_pos.xy += (solutions.x < solutions.y) ? float2(crossOffset.x, 0.0) : float2(0.0, crossOffset.y);
    return intersection_pos;
}

// inline float2 scaledUv(float2 uv, uint index)
// {
//     float2 scaledScreen = getLevelResolution(index);
//     float2 realScale = scaledScreen.xy / getScreenResolution();
//     uv *= realScale;
//     return uv;
// }
//
// inline float sampleDepth(float2 uv, uint index)
// {
//     uv = scaledUv(uv, index);
//     return 1.0 - UNITY_SAMPLE_TEX2DARRAY(_DepthPyramid, float3(uv, index));
// }
//
// inline float minimum_depth_plane(float2 ray, float level)
// {
//     return sampleDepth(ray, level);
// }

// inline float3 hiZTrace(float thickness, float3 p, float3 v, float MaxIterations, out float hit, out float iterations)
// {
//     const int rootLevel = HIZ_MAX_LEVEL;
//     const int endLevel = HIZ_STOP_LEVEL;
//     const int startLevel = HIZ_START_LEVEL;
//     int level = HIZ_START_LEVEL;
//
//     iterations = 0;
//     // isSky = false;
//     hit = 0;
//
//     [branch]
//     if (v.z <= 0)
//     {
//         return float3(0, 0, 0);
//     }
//
//     // scale vector such that z is 1.0f (maximum depth)
//     float3 d = v.xyz / v.z;
//     // get the cell cross direction and a small offset to enter the next cell when doing cell crossing
//     float2 crossStep = float2(d.x >= 0.0f ? 1.0f : -1.0f, d.y >= 0.0f ? 1.0f : -1.0f);
//     float2 crossOffset = float2(crossStep.xy * 0.0001); // float2(crossStep.xy * cross_epsilon() );
//     crossStep.xy = saturate(crossStep.xy);
//
//     // set current ray to original screen coordinate and depth
//     float3 ray = p.xyz;
//     // cross to next cell to avoid immediate self-intersection
//     float2 rayCell = cell(ray.xy, cell_count(level));
//     ray = intersectCellBoundary(ray, d, rayCell.xy, cell_count(level), crossStep.xy, crossOffset.xy);
//     [loop]
//     while (level >= endLevel
//         && iterations < MaxIterations
//         && ray.x >= 0 && ray.x < 1
//         && ray.y >= 0 && ray.y < 1
//         && ray.z > 0)
//     {
//         isSky = false;
//         // get the cell number of the current ray
//         const float2 cellCount = cell_count(level);
//         const float2 oldCellIdx = cell(ray.xy, cellCount);
//
//         // get the minimum depth plane in which the current ray resides
//         float minZ = minimum_depth_plane(ray.xy, level);
//
//         // intersect only if ray depth is below the minimum depth plane
//         float3 tmpRay = ray;
//         float min_minus_ray = minZ - ray.z;
//
//         tmpRay = min_minus_ray > 0 ? intersectDepthPlane(tmpRay, d, min_minus_ray) : tmpRay;
//
//         // get the new cell number as well
//         const float2 newCellIdx = cell(tmpRay.xy, cellCount);
//         // if the new cell number is different from the old cell number, a cell was crossed
//         [branch]
//         if (crossed_cell_boundary(oldCellIdx, newCellIdx))
//         {
//             // intersect the boundary of that cell instead, and go up a level for taking a larger step next iteration
//             tmpRay = intersectCellBoundary(ray, d, oldCellIdx, cellCount.xy, crossStep.xy, crossOffset.xy);
//             level = min(rootLevel, level + 2.0f);
//         }
//         else if (level == startLevel)
//         {
//             float minZOffset = (minZ + (1.0 - p.z) * thickness);
//             // isSky = minZ == 1;
//             if (minZ >= 1)
//                 break;
//             [flatten]
//             if (abs(min_minus_ray) >= 0.00002f)
//             {
//                 tmpRay = intersectCellBoundary(ray, d, oldCellIdx, cellCount.xy, crossStep.xy, crossOffset.xy);
//                 level = HIZ_START_LEVEL + 1;
//             }
//         }
//         // go down a level in the hi-z buffer
//         --level;
//         ray.xyz = tmpRay.xyz;
//         ++iterations;
//     }
//     hit = level < endLevel ? 1 : 0;
//     hit = iterations > 0 ? hit : 0;
//     return ray;
// }


float4 FragTestHiZ(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);

    //朝上的像素 不发生反射直接跳过
    UNITY_BRANCH
    if (dot(gbuffer2.xyz, 1.0) == 0.0)
        return 0.0;

    float rawDepth = SampleSceneDepth(uv);
    
    UNITY_BRANCH
    if (IsInfinityFar(rawDepth))
    {
        return float4(input.texcoord, 0, 0);
    }
    
    Ray ray;
    ray.origin = GetViewSpacePosition(rawDepth, uv);

    //太远的点也直接跳过
    UNITY_BRANCH
    if (ray.origin.z < - _MaximumMarchDistance)
        return 0.0;

    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
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
                                       _TestTex_TexelSize,
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
