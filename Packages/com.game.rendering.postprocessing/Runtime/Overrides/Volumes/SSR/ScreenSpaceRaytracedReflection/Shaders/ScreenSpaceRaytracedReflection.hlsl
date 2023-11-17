#ifndef SCREEN_SPACE_RAYTRACED_REFLECTION_INCLUDED
#define SCREEN_SPACE_RAYTRACED_REFLECTION_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

#include "ScreenSpaceRaytracedReflectionInput.hlsl"


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
#if SSR_METALLIC_WORKFLOW
    float4 SSR_Pass(float2 uv, float3 normalVS, float3 rayStart, float roughness, float metallic)
#else
    float4 SSR_Pass(float2 uv, float3 normalVS, float3 rayStart, float roughness)
#endif
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

        // #if SSR_BACK_FACES
        //     GetLinearDepths(p.xy, sceneDepth, sceneBackDepth);
        //     if (pz >= sceneDepth && pz <= sceneBackDepth)
        // #else
            sceneDepth = GetLinearDepth(p.xy);
        depthDiff = pz - sceneDepth;
        if (depthDiff > 0 && depthDiff < THICKNESS)
        // #endif

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


    if (collision > 0)
    {
        #if SSR_METALLIC_WORKFLOW
            float reflectionIntensity = metallic;
        #else
            float reflectionIntensity = (1.0 - roughness);
        #endif
        reflectionIntensity *= pow(collision, DECAY);


        // intersection found

        float wdist = rayLength * zdist;
        float fresnel = 1.0 - FRESNEL * abs(dot(normalVS, viewDirVS));

        float blurAmount = max(0, wdist - CONTACT_HARDENING) * FUZZYNESS * roughness;

        // apply fresnel
        float reflectionAmount = reflectionIntensity * fresnel;

        // return hit pixel
        return float4(p.xy, blurAmount + 0.001, reflectionAmount);
    }


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

float4 FragSSR(VaryingsSSR input) : SV_Target
{
    float2 uv = input.texcoord.xy;
    
    float depth = SampleSceneDepth(uv);
    #if UNITY_REVERSED_Z
        depth = 1.0 - depth;
    #endif
    if (depth >= 1.0)
        return float4(0, 0, 0, 0);

    depth = 2.0 * depth - 1.0;
    float3 positionVS = ComputeViewSpacePosition(input.texcoord.zw, depth, unity_CameraInvProjection);
    
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

    float smoothness = gbuffer2.a;

    #if SSR_METALLIC_WORKFLOW //金属度粗糙度计算
        float4 gbuffer1 = SAMPLE_TEXTURE2D_X(_GBuffer1, sampler_PointClamp, uv);

        float metallic = gbuffer1.r;//金属度
        
        metallic = SAMPLE_TEXTURE2D_LOD(_MetallicGradientTex, sampler_LinearClamp, float2(metallic, 0), 0).r;
        if (metallic <= 0) return 0;

        float roughness = SAMPLE_TEXTURE2D_LOD(_SmoothnessGradientTex, sampler_LinearClamp, float2(1.0 - smoothness, 0), 0).r;
        float4 reflection = SSR_Pass(uv, normalVS, positionVS, roughness, metallic);
    #else
        float roughness = 1.0 - max(0, smoothness - REFLECTIONS_THRESHOLD);
        if (roughness >= 1.0) return 0;
        float4 reflection = SSR_Pass(uv, normalVS, positionVS, roughness);
    #endif

    return reflection;
}

//------------------------------------------------------
half4 FragResolve(Varyings input) : SV_Target
{
    float2 uv = input.texcoord.xy;
    half4 reflData = SAMPLE_TEXTURE2D(_RayCastRT, sampler_PointClamp, uv);
    half4 reflection = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, reflData.xy);

    reflection.rgb = min(reflection.rgb, 8.0); // stop NAN pixels
    half vd = dot2((reflData.xy - 0.5) * 2.0);
    half vignette = saturate(VIGNETTE_SIZE - vd * vd);
    vignette = pow(vignette, VIGNETTE_POWER);


    half reflectionIntensity = clamp(reflData.a * REFLECTIONS_MULTIPLIER, REFLECTIONS_MIN_INTENSITY, REFLECTIONS_MAX_INTENSITY);
    

    reflectionIntensity *= vignette;
    reflection.rgb *= reflectionIntensity;

    reflection.rgb = min(reflection.rgb, 1.2); // clamp max brightness

    // conserve energy
    half4 pixel = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    reflection.rgb -= min(0.5, pixel.rgb * reflectionIntensity);

    // keep blur factor in alpha channel
    reflection.a = reflData.z;
    return reflection;
}

//---------------------------------------------------
half4 FragBlur(Varyings input) : SV_Target
{
    float2 uv = input.texcoord.xy;
    // SSR_FRAG_SETUP_GAUSSIAN_UV(input)
    
    SSR_FRAG_SETUP_GAUSSIAN_UV(input)

    half4 c0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
    half4 c1 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset1);
    half4 c2 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset1);
    half4 c3 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset2);
    half4 c4 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - offset2);

    #if SSR_DENOISE
        half l0 = abs(getLuma(c0.rgb));
        half l1 = abs(getLuma(c1.rgb));
        half l2 = abs(getLuma(c2.rgb));
        half l3 = abs(getLuma(c3.rgb));
        half l4 = abs(getLuma(c4.rgb));

        half ml = (l0 + l1 + l2 + l3 + l4) * 0.2;
        c0.rgb *= pow((1.0 + min(ml, l0)) / (1.0 + l0), DENOISE_POWER);
        c1.rgb *= pow((1.0 + min(ml, l1)) / (1.0 + l1), DENOISE_POWER);
        c2.rgb *= pow((1.0 + min(ml, l2)) / (1.0 + l2), DENOISE_POWER);
        c3.rgb *= pow((1.0 + min(ml, l3)) / (1.0 + l3), DENOISE_POWER);
        c4.rgb *= pow((1.0 + min(ml, l4)) / (1.0 + l4), DENOISE_POWER);
    #endif

    half4 blurred = c0 * 0.2270270270 + (c1 + c2) * 0.3162162162 + (c3 + c4) * 0.0702702703;
    return blurred;
}

/////////////////////////////////////////////////////

half4 Combine(Varyings input)
{
    float2 uv = input.texcoord;
    // exclude skybox from blur bleed
    float depth = SampleSceneDepth(uv);
    #if UNITY_REVERSED_Z
        depth = 1.0 - depth;
    #endif
    if (depth >= 1.0) return float4(0, 0, 0, 0);

    half4 mip0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
    half4 mip1 = SAMPLE_TEXTURE2D_X(_BlurRTMip0, sampler_LinearClamp, uv);
    half4 mip2 = SAMPLE_TEXTURE2D_X(_BlurRTMip1, sampler_LinearClamp, uv);
    half4 mip3 = SAMPLE_TEXTURE2D_X(_BlurRTMip2, sampler_LinearClamp, uv);
    half4 mip4 = SAMPLE_TEXTURE2D_X(_BlurRTMip3, sampler_LinearClamp, uv);
    half4 mip5 = SAMPLE_TEXTURE2D_X(_BlurRTMip4, sampler_LinearClamp, uv);

    half r = mip5.a;
    half4 reflData = SAMPLE_TEXTURE2D_X(_RayCastRT, sampler_PointClamp, uv);
    if (reflData.z > 0)
    {
        r = min(reflData.z, r);
    }

    half roughness = clamp(r + _MinimumBlur, 0, 5);

    half w0 = max(0, 1.0 - roughness);
    half w1 = max(0, 1.0 - abs(roughness - 1.0));
    half w2 = max(0, 1.0 - abs(roughness - 2.0));
    half w3 = max(0, 1.0 - abs(roughness - 3.0));
    half w4 = max(0, 1.0 - abs(roughness - 4.0));
    half w5 = max(0, 1.0 - abs(roughness - 5.0));

    half4 refl = mip0 * w0 + mip1 * w1 + mip2 * w2 + mip3 * w3 + mip4 * w4 + mip5 * w5;
    return refl;
}

half4 FragCombine(Varyings input) : SV_Target
{
    return Combine(input);
}

half4 FragCombineWithCompare(Varyings input) : SV_Target
{
    float2 uv = input.texcoord.xy;
    if (uv.x < SEPARATION_POS - _BlitTexture_TexelSize.x * 3)
    {
        return 0;
    }
    else if (uv.x < SEPARATION_POS + _BlitTexture_TexelSize.x * 3)
    {
        return 1.0;
    }
    else
    {
        return Combine(input);
    }
}

half4 FragCopyExact(Varyings input) : SV_Target
{
    float2 uv = input.texcoord.xy;
    half4 pixel = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
    pixel = max(pixel, 0.0);
    return pixel;
}

#endif // SCREEN_SPACE_RAYTRACED_REFLECTION_INCLUDED
