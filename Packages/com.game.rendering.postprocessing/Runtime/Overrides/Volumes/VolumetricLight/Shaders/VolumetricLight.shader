Shader "Hidden/PostProcessing/VolumetricLight"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    #include "VolumetricLightInclude.cs.hlsl"
    

    float4 _BlitTexture_TexelSize;
    TEXTURE2D(_DitherTexture);      SAMPLER(sampler_DitherTexture);
    TEXTURE3D(_NoiseTexture);       SAMPLER(sampler_NoiseTexture);
    

    #include "VolumetricLightPass.hlsl"

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "Volumetric Light"

            HLSLPROGRAM

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _JITTER
            #pragma multi_compile _ _NOISE

            #pragma vertex vertDir
            #pragma fragment Frag

            float3 _FrustumCorners[4];

            struct varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            varyings vertDir(Attributes input)
            {
                varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

                output.positionCS = pos;
                output.texcoord = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                int index = uv.x + uv.y * 2;
                output.positionWS = _FrustumCorners[index];
                return output;
            }

            half4 Frag(varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                // return float4(uv, 0, 1);
                //read depth and reconstruct world position
                float depth = SampleSceneDepth(uv);

                float linear01Depth = Linear01Depth(depth, _ZBufferParams);

                // return depth;
                //从顶点获取世界坐标得方法不对 还是得这样
                float3 positionWS = ComputeWorldSpacePosition(input.positionCS.xy / _ScaledScreenParams, depth, UNITY_MATRIX_I_VP);

                // float3 positionWS = linear01Depth * input.positionWS;
                // float3 positionWS = input.positionWS ;
                // return float4(positionWS, 1);
                
                
                float3 rayStart = _WorldSpaceCameraPos;
                // return float4(input.positionWS, 1);
                
                float3 rayDir = positionWS - _WorldSpaceCameraPos;
                rayDir *= linear01Depth;
                // return half4(rayDir, 1);

                float rayLength = length(rayDir);
                rayDir /= rayLength;
                rayLength = min(rayLength, _MaxRayLength);

                float4 color = RayMarch(input.positionCS.xy, rayStart, rayDir, rayLength);
                if (linear01Depth > 0.9999)
                {
                    color.w = lerp(color.w, 1, _SkyboxExtinction);
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
                float texelSize = _BlitTexture_TexelSize.x * 2.0;
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

                // 9-tap gaussian blur on the downsampled source
                half3 c0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 4.0, 0.0)).rgb;
                half3 c1 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 3.0, 0.0)).rgb;
                half3 c2 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 2.0, 0.0)).rgb;
                half3 c3 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 1.0, 0.0)).rgb;
                half3 c4 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 c5 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 1.0, 0.0)).rgb;
                half3 c6 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 2.0, 0.0)).rgb;
                half3 c7 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 3.0, 0.0)).rgb;
                half3 c8 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 4.0, 0.0)).rgb;

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
                float texelSize = _BlitTexture_TexelSize.y;
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

                // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
                half3 c0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize * 3.23076923)).rgb;
                half3 c1 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize * 1.38461538)).rgb;
                half3 c2 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 c3 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize * 1.38461538)).rgb;
                half3 c4 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize * 3.23076923)).rgb;

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

            TEXTURE2D(_LightTex);

            float4 FragComposite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float4 light = SAMPLE_TEXTURE2D_X(_LightTex, sampler_LinearClamp, uv);
                light.rgb = lerp(light.rgb, 0, light.w);
                return source + light;
            }

            ENDHLSL
        }
    }
}
