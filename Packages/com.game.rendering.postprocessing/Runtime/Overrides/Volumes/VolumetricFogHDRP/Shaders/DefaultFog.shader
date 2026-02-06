Shader "Hidden/VolumeticLighting/DefaultFog"
{
    Properties 
    {
       [NoScaleOffset]_Mask("Mask", 3D) = "white" {}
        _ScrollSpeed("Scroll Speed", Vector) = (0, 0, 0, 0)
        _Tiling("Tiling", Vector) = (0, 0, 0, 0)
        _AlphaOnlyTexture("Alpha Only Texture", Float) = 0
        [HideInInspector][Enum(UnityEngine.Rendering.Universal.Extensions.HotFix.LocalVolumetricFogBlendingMode)]_FogVolumeBlendMode("Float", Float) = 1
        [HideInInspector][Enum(UnityEngine.Rendering.BlendMode)]_FogVolumeDstColorBlend("Float", Float) = 1
        [HideInInspector][Enum(UnityEngine.Rendering.BlendMode)]_FogVolumeSrcColorBlend("Float", Float) = 1
        [HideInInspector][Enum(UnityEngine.Rendering.BlendMode)]_FogVolumeDstAlphaBlend("Float", Float) = 1
        [HideInInspector][Enum(UnityEngine.Rendering.BlendMode)]_FogVolumeSrcAlphaBlend("Float", Float) = 1
        [HideInInspector][Enum(UnityEngine.Rendering.BlendOp)]_FogVolumeColorBlendOp("Float", Float) = 0
        [HideInInspector][Enum(UnityEngine.Rendering.BlendOp)]_FogVolumeAlphaBlendOp("Float", Float) = 0
        [HideInInspector]_FogVolumeSingleScatteringAlbedo("Color", Color) = (1, 1, 1, 1)
        [HideInInspector]_FogVolumeFogDistanceProperty("Float", Float) = 10
        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "FogVolumeVoxelize"
            Tags
            {
                "LightMode" = "FogVolumeVoxelize"
            }

            Cull Off
            Blend [_FogVolumeSrcColorBlend] [_FogVolumeDstColorBlend], [_FogVolumeSrcAlphaBlend] [_FogVolumeDstAlphaBlend]
            BlendOp [_FogVolumeColorBlendOp], [_FogVolumeAlphaBlendOp]
            ZTest Off
            ZWrite Off


            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #pragma multi_compile_fragment _ _ENABLE_VOLUMETRIC_FOG_MASK

            #if defined(_ENABLE_VOLUMETRIC_FOG_MASK)
            #define KEYWORD_PERMUTATION_0
            #else
            #define KEYWORD_PERMUTATION_1
            #endif

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            //#include "Packages/com.unity.render-pipelines.universal/Runtime/Extensions/ShaderLibrary/VolumetricLighting/GeometricTools.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
            #include "../ShaderLibrary/VolumetricLighting/VolumetricGlobalParams.cs.hlsl"
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl" 
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "../ShaderLibrary/VolumetricLighting/VolumetricLighting.cs.hlsl"
            #include "../ShaderLibrary/VolumetricLighting/VolumetricMaterialUtils.hlsl"

            #define SHADERPASS SHADERPASS_FOGVOLUME_VOXELIZATION
            #define FOG_VOLUME_BLENDING_ADDITIVE 1
            #define SUPPORT_GLOBAL_MIP_BIAS 1

            CBUFFER_START(UnityPerMaterial)
                float3 _ScrollSpeed;
                float3 _Tiling;
                float _AlphaOnlyTexture;
                float _FogVolumeBlendMode;
                float4 _FogVolumeSingleScatteringAlbedo;
                float _FogVolumeFogDistanceProperty;
            CBUFFER_END

            TEXTURE3D(_Mask);
            SAMPLER(sampler_Mask);

            uint _VolumetricFogGlobalIndex;
            StructuredBuffer<VolumetricMaterialRenderingData> _VolumetricMaterialData;
            ByteAddressBuffer _VolumetricGlobalIndirectionBuffer;

            struct a2v
            {
                float4 positionOS: POSITION;
                float3 normalOS: TANGENT;
                half4 vertexColor: COLOR;
                float4 uv : TEXCOORD0;
            };

            struct VertexToFragment
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirectionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                nointerpolation float viewIndex : TEXCOORD2;
                nointerpolation uint depthSlice : SV_RenderTargetArrayIndex;
            };

            float3 GetCubeVertexPosition(uint vertexIndex)
            {
                int index = _VolumetricGlobalIndirectionBuffer.Load(_VolumetricFogGlobalIndex << 2);
                return _VolumetricMaterialData[index].obbVertexPositionWS[vertexIndex].xyz;
            }

            // VertexCubeSlicing needs GetCubeVertexPosition to be declared before
            #define GET_CUBE_VERTEX_POSITION GetCubeVertexPosition
            #include "../ShaderLibrary/VolumetricLighting/VertexCubeSlicing.hlsl"

            VertexToFragment Vert(uint instanceId : INSTANCEID_SEMANTIC, uint vertexId : VERTEXID_SEMANTIC)
            {
                VertexToFragment output;
                int materialDataIndex = _VolumetricGlobalIndirectionBuffer.Load(_VolumetricFogGlobalIndex << 2);


                uint sliceCount = _VolumetricMaterialData[materialDataIndex].sliceCount;
                uint viewIndex = instanceId / sliceCount;
                // In VR sliceCount needs to be the same for each eye to be able to retrieve correctly the view index
                // Patch the mater data index to read the correct view index dependent data
                materialDataIndex += viewIndex * _VolumeCount;

                uint sliceStartIndex = _VolumetricMaterialData[materialDataIndex].startSliceIndex;

                #if defined(UNITY_STEREO_INSTANCING_ENABLED)
                unity_StereoEyeIndex = viewIndex;
                #endif
                output.viewIndex = viewIndex;

                uint sliceIndex = sliceStartIndex + (instanceId % sliceCount);
                output.depthSlice = sliceIndex + viewIndex * _VBufferSliceCount;

                float sliceDepth = VBufferDistanceToSliceIndex(sliceIndex);
                #if USE_VERTEX_CUBE_SLICING

                float3 cameraForward = -UNITY_MATRIX_V[2].xyz;
                float3 sliceCubeVertexPosition = ComputeCubeSliceVertexPositionRWS(cameraForward, sliceDepth, vertexId);
                output.positionCS = TransformWorldToHClip(float4(sliceCubeVertexPosition, 1.0));
                output.viewDirectionWS = GetWorldSpaceViewDir(sliceCubeVertexPosition);
                output.positionOS = mul(UNITY_MATRIX_I_M, sliceCubeVertexPosition);

                #else

                output.positionCS = GetQuadVertexPosition(vertexId);
                output.positionCS.xy = output.positionCS.xy * _VolumetricMaterialData[materialDataIndex].viewSpaceBounds.zw + _VolumetricMaterialData[materialDataIndex].viewSpaceBounds.xy;
                output.positionCS.z = EyeDepthToLinear(sliceDepth, _ZBufferParams);
                output.positionCS.w = 1;

                float3 positionWS = ComputeWorldSpacePosition(output.positionCS, _IsObliqueProjectionMatrix ? _CameraInverseViewProjection_NO : UNITY_MATRIX_I_VP);
                output.viewDirectionWS = GetWorldSpaceViewDir(positionWS);

                // Calculate object space position
                output.positionOS = mul(UNITY_MATRIX_I_M, float4(positionWS, 1)).xyz;

                #endif // USE_VERTEX_CUBE_SLICING

                //float3 posOffset = mul(UNITY_MATRIX_M , output.positionOS.xyz);

                output.positionCS.xy = -output.positionCS.xy;
                output.positionCS.z -= output.positionCS.z;
                return output;
            }

            float ComputeFadeFactor(float3 coordNDC, float distance)
            {
                bool exponential = uint(_VolumetricMaterialFalloffMode) == LOCALVOLUMETRICFOGFALLOFFMODE_EXPONENTIAL;
                bool multiplyBlendMode = _FogVolumeBlendMode == LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY;

                return ComputeVolumeFadeFactor(
                    coordNDC, distance,
                    _VolumetricMaterialRcpPosFaceFade.xyz,
                    _VolumetricMaterialRcpNegFaceFade.xyz,
                    _VolumetricMaterialInvertFade,
                    _VolumetricMaterialRcpDistFadeLen,
                    _VolumetricMaterialEndTimesRcpDistFadeLen,
                    exponential,
                    multiplyBlendMode
                );
            }

            void Frag(VertexToFragment v2f, out float4 outColor : SV_Target0)
            {
                float3 albedo = 1;
                float extinction = 1;

                float sliceDistance = VBufferDistanceToSliceIndex(v2f.depthSlice % _VBufferSliceCount);

                // Compute voxel center position and test against volume OBB
                float3 raycenterDirWS = normalize(-v2f.viewDirectionWS); // Normalize
                float3 rayoriginWS = GetCurrentViewPosition();
                float3 voxelCenterWS = rayoriginWS + sliceDistance * raycenterDirWS;

                float3x3 obbFrame = float3x3(_VolumetricMaterialObbRight.xyz, _VolumetricMaterialObbUp.xyz, cross(_VolumetricMaterialObbRight.xyz, _VolumetricMaterialObbUp.xyz));

                float3 voxelCenterBS = mul(voxelCenterWS - _VolumetricMaterialObbCenter.xyz + _WorldSpaceCameraPos.xyz, transpose(obbFrame));
                float3 voxelCenterCS = (voxelCenterBS * rcp(_VolumetricMaterialObbExtents.xyz));

                bool overlap = Max3(abs(voxelCenterCS.x), abs(voxelCenterCS.y), abs(voxelCenterCS.z)) <= 1;
                if (!overlap)
                    clip(-1);

                #ifdef _ENABLE_VOLUMETRIC_FOG_MASK
                float3 maskUV = saturate(voxelCenterCS * 0.5 + 0.5);
                maskUV = (_ScrollSpeed * _Time.g) + (maskUV * _Tiling);
                half4 mask = SAMPLE_TEXTURE3D(_Mask, sampler_Mask, maskUV);
                half4 maskCol = lerp(mask, half4(1, 1, 1, mask.a), _AlphaOnlyTexture);
                albedo = maskCol.rgb;
                extinction = maskCol.a;
                #endif

                extinction *= ExtinctionFromMeanFreePath(_FogVolumeFogDistanceProperty);
                albedo *= _FogVolumeSingleScatteringAlbedo.rgb;
                float3 voxelCenterNDC = voxelCenterCS * 0.5 + 0.5;
                // voxelCenterNDC.xy = 1 - voxelCenterNDC.xy;
                //voxelCenterNDC.x = 1 - voxelCenterNDC.x;
                float fade = ComputeFadeFactor(voxelCenterNDC, sliceDistance);
                //fade = 1;
                // When multiplying fog, we need to handle specifically the blend area to avoid creating gaps in the fog
                if (_FogVolumeBlendMode == LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY)
                {
                    outColor = max(0, lerp(float4(1.0, 1.0, 1.0, 1.0), float4(albedo * extinction, extinction), fade.xxxx));
                }
                else
                {
                    extinction *= fade;
                    outColor = max(0, float4(saturate(albedo * extinction), extinction));
                }
                //outColor = 1;
            }
            ENDHLSL
        }
    }
}