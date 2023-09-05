Shader "Hidden/PostProcessing/VolumetricLight"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    #include "VolumetricLightInclude.cs.hlsl"
    #include "VolumetricLightPass.hlsl"

    float4 _BlitTexture_TexelSize;
    TEXTURE2D(_DitherTexture);      SAMPLER(sampler_DitherTexture);

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

            #pragma vertex vertDir
            #pragma fragment Frag

            float4 _FrustumCorners[4];

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
                output.positionWS = _FrustumCorners[output.texcoord.x + output.texcoord.y * 2];
                return output;
            }


            // 重建世界坐标
            float3 GetWorldPosition(float2 uv, float3 viewVec, out float depth, out float linearDepth)
            {
                depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;//采样深度图
                depth = Linear01Depth(depth, _ZBufferParams); //转换为线性深度
                linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                float3 viewPos = viewVec * depth; //获取实际的观察空间坐标（插值后）
                float3 worldPos = mul(unity_CameraToWorld, float4(viewPos, 1)).xyz; //观察空间-->世界空间坐标
                return worldPos;
            }

            half4 Frag(varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                //read depth and reconstruct world position
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
                //从顶点获取世界坐标得方法不对 还是得这样
                float3 positionWS = ComputeWorldSpacePosition(input.positionCS.xy / _ScaledScreenParams, depth, UNITY_MATRIX_I_VP);

                
                float linear01Depth = Linear01Depth(depth, _ZBufferParams);
                float3 rayStart = _WorldSpaceCameraPos;
                // return float4(input.positionWS, 1);
                
                float3 rayDir = positionWS + _WorldSpaceCameraPos;
                rayDir *= linear01Depth;
                // return half4(rayDir, 1);

                float rayLength = length(rayDir);
                rayDir /= rayLength;
                rayLength = min(rayLength, _MaxRayLength);

                float4 color = RayMarch(input.positionCS.xy, rayStart, rayDir, rayLength);
                if (linear01Depth > 0.999999)
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
                return source + light;
            }

            ENDHLSL
        }
    }
}
