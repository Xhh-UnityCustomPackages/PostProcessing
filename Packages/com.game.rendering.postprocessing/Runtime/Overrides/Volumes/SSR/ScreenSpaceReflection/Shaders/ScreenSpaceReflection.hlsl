#ifndef SCREEN_SPACE_REFLECTION_INCLUDED
#define SCREEN_SPACE_REFLECTION_INCLUDED

#include "ScreenSpaceReflectionInput.hlsl"
#include "Packages/com.game.rendering.postprocessing/ShaderLibrary/DeclareMotionVectorTexture.hlsl"

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

//
// Fragment shaders
//
float4 FragReproject(Varyings input) : SV_Target
{
    float2 uv = input.texcoord;

    // 没有使用jitter 不考虑sceneview 依赖MotionVector
    half2 motionVector = SampleMotionVector(uv);
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

    // GlobalIllumination
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
   
    
    float4 resolve = SAMPLE_TEXTURE2D(_SSR_ResolveTex, sampler_LinearClamp, uv);
    float confidence = saturate(2.0 * dot(-viewDirectionWS, normalize(reflectVector)));
    
    float distanceFade = _DistanceFade;
    
    half smoothness = gbuffer2.a;
    half occlusion = gbuffer1.a;
    // https://www.desmos.com/calculator/k3hodgy8ry TODO 这个结果是个比较奇怪的曲线
    float fade = resolve.a * resolve.a * 3;

    // fade是低频部分 理论上相当于一个锐化操作
    fade = (1.0 - saturate(fade * smoothstep(0.5, 1.0, fade) * distanceFade)) * confidence * smoothness;
    // fade = (1.0 - distanceFade * resolve.a) * confidence * smoothness;
    
    half3 indirectSpecular = resolve.rgb * fade;
    
    half3 indirectSpecularSSR = indirectSpecular;
    indirectSpecularSSR *= occlusion * mask *  _Intensity;
    
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
