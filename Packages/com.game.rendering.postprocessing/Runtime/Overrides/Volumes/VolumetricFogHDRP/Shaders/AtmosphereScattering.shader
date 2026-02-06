Shader "Hidden/VolumeticLighting/AtmosphereScattering"
{

    HLSLINCLUDE
    // Includes
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
    #include "../ShaderLibrary/VolumetricLighting/VolumetricGlobalParams.cs.hlsl"
    #include "../ShaderLibrary/VolumetricLighting/VBuffer.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"


    struct VaryingsScattering
    {
        float4 positionCS : SV_POSITION;
        float4 texCoord0 : INTERP0;
        float4 texCoord1 : INTERP1;
    };

    float4x4 _PixelCoordToViewDirWS;
    TEXTURE3D(_VolumetricLightingBuffer);


    VaryingsScattering vert(Attributes input)
    {
        VaryingsScattering output = (VaryingsScattering)0;
        float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
        float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

        output.positionCS = pos;
        output.texCoord0.xy = uv;

        float3 p = ComputeWorldSpacePosition(output.positionCS, UNITY_MATRIX_I_VP);

        // Encode view direction in texCoord1
        output.texCoord1.xyz = GetWorldSpaceViewDir(p);

        return output;
    }
    ENDHLSL
    
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        ZTest Always ZWrite Off Cull Off Blend Off
        LOD 200

        Pass
        {
            Name "DrawProcedural"


            HLSLPROGRAM
            // Pragmas
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            

            // Returns false when fog is not applied
            bool EvaluateAtmosphericScattering(PositionInputs posInput, float3 V, out float3 color, out float3 opacity)
            {
                color = opacity = 0;

                #ifdef OPAQUE_FOG_PASS
                bool isSky = posInput.deviceDepth == UNITY_RAW_FAR_CLIP_VALUE;
                #else
                bool isSky = false;
                #endif

                // Convert depth to distance along the ray. Doesn't work with tilt shift, etc.
                // When a pixel is at far plane, the world space coordinate reconstruction is not reliable.
                // So in order to have a valid position (for example for height fog) we just consider that the sky is a sphere centered on camera with a radius of 5km (arbitrarily chosen value!)
                float tFrag = isSky ? _MaxFogDistance : posInput.linearDepth * rcp(dot(-V, GetViewForwardDir()));
                
                 float4 volFog = float4(0.0, 0.0, 0.0, 0.0);
                  // if (_EnableVolumetricFog != 0)
                {
                    float4 value = SampleVBuffer(TEXTURE3D_ARGS(_VolumetricLightingBuffer, sampler_LinearClamp),
                                                 posInput.positionNDC,
                                                 tFrag,
                                                 _VBufferViewportSize,
                                                 _VBufferLightingViewportScale.xyz,
                                                 _VBufferLightingViewportLimit.xyz,
                                                 _VBufferDistanceEncodingParams,
                                                 _VBufferDistanceDecodingParams,
                                                 true, false, false);
                    volFog = DelinearizeRGBA(float4(value.rgb, value.a));
                }
                color = volFog.rgb;
                opacity = volFog.aaa;
                
                return true;
            }

            PositionInputs GetPositionInput(VaryingsScattering input, float depth)
            {
                return GetPositionInput(input.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
            }
            
            half4 frag(VaryingsScattering input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 V = normalize(input.texCoord1.xyz);
                float linearDepth = LinearEyeDepth(SampleSceneDepth(input.texCoord0.xy), _ZBufferParams);
                
                //  PositionInputs posInput = GetPositionInput(input, SampleSceneDepth(input.texCoord0.xy));
                //  float3 volColor, volOpacity;
                //     EvaluateAtmosphericScattering(posInput, V, volColor, volOpacity);
                // return half4(volColor,volOpacity.r);
                
                #ifdef OPAQUE_FOG_PASS
                bool isSky = posInput.deviceDepth == UNITY_RAW_FAR_CLIP_VALUE;
                #else
                bool isSky = false;
                #endif

                // Convert depth to distance along the ray. Doesn't work with tilt shift, etc.
                // When a pixel is at far plane, the world space coordinate reconstruction is not reliable.
                // So in order to have a valid position (for example for height fog) we just consider that the sky is a sphere centered on camera with a radius of 5km (arbitrarily chosen value!)
                float tFrag = isSky ? _MaxFogDistance : linearDepth * rcp(dot(-V, GetViewForwardDir()));
                
                float4 volFog = float4(0.0, 0.0, 0.0, 0.0);
                // if (_EnableVolumetricFog != 0)
                {
                    float4 value = SampleVBuffer(TEXTURE3D_ARGS(_VolumetricLightingBuffer, sampler_LinearClamp),
                                                 1 - input.texCoord0.xy,
                                                 tFrag,
                                                 _VBufferViewportSize,
                                                 _VBufferLightingViewportScale.xyz,
                                                 _VBufferLightingViewportLimit.xyz,
                                                 _VBufferDistanceEncodingParams,
                                                 _VBufferDistanceDecodingParams,
                                                 true, false, false);
                    volFog = DelinearizeRGBA(float4(value.rgb, value.a));
                }

                return volFog;
            }
            ENDHLSL
        }
    }
}