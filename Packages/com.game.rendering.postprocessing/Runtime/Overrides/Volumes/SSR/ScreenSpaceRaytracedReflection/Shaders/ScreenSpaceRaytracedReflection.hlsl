#ifndef SCREEN_SPACE_RAYTRACED_REFLECTION_INCLUDED
#define SCREEN_SPACE_RAYTRACED_REFLECTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"


TEXTURE2D_HALF(_GBuffer0);
TEXTURE2D_HALF(_GBuffer1);
TEXTURE2D_HALF(_GBuffer2);

TEXTURE2D(_MetallicGradientTex);
TEXTURE2D(_SmoothnessGradientTex);

TEXTURE2D(_DownscaledShinyDepthRT);

TEXTURE2D(_NoiseTex);   float4 _NoiseTex_TexelSize;

float4x4 _InverseProjectionMatrix;
float4x4 _WorldToViewDir;

float4 _SSRSettings;
float4 _SSRSettings2;
float4 _SSRSettings3;
float4 _SSRSettings4;
float4 _SSRSettings5;

#define THICKNESS                   _SSRSettings.x
#define SAMPLES                     _SSRSettings.y
#define BINARY_SEARCH_ITERATIONS    _SSRSettings.z
#define MAX_RAY_LENGTH              _SSRSettings.w
#define JITTER                      _SSRSettings2.x
#define CONTACT_HARDENING           _SSRSettings2.y
#define REFLECTIVITY                _SSRSettings2.w
#define INPUT_SIZE                  _SSRSettings3.xy
#define GOLDEN_RATIO_ACUM           _SSRSettings3.z
#define DEPTH_BIAS                  _SSRSettings3.w

float4 _MaterialData;
#define SMOOTHNESS                  _MaterialData.x
#define FRESNEL                     _MaterialData.y
#define FUZZYNESS                   _MaterialData.z
#define DECAY                       _MaterialData.w



#if SSR_THICKNESS_FINE
    #define THICKNESS_FINE _SSRSettings5.x
#else
    #define THICKNESS_FINE THICKNESS
#endif


inline half3 GetScreenSpacePos(half2 uv, half depth)
{
    return half3(uv.xy * 2 - 1, depth.r);
}

inline half3 GetViewSpacePos(half3 screenPos, half4x4 _InverseProjectionMatrix)
{
    half4 viewPos = mul(_InverseProjectionMatrix, half4(screenPos, 1));
    return viewPos.xyz / viewPos.w;
}


inline float GetLinearDepth(float2 uv)
{
    float depth = SAMPLE_TEXTURE2D_X_LOD(_DownscaledShinyDepthRT, sampler_PointClamp, uv, 0).r;
    return depth;
}

//--------------------------------------------------------------
half4 FragCopyDepth(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    float depth = SampleSceneDepth(uv).r;
    depth = LinearEyeDepth(depth, _ZBufferParams);
    // #if SSR_BACK_FACES
    //     float backDepth = SAMPLE_TEXTURE2D_X(_DownscaledShinyBackDepthRT, sampler_PointClamp, i.uv.xy).r;
    //     backDepth = LinearEyeDepth(backDepth, _ZBufferParams);
    //     backDepth = clamp(backDepth, depth + MINIMUM_THICKNESS, depth + THICKNESS);
    //     return half4(depth, backDepth, 0, 1.0);
    // #else
        return half4(depth.xxx, 1.0);
    // #endif

}

//----------------------------------------------------------------
float4 SSR_Pass(float2 uv, float3 normalVS, float3 rayStart, float roughness, float metallic)
{
    float3 viewDirVS = normalize(rayStart);
    float3 rayDir = reflect(viewDirVS, normalVS);

    // if ray is toward the camera, early exit (optional)
    //if (rayDir.z < 0) return 0.0.xxxx;

    float rayLength = MAX_RAY_LENGTH;

    float3 rayEnd = rayStart + rayDir * rayLength;
    if (rayEnd.z < _ProjectionParams.y)
    {
        rayLength = (_ProjectionParams.y - rayStart.z) / rayDir.z;
        rayEnd = rayStart + rayDir * rayLength;
    }

    float4 sposStart = mul(unity_CameraProjection, float4(rayStart, 1.0));
    float4 sposEnd = mul(unity_CameraProjection, float4(rayEnd, 1.0));
    float k0 = rcp(sposStart.w);
    float q0 = rayStart.z * k0;
    float k1 = rcp(sposEnd.w);
    float q1 = rayEnd.z * k1;
    float4 p = float4(uv, q0, k0);

    // length in pixels
    float2 uv1 = (sposEnd.xy * rcp(rayEnd.z) + 1.0) * 0.5;
    float2 duv = uv1 - uv;
    float2 duvPixel = abs(duv * INPUT_SIZE);
    float pixelDistance = max(duvPixel.x, duvPixel.y);
    int sampleCount = (int)clamp(pixelDistance, 1, SAMPLES);
    float4 pincr = float4(duv, q1 - q0, k1 - k0) * rcp(sampleCount);

    #if SSR_JITTER
        float jitter = SAMPLE_TEXTURE2D(_NoiseTex, sampler_PointRepeat, uv * INPUT_SIZE * _NoiseTex_TexelSize.xy + GOLDEN_RATIO_ACUM).r;
        pincr *= 1.0 + jitter * JITTER;
        p += pincr * (jitter * JITTER);
    #endif

    float collision = 0;
    float dist = 0;
    float zdist = 0;
    float sceneDepth, sceneBackDepth, depthDiff;

    UNITY_LOOP
    for (int k = 0; k < sampleCount; k++)
    {
        p += pincr;
        if (any(floor(p.xy) != 0)) return 0.0.xxxx; // exit if out of screen space
        float pz = p.z / p.w;

        #if SSR_BACK_FACES
            GetLinearDepths(p.xy, sceneDepth, sceneBackDepth);
            if (pz >= sceneDepth && pz <= sceneBackDepth)
        #else
            sceneDepth = GetLinearDepth(p.xy);
            depthDiff = pz - sceneDepth;
            if (depthDiff > 0 && depthDiff < THICKNESS)
        #endif
        {
            float4 origPincr = pincr;
            p -= pincr;
            float reduction = 1.0;

            UNITY_LOOP
            for (int j = 0; j < BINARY_SEARCH_ITERATIONS; j++)
            {
                reduction *= 0.5;
                p += pincr * reduction;
                pz = p.z / p.w;
                sceneDepth = GetLinearDepth(p.xy);
                depthDiff = sceneDepth - pz;
                pincr = sign(depthDiff) * origPincr;
            }
            #if SSR_THICKNESS_FINE
                if (abs(depthDiff) < THICKNESS_FINE)
            #endif
            {
                float hitAccuracy = 1.0 - abs(depthDiff) / THICKNESS_FINE;
                zdist = (pz - rayStart.z) / (0.0001 + rayEnd.z - rayStart.z);
                float rayFade = 1.0 - saturate(zdist);
                collision = hitAccuracy * rayFade;
                break;
            }
            pincr = origPincr;
            p += pincr;
        }
    }

    #if SSR_SKYBOX
        float reflectionIntensity = metallic;
        if (collision <= 0 && sceneDepth > _ProjectionParams.z - 1.0)
        {
            zdist = 1;
            reflectionIntensity *= SKYBOX_INTENSITY;
        }
        else
        {
            reflectionIntensity *= pow(collision, DECAY);
        }
    #else
        if (collision > 0)
        {
            float reflectionIntensity = metallic;
            reflectionIntensity *= pow(collision, DECAY);

    #endif

    // intersection found

    float wdist = rayLength * zdist;
    float fresnel = 1.0 - FRESNEL * abs(dot(normalVS, viewDirVS));

    float blurAmount = max(0, wdist - CONTACT_HARDENING) * FUZZYNESS * roughness;

    // apply fresnel
    float reflectionAmount = reflectionIntensity * fresnel;

    // return hit pixel
    return float4(p.xy, blurAmount + 0.001, reflectionAmount);

    #if !SSR_SKYBOX
    }
    
    #endif

    return float4(0, 0, 0, 0);
}

struct VaryingsSSR
{
    float4 positionCS : SV_POSITION;
    float4 texcoord : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};


VaryingsSSR VertSSR(Attributes input)
{
    VaryingsSSR output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
    float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

    output.positionCS = pos;
    output.texcoord.xy = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

    float4 projPos = output.positionCS * 0.5;
    projPos.xy = projPos.xy + projPos.w;
    output.texcoord.zw = projPos.xy;

    return output;
}

half4 FragSSR(VaryingsSSR input) : SV_Target
{
    float2 uv = input.texcoord.xy;
    
    float depth = SampleSceneDepth(uv);
    #if UNITY_REVERSED_Z
        depth = 1.0 - depth;
    #endif
    if (depth >= 1.0)
        return float4(0, 0, 0, 0);

    // half3 ScreenPos = GetScreenSpacePos(uv, depth);
    // half3 positionVS = GetViewSpacePos(ScreenPos, _InverseProjectionMatrix);
    float3 positionVS = ComputeViewSpacePosition(input.texcoord.zw, depth, unity_CameraInvProjection);
    // return half4(positionVS, 1);
    
    float4 gbuffer0 = SAMPLE_TEXTURE2D_X(_GBuffer0, sampler_PointClamp, uv);
    float4 gbuffer1 = SAMPLE_TEXTURE2D_X(_GBuffer1, sampler_PointClamp, uv);
    float4 gbuffer2 = SAMPLE_TEXTURE2D_X(_GBuffer2, sampler_PointClamp, uv);

    #if defined(_GBUFFER_NORMALS_OCT)
        half2 remappedOctNormalWS = Unpack888ToFloat2(gbuffer2.xyz); // values between [ 0,  1]
        half2 octNormalWS = remappedOctNormalWS.xy * 2.0h - 1.0h;    // values between [-1, +1]
        float3 normalWS = UnpackNormalOctQuadEncode(octNormalWS);
    #else
        float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    #endif
    
    float3 normalVS = mul((float3x3)_WorldToViewDir, normalWS);
    normalVS.z *= -1.0;

    float metallic = gbuffer1.r;//金属度
    float smoothness = gbuffer2.a;

    metallic = SAMPLE_TEXTURE2D_LOD(_MetallicGradientTex, sampler_LinearClamp, float2(metallic, 0), 0).r;
    if (metallic <= 0) return 0;

    float roughness = SAMPLE_TEXTURE2D_LOD(_SmoothnessGradientTex, sampler_LinearClamp, float2(1.0 - smoothness, 0), 0).r;
    float4 reflection = SSR_Pass(uv, normalVS, positionVS, roughness, metallic);

    return reflection;
}

#endif // SCREEN_SPACE_RAYTRACED_REFLECTION_INCLUDED
