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


                // int index = uv.x + uv.y * 2;
                // output.positionWS = _FrustumCorners[index];


                float3 ndcPos = float3(output.texcoord.xy * 2.0 - 1.0, 1); //直接把uv映射到ndc坐标
                float far = _ProjectionParams.z; //获取投影信息的z值，代表远平面距离
                float3 clipVec = float3(ndcPos.x, ndcPos.y, ndcPos.z * - 1) * far; //裁切空间下的视锥顶点坐标
                output.positionWS = mul(unity_CameraInvProjection, clipVec.xyzz).xyz;

                return output;
            }


            float3 GetWorldPosition(float2 uv, float linear01Depth, float3 viewVec)
            {
                // float depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;//采样深度图
                // depth = Linear01Depth(depth, _ZBufferParams); //转换为线性深度
                float3 viewPos = viewVec * linear01Depth; //获取实际的观察空间坐标（插值后）
                float3 worldPos = mul(unity_CameraToWorld, float4(viewPos, 1)).xyz; //观察空间-->世界空间坐标
                return worldPos;
            }

            half4 Frag(varyings input) : SV_Target
            {
                float2 uv = input.texcoord;


                // float2 channel = floor(input.positionCS);
                // //棋盘格刷新
                // clip(channel.y % 2 * channel.x % 2 + (channel.y + 1) % 2 * (channel.x + 1) % 2 - 0.1f);

                // return float4(uv, 0, 1);
                //read depth and reconstruct world position
                float depth = SampleSceneDepth(uv);

                float linear01Depth = Linear01Depth(depth, _ZBufferParams);

                // return linear01Depth;
                //从顶点获取世界坐标得方法不对 还是得这样
                // float3 positionWS = ComputeWorldSpacePosition(input.positionCS.xy / _ScaledScreenParams, depth, UNITY_MATRIX_I_VP);

                // float3 positionWS = linear01Depth * input.positionWS;
                float3 positionWS = GetWorldPosition(uv, linear01Depth, input.positionWS);
                // return 1;
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
                // light.rgb = lerp(light.rgb, 0, light.w);
                return source + light;
            }

            ENDHLSL
        }
    }
}
