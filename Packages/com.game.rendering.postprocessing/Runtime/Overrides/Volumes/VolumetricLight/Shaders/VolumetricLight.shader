Shader "Hidden/PostProcessing/VolumetricLight"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    #include "VolumetricLightInclude.cs.hlsl"
    #include "VolumetricLightPass.hlsl"

    TEXTURE2D_X(_SourceTex);        float4 _SourceTex_TexelSize;
    TEXTURE2D(_DitherTexture);      SAMPLER(sampler_DitherTexture);

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

            #pragma vertex vertDir
            #pragma fragment Frag

            float4 _FrustumCorners[4];

            struct appData
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint vertexID : SV_VertexID;
            };

            struct vertData
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : VAR_POSITION_WS;
            };

            vertData vertDir(appData input)
            {
                vertData output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                //SV_VertexId doesn't work on OpenGL for some reason -> reconstruct id from uv
                output.positionWS = _FrustumCorners[input.uv.x + input.uv.y * 2];
                return output;
            }


            float4 Frag(vertData input) : SV_Target
            {
                float2 uv = input.uv;
                
                //read depth and reconstruct world position
                float depth = SampleSceneDepth(uv);
                float linear01Depth = Linear01Depth(depth, _ZBufferParams);
                float3 rayStart = _WorldSpaceCameraPos;
                
                return float4(input.positionWS, 1);
                float3 rayDir = input.positionWS - _WorldSpaceCameraPos;
                rayDir *= linear01Depth;

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
