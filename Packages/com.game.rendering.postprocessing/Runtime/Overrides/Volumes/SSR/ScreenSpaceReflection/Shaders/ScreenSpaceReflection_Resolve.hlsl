#ifndef SCREEN_SPACE_REFLECTION_INCLUDED
#define SCREEN_SPACE_REFLECTION_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/DeclareMotionVectorTexture.hlsl"


float4 FragResolve(Varyings input) : SV_Target
{
    float4 test = SAMPLE_TEXTURE2D(_SSR_TestTex, sampler_PointClamp, input.texcoord);
    
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

float4 FragComposite2(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    float4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    float depth = SampleSceneDepth(uv);
    UNITY_BRANCH
    if (Linear01Depth(depth, _ZBufferParams) > 0.999)
        return sourceColor;

    float4 resolve = SAMPLE_TEXTURE2D(_SSR_ResolveTex, sampler_LinearClamp, uv);
    return sourceColor + resolve;
}

float4 FragComposite(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;
    // 由于没有Gbuffer之前的predepth阶段 只能依靠存储两个阶段的深度 过滤出Gbuffer中没有的物体 才能混合SSR
    // TODO 模糊阶段会把Gbuffer后的像素混进去 导致边缘会有溢出 可能要考虑提前mask
    // float preDepth = SAMPLE_TEXTURE2D(_MaskDepthRT, sampler_MaskDepthRT, uv).r;
    float depth = SampleSceneDepth(uv);

    //SSR 遮罩
    float mask = 1;//1 - (depth > preDepth);

    float4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    
    UNITY_BRANCH
    if (Linear01Depth(depth, _ZBufferParams) > 0.999)
        return sourceColor;

    half4 gbuffer1 = SAMPLE_TEXTURE2D_LOD(_GBuffer1, sampler_PointClamp, uv, 0);
    half4 gbuffer2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, uv, 0);
    
    half3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float3 positionVS = GetViewSpacePosition(depth, uv);
    float3 viewDirectionWS = -mul((float3x3)_InverseViewMatrixSSR, normalize(positionVS));
    
    
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    float confidence = saturate(2.0 * dot(-viewDirectionWS, normalize(reflectVector)));
    
    half smoothness = gbuffer2.a;
    half occlusion = gbuffer1.a;
    
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
    float opacity = PerceptualRoughnessFade(perceptualRoughness, _SsrRoughnessFadeRcpLength, _SsrRoughnessFadeEndTimesRcpLength);
    
    float mipLevel = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    float4 resolve = SAMPLE_TEXTURE2D_LOD(_SSR_ResolveTex, sampler_LinearClamp, uv, mipLevel);
   
    float fade = resolve.a;

    // fade是低频部分 理论上相当于一个锐化操作
    fade = (1.0 - saturate(fade * smoothstep(0.5, 1.0, fade) * _DistanceFade)) * confidence * opacity;
    // fade = (1.0 - _DistanceFade * resolve.a) * confidence * smoothness;
    
    half3 indirectSpecular = resolve.rgb * fade;
    
    half3 indirectSpecularSSR = indirectSpecular;
    indirectSpecularSSR *= occlusion * mask *  _SSRIntensity;
    
    // 自定义环境光强度(这里就是SSR强度), 是CustomLit传进来的
    float envCustomIntensity = 1;
    // UNITY_BRANCH
    // if (IsMaterialFlagIDCustomEnvSpecIntensity(materialFlags))
    //     envCustomIntensity = gbuffer1.b * 10;

    indirectSpecularSSR *= envCustomIntensity;
    
    #if DEBUG_SCREEN_SPACE_REFLECTION
    return half4(indirectSpecularSSR, 1);
    #endif

    sourceColor.rgb += indirectSpecularSSR;

    return sourceColor;
}

#endif // SCREEN_SPACE_REFLECTION_INCLUDED
