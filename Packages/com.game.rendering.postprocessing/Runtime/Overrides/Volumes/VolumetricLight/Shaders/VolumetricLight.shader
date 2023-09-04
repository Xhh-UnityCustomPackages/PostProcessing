Shader "Hidden/PostProcessing/VolumetricLight"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "VolumetricLightInclude.cs.hlsl"

    TEXTURE2D_X(_SourceTex);        float4 _SourceTex_TexelSize;

    //-----------------------------------------------------------------------------------------
    // GetLightAttenuation
    //-----------------------------------------------------------------------------------------
    float GetShadowAttenuation(float3 posWS)
    {
        float atten = 1;
        #if SHADOWS_DEPTH_ON
            // sample cascade shadow map
            float4 shadowCoord = TransformWorldToShadowCoord(posWS);
            atten = MainLightRealtimeShadow(shadowCoord);
        #endif
        return atten;
    }

    //-----------------------------------------------------------------------------------------
    // MieScattering
    //-----------------------------------------------------------------------------------------
    #define MieScattering(cosAngle, g) g.w * (g.x / (pow(g.y - g.z * cosAngle, 1.5)))
    #define random(seed) sin(seed * float2(641.5467987313875, 3154.135764) + float2(1.943856175, 631.543147))
    #define highQualityRandom(seed) cos(sin(seed * float2(641.5467987313875, 3154.135764) + float2(1.943856175, 631.543147)) * float2(4635.4668457, 84796.1653) + float2(6485.15686, 1456.3574563))

    float4 ScatterStep(float3 accumulatedLight, float accumulatedTransmittance, float sliceLight, float sliceDensity)
    {
        sliceDensity = max(sliceDensity, 0.000001);
        float sliceTransmittance = exp(-sliceDensity / _SampleCount);

        // Seb Hillaire's improved transmission by calculating an integral over slice depth instead of
        // constant per slice value. Light still constant per slice, but that's acceptable. See slide 28 of
        // Physically-based & Unified Volumetric Rendering in Frostbite
        // http://www.frostbite.com/2015/08/physically-based-unified-volumetric-rendering-in-frostbite/
        float3 sliceLightIntegral = sliceLight * (1.0 - sliceTransmittance) / sliceDensity;

        accumulatedLight += sliceLightIntegral * accumulatedTransmittance;
        accumulatedTransmittance *= sliceTransmittance;
        
        return float4(accumulatedLight, accumulatedTransmittance);
    }

    float4 RayMarch(float2 screenPos, float3 rayStart, float3 final, float3 rayDir, float rate, Light light)
    {
        float4 vlight = float4(0, 0, 0, 1);
        rate *= _Density;

        float cosAngle = dot(-light.direction, -rayDir);

        float3 step = 1.0 / _SampleCount;
        step.yz *= float2(0.25, 0.2);
        float2 seed = random((_ScreenParams.y * screenPos.y + screenPos.x) * _ScreenParams.x + _RandomNumber);
        [loop]
        for (float i = step.x; i < 1; i += step.x)
        {
            seed = random(seed);
            float lerpValue = i + seed.y * step.y + seed.x * step.z;
            float3 currentPosition = lerp(rayStart, final, lerpValue);
            float atten = GetShadowAttenuation(currentPosition) * _Intensity ;

            atten *= MieScattering(cosAngle, _MieG) ;
            vlight = ScatterStep(vlight.rgb, vlight.a, atten, rate);
        }

        vlight.rgb *= light.color ;
        vlight.a = saturate(vlight.a);

        return vlight;
    }


    //Blur

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Off ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "Volumetric Light"

            HLSLPROGRAM

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            // -------------------------------------
            // Material keywords
            #pragma multi_compile_fragment  SHADOWS_DEPTH_ON

            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                
                float2 randomOffset = highQualityRandom((_ScreenParams.y * uv.y + uv.x) * _ScreenParams.x + _RandomNumber) * _JitterOffset;
                uv += randomOffset;

                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    //  调整 Z 以匹配 OpenGL 的 NDC ([-1, 1])
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                
                float3 posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                

                float3 rayDir = posWS.xyz - _WorldSpaceCameraPos;
                float rayLength = length(rayDir);
                rayDir /= rayLength;
                //总射线长度
                rayLength = min(rayLength, _MaxRayLength);

                //最终步进的世界坐标
                float3 final = _WorldSpaceCameraPos + rayDir * rayLength;

                Light mainLight = GetMainLight();

                float4 color = RayMarch(uv, _WorldSpaceCameraPos, final, rayDir, rayLength / _MaxRayLength, mainLight);
                
                //天空盒部分
                if (Linear01Depth(depth, _ZBufferParams) > 0.9999)
                {
                    color.rgb *= 0;
                }
                
                return color;
            }
            
            ENDHLSL
        }

        Pass
        {
            Name "Volumetric Light BlurH"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragBlurH

            half4 FragBlurH(Varyings input) : SV_Target
            {
                float texelSize = _SourceTex_TexelSize.x * 2.0;
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

                // 9-tap gaussian blur on the downsampled source
                half3 c0 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 4.0, 0.0));
                half3 c1 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 3.0, 0.0));
                half3 c2 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 2.0, 0.0));
                half3 c3 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 1.0, 0.0));
                half3 c4 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
                half3 c5 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 1.0, 0.0));
                half3 c6 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 2.0, 0.0));
                half3 c7 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 3.0, 0.0));
                half3 c8 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 4.0, 0.0));

                half3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
                + c4 * 0.22702703
                + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

                return half4(color, 1);
            }

            ENDHLSL
        }

        Pass
        {
            Name "Volumetric Light BlurV"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragBlurV

            float4 FragBlurV(Varyings input) : SV_Target
            {
                float texelSize = _SourceTex_TexelSize.y;
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

                // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
                half3 c0 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 3.23076923));
                half3 c1 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 1.38461538));
                half3 c2 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
                half3 c3 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 1.38461538));
                half3 c4 = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 3.23076923));

                half3 color = c0 * 0.07027027 + c1 * 0.31621622
                + c2 * 0.22702703
                + c3 * 0.31621622 + c4 * 0.07027027;

                return half4(color, 1);
            }

            ENDHLSL
        }

        Pass
        {
            Name "Volumetric Light Composite"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragComposite

            TEXTURE2D_X(_LightTex);

            float4 FragComposite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 source = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
                float4 light = SAMPLE_TEXTURE2D_X(_LightTex, sampler_LinearClamp, uv);
                return source + light;
            }

            ENDHLSL
        }
    }
}
