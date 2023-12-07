Shader "Hidden/PostProcessing/ScreenSpaceCavity"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    #include "ScreenSpaceCavityInput.hlsl"



    #pragma multi_compile_local __ DEBUG_EFFECT DEBUG_NORMALS
    #pragma multi_compile_local __ NORMALS_RECONSTRUCT
    #pragma multi_compile_fragment __ _GBUFFER_NORMALS_OCT
    #pragma multi_compile_local __ SATURATE_CAVITY
    #pragma multi_compile_local __ OUTPUT_TO_TEXTURE
    #pragma multi_compile_local __ UPSCALE_CAVITY
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            // 0
            Name "ScreenSpaceCavity"

            HLSLPROGRAM


            #pragma vertex Vert
            #pragma fragment Frag_Cavity

            void Cavity(float2 uv, float3 normal, out float cavity, out float edges)
            {
                cavity = edges = 0.0;
                float2 p11_22, p13_31;
                float3x3 camProj = GetCoordinateConversionParameters(p11_22, p13_31);

                float depth;
                float3 vpos;
                SampleDepthAndViewpos(uv, p11_22, p13_31, depth, vpos);

                float randAddon = uv.x * 1e-10;
                float rcpSampleCount = rcp(ACTUAL_CAVITY_SAMPLES);

                UNITY_LOOP
                // UNITY_UNROLL
                for (int i = 0; i < int(ACTUAL_CAVITY_SAMPLES); i++)
                {
                    #if defined(SHADER_API_D3D11)
                        i = floor(1.0001 * i);
                    #endif

                    #if 0
                        float3 v_s1 = PickSamplePoint(uv.yx, randAddon, i);
                    #else
                        float3 v_s1 = PickSamplePoint(uv, randAddon, i);
                    #endif
                    v_s1 *= sqrt((i + 1.0) * rcpSampleCount) * _CavityWorldRadius * 0.5;
                    float3 vpos_s1 = vpos + v_s1;

                    float3 spos_s1 = mul(camProj, vpos_s1);
                    #if ORTHOGRAPHIC_PROJECTION
                        float2 uv_s1_01 = clamp((spos_s1.xy + 1.0) * 0.5, 0.0, 1.0);
                    #else
                        float2 uv_s1_01 = clamp((spos_s1.xy * rcp(vpos_s1.z) + 1.0) * 0.5, 0.0, 1.0);
                    #endif

                    float depth_s1 = LinearEyeDepth(SampleSceneDepth(uv_s1_01), _ZBufferParams);
                    float3 vpos_s2 = ReconstructViewPos(uv_s1_01, depth_s1, p11_22, p13_31);

                    float3 dir = vpos_s2 - vpos;
                    float len = length(dir);
                    float f_dot = dot(dir, normal);
                    //float kBeta = 0.002;
                    float kBeta = 0.002 * 4;
                    float f_cavities = f_dot - kBeta * depth;
                    float f_edge = -f_dot - kBeta * depth;
                    float f_bias = 0.05 * len + 0.0001;

                    if (f_cavities > - f_bias)
                    {
                        float attenuation = 1.0 / (len * (1.0 + len * len * 3.));
                        cavity += f_cavities * attenuation;
                    }
                    if (f_edge > f_bias)
                    {
                        float attenuation = 1.0 / (len * (1.0 + len * len * 0.01));
                        edges += f_edge * attenuation;
                    }
                }

                //cavity *= 1.0 / ACTUAL_CAVITY_SAMPLES;
                //edges *= 1.0 / ACTUAL_CAVITY_SAMPLES;
                cavity *= 1.0 * _CavityWorldRadius * 0.5;
                edges *= 1.0 * _CavityWorldRadius * 0.5;

                float kContrast = 0.6;
                cavity = pow(abs(cavity * rcpSampleCount), kContrast);
                edges = pow(abs(edges * rcpSampleCount), kContrast);

                cavity = clamp(cavity * _CavityDarks, 0.0, 1.0);
                edges = edges * _CavityBrights;
            }
            
            float4 Frag_Cavity(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                
                float3 P = FetchViewPos(uv);
                float3 N = FetchViewNormals(P, uv);

                float cavity = 0.0, edges = 0.0;
                Cavity(uv, N, cavity, edges);
                return float4(cavity, edges, SampleSceneDepth(uv), 1.0);
            }


            ENDHLSL
        }


        Pass // 4

        {
            Name "Final"
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_Composite

            float4 Frag_Composite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                
                float3 P = FetchViewPos(uv);
                float3 N = FetchViewNormals(P, uv);

                float4 col = SAMPLE_TEXTURE2D_X(_CavityTex, sampler_PointClamp, uv);;
                float4 untouchedCol = col;
                //float depth01 = FetchRawDepth(uv);
                //if (depth01 == 1.0 || depth01 == 0.0) return col;

                float curvature = 0.0;
                curvature = Curvature(uv, P);
                
                //float cavity = 0.0, edges = 0.0;
                //Cavity(uv, N, cavity, edges);

                float2 cavityTex;
                #if UPSCALE_CAVITY
                    float2 LowResTexelSize = _BlitTexture_TexelSize.xy;
                    float2 LowResBufferSize = _BlitTexture_TexelSize.zw;
                    float2 Corner00UV = floor(uv * LowResBufferSize - .5f) / LowResBufferSize + .5f * LowResTexelSize;
                    float2 BilinearWeights = (uv - Corner00UV) * LowResBufferSize;

                    //xy is signal, z is depth it used
                    float3 TextureValues00 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, Corner00UV).xyz;
                    float3 TextureValues10 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, Corner00UV + float2(LowResTexelSize.x, 0)).xyz;
                    float3 TextureValues01 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, Corner00UV + float2(0, LowResTexelSize.y)).xyz;
                    float3 TextureValues11 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, Corner00UV + LowResTexelSize).xyz;

                    float4 CornerWeights = float4(
                        (1 - BilinearWeights.y) * (1 - BilinearWeights.x),
                        (1 - BilinearWeights.y) * BilinearWeights.x,
                        BilinearWeights.y * (1 - BilinearWeights.x),
                        BilinearWeights.y * BilinearWeights.x);

                    float Epsilon = .0001f/*-.0001f*//*0.0f*/;

                    float4 CornerDepths = abs(float4(TextureValues00.z, TextureValues10.z, TextureValues01.z, TextureValues11.z));
                    float SceneDepth = FetchRawDepth(uv);
                    float4 DepthWeights = 1.0f / (abs(CornerDepths - SceneDepth.xxxx) + Epsilon);
                    float4 FinalWeights = CornerWeights * DepthWeights;

                    cavityTex = (FinalWeights.x * TextureValues00.xy + FinalWeights.y * TextureValues10.xy + FinalWeights.z * TextureValues01.xy + FinalWeights.w * TextureValues11.xy) / dot(FinalWeights, 1);
                #else
                    cavityTex = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).xy;
                #endif
                float cavity = cavityTex.r, edges = cavityTex.g;

                if (uv.x < _Input_TexelSize.x * 2 || uv.y < _Input_TexelSize.y * 2 || 1 - uv.x < _Input_TexelSize.x * 2 || 1 - uv.y < _Input_TexelSize.y * 2)
                {
                    curvature = cavity = edges = 0;
                };

                col.rgb += (curvature * 0.4);

                #if SATURATE_CAVITY
                    //float3 extra = col.rgb - saturate(col.rgb);
                    col.rgb = pow(saturate(col.rgb), 1 - (edges * 0.5));
                    col.rgb = pow(saturate(col.rgb), 1 + (cavity * 1));
                    //col.rgb += extra;
                #else
                    col.rgb += (edges * 0.2);
                    col.rgb -= (cavity * 0.2);
                #endif

                //Uncomment this block of code for on/off back and forth effect preview
                //if (uv.x < sin(_Time.y+(3.1415/2)) * 0.5 + 0.5)
                //{
                //	if (uv.x > (sin(_Time.y+(3.1415/2)) * 0.5 + 0.5) - 0.002) return 0;
                //	return untouchedCol;
                //}


                #if DEBUG_EFFECT
                    return ((1.0 - cavity) * (1.0 + edges) * (1.0 + curvature)) * 0.25;
                #elif DEBUG_NORMALS
                    return float4(N * 0.5 + 0.5, 1);
                #endif

                #if OUTPUT_TO_TEXTURE
                    float r = curvature * 0.4;
                    float g = (edges * 0.2) - (cavity * 0.2);
                    return float4(r * rcp(max(1, P.z * _DistanceFade)) * _EffectIntensity, g * rcp(max(1, P.z * _DistanceFade)) * _EffectIntensity, 1, 1);
                    //Values rescaled so they're more consistent to work with, if you just +curvature+edges it should match 'screen' output
                #else
                    return lerp(untouchedCol, col, rcp(max(1, P.z * _DistanceFade)) * _EffectIntensity);
                #endif
            }

            ENDHLSL
        }
    }
}
