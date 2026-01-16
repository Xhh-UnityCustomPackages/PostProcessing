Shader "Hidden/PostProcessing/ScreenSpaceReflection"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            // 0
            Name "ScreenSpaceReflection LinearSS Test"

            HLSLPROGRAM
            
            #include "ScreenSpaceReflection_Linear.hlsl"
            
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            
            #pragma vertex Vert
            #pragma fragment FragTestLinear
            ENDHLSL
        }

        Pass
        {
            // 1
            Name "ScreenSpaceReflection HiZ Test"

            HLSLPROGRAM

            #include "ScreenSpaceReflection_Hiz.hlsl"

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #pragma vertex Vert
            #pragma fragment FragTestHiZ

            float4 FragTestHiZ(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                uint2 positionSS = (uint2)(input.texcoord * _ScreenSize.xy);
                return ScreenSpaceReflection(positionSS);
            }
            ENDHLSL
        }

        Pass
        {
            // 2
            Name "ScreenSpaceReflection Reprojection"
            HLSLPROGRAM
            #include "ScreenSpaceReflection_Reprojection.hlsl"

            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ SSR_APPROX
            #pragma multi_compile_fragment _ SSR_USE_COLOR_PYRAMID

            #pragma vertex Vert
            #pragma fragment FragSSRReprojection

            half4 FragSSRReprojection(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                uint2 positionSS = (uint2)(input.texcoord * _ScreenSize.xy);
                return ScreenSpaceReflectionReprojection(positionSS);
            }
            ENDHLSL
        }

        Pass
        {
            // 3
            Name "ScreenSpaceReflection Composite"

            HLSLPROGRAM
            #include "ScreenSpaceReflectionInput.hlsl"

            #pragma shader_feature_local _ DEBUG_SCREEN_SPACE_REFLECTION SPLIT_SCREEN_SPACE_REFLECTION

            #pragma vertex Vert
            #pragma fragment FragSSRComposite

            float4 FragSSRComposite(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                float4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                float4 resolve = SAMPLE_TEXTURE2D(_SsrLightingTexture, sampler_LinearClamp, uv);
                #if DEBUG_SCREEN_SPACE_REFLECTION
                return resolve;
                #elif SPLIT_SCREEN_SPACE_REFLECTION
                if (uv.x < SEPARATION_POS - _BlitTexture_TexelSize.x * 1)
                {
                    return sourceColor;
                }
                else if (uv.x < SEPARATION_POS + _BlitTexture_TexelSize.x * 1)
                {
                    return 1.0;
                }
                #endif
                
                return sourceColor + resolve;
            }
            
            ENDHLSL
        }

        Pass
        {
            // 4
            Name "ScreenSpaceReflection Composite"

            HLSLPROGRAM
            #include "ScreenSpaceReflection_Reprojection.hlsl"

            #pragma shader_feature_local _ DEBUG_SCREEN_SPACE_REFLECTION SPLIT_SCREEN_SPACE_REFLECTION

            #pragma vertex Vert
            #pragma fragment FragSSRAccumulation
            ENDHLSL
        }
    }
}
