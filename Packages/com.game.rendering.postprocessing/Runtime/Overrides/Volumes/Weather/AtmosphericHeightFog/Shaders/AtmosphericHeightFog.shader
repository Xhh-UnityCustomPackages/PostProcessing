Shader "Hidden/PostProcessing/AtmosphericHeightFog"
{

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag


            float _FogIntensity;
            float4 _DistanceParam;
            half3 _FogColorStart, _FogColorEnd;

            float3 _DirectionalDir;
            float4 _DirectionalParam;
            half3 _DirectionalColor;

            float4 _HeightParam;
            float4 _SkyboxParam1, _SkyboxParam2;


            #define FogDistanceStart            _DistanceParam.x
            #define FogDistanceEnd              _DistanceParam.y
            #define FogDistanceFalloff          _DistanceParam.z
            #define FogColorDuo                 _DistanceParam.w

            #define DirectionalIntensity        _DirectionalParam.x
            #define DirectionalFalloff          _DirectionalParam.y

            #define FogAxixOption               float3(0, 1, 0)
            #define FogHeightStart              _HeightParam.x
            #define FogHeightEnd                _HeightParam.y
            #define FogHeightFalloff            _HeightParam.z

            #define SkyboxFogIntensity          _SkyboxParam1.x
            #define SkyboxFogHeight             _SkyboxParam1.y
            #define SkyboxFogFalloff            _SkyboxParam1.z
            #define SkyboxFogOffset             _SkyboxParam1.w
            #define SkyboxFogFill               _SkyboxParam2.x


            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                float depth = SampleSceneDepth(uv);

                float3 positionWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                // return float4(posWS, 1);

                float3 distanceFromCamera = distance(positionWS, _WorldSpaceCameraPos);
                half fogDistanceMask = pow(abs(saturate(((distanceFromCamera - FogDistanceStart) / (FogDistanceEnd - FogDistanceStart)))), FogDistanceFalloff);
                
                // return fogDistanceMask;
                half3 fogDistanceColor = lerp(_FogColorStart.rgb, _FogColorEnd.rgb, (saturate((fogDistanceMask - 0.5)) * FogColorDuo));
                

                float3 viewDirWS = normalize(positionWS - _WorldSpaceCameraPos);
                float VdotL = dot(viewDirWS, _DirectionalDir);
                float directionalMask = pow(abs(((VdotL * 0.5 + 0.5) * DirectionalIntensity)), DirectionalFalloff);
                float3 fogColorResult = lerp(fogDistanceColor, (_DirectionalColor).rgb, directionalMask);

                // return float4(fogColorResult, 1);


                float3 fogAxisOption = positionWS * FogAxixOption;
                half fogHeightMask = pow(abs(saturate((((fogAxisOption.x + fogAxisOption.y + fogAxisOption.z) - FogHeightEnd) / (FogHeightStart - FogHeightEnd)))), FogHeightFalloff);

                // return fogHeightMask;
                // float fogFade = lerp((fogDistanceMask * fogHeightMask), saturate((fogDistanceMask + fogHeightMask)), _FogLayersMode);
                float fogFade = fogDistanceMask * fogHeightMask;
                float fogFadeResult = fogFade;

                //Noise

                //skybox
                float3 skyboxFogAxisOption = viewDirWS * FogAxixOption;
                float skyboxFogFade = max(abs(saturate(((abs(((skyboxFogAxisOption.x + skyboxFogAxisOption.y + skyboxFogAxisOption.z) + - SkyboxFogOffset)) - SkyboxFogHeight) / (0.0 - SkyboxFogHeight)))), 0.0001);
                skyboxFogFade = lerp(pow(skyboxFogFade, SkyboxFogFalloff), 1.0, SkyboxFogFill);
                skyboxFogFade = (skyboxFogFade * SkyboxFogIntensity);

                half skyboxFogMask = (1.0 - ceil(depth));

                fogFadeResult = lerp(fogFadeResult, skyboxFogFade, skyboxFogMask);
                fogFadeResult *= _FogIntensity;

                // return fogFadeResult;

                float3 Color = fogColorResult;
                float Alpha = fogFadeResult;

                float4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                
                Color = lerp(sourceColor, Color, Alpha);
                return half4(Color, 1);
            }
            ENDHLSL
        }
    }
}
