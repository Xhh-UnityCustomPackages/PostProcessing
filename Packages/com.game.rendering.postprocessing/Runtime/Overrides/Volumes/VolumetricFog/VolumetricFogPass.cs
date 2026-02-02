using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [PostProcess("Volumetric Fog", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public partial class VolumetricFogPass : PostProcessVolumeRenderer<VolumetricFog>
    {
        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth;

        	
        private const string DownsampledCameraDepthRTName = "_DownsampledCameraDepth";
        private const string VolumetricFogRenderRTName = "_VolumetricFog";
        private const string VolumetricFogBlurRTName = "_VolumetricFogBlur";
        private const string VolumetricFogUpsampleCompositionRTName = "_VolumetricFogUpsampleComposition";

        private static readonly float[] Anisotropies = new float[UniversalRenderPipeline.maxVisibleAdditionalLights];
        private static readonly float[] Scatterings = new float[UniversalRenderPipeline.maxVisibleAdditionalLights];
        private static readonly float[] RadiiSq = new float[UniversalRenderPipeline.maxVisibleAdditionalLights];
		
        private int _downsampleDepthPassIndex;
        private int _volumetricFogRenderPassIndex;
        private int _volumetricFogHorizontalBlurPassIndex;
        private int _volumetricFogVerticalBlurPassIndex;
        private int _volumetricFogDepthAwareUpsampleCompositionPassIndex;
		
        private readonly Material _downsampleDepthMaterial;
        private readonly Material _volumetricFogMaterial;
		
        private readonly ComputeShader _volumetricFogRaymarchCS;
        private readonly int _volumetricFogRaymarchKernel;

        private readonly ComputeShader _volumetricFogBlurCS;
        private readonly int _volumetricFogBlurKernel;
		
        private readonly ComputeShader _bilateralUpsampleCS;
        private readonly int _bilateralUpSampleColorKernel;

        private RTHandle _downsampledCameraDepthRTHandle;
        private RTHandle _volumetricFogRenderRTHandle;
        private RTHandle _volumetricFogBlurRTHandle;
        private RTHandle _volumetricFogUpsampleCompositionRTHandle;
		
        private bool _upsampleInCS;
        private bool _raymarchInCS;
        private bool _blurInCS;

        private int _rtWidth;
        private int _rtHeight;
		
        private ShaderVariablesBilateralUpsample _shaderVariablesBilateralUpsampleCB;
        
        private readonly ProfilingSampler m_BlurSampler = new("Volumetric Fog Blur");
        public VolumetricFogPass()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogResources>();
            
            _bilateralUpsampleCS = runtimeShaders.volumetricFogUpsampleCS;
            _bilateralUpSampleColorKernel = _bilateralUpsampleCS.FindKernel("VolumetricFogBilateralUpSample");
            _volumetricFogRaymarchCS = runtimeShaders.volumetricFogRaymarchCS;
            _volumetricFogRaymarchKernel = _volumetricFogRaymarchCS.FindKernel("VolumetricFogRaymarch");
            _volumetricFogBlurCS = runtimeShaders.volumetricFogBlurCS;
            _volumetricFogBlurKernel = _volumetricFogBlurCS.FindKernel("VolumetricFogBlur");
            
            var pipelineRuntimeShaders = GraphicsSettings.GetRenderPipelineSettings<PostProcessFeatureRuntimeResources>();
            _downsampleDepthMaterial = CoreUtils.CreateEngineMaterial(pipelineRuntimeShaders.downsampleDepthPS);
            _volumetricFogMaterial = CoreUtils.CreateEngineMaterial(runtimeShaders.volumetricFogPS);
            InitializePassesIndices();
        }
        
        private void InitializePassesIndices()
        {
            _downsampleDepthPassIndex = _downsampleDepthMaterial.FindPass("DownsampleDepth");
            _volumetricFogRenderPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogRender");
            _volumetricFogHorizontalBlurPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogHorizontalBlur");
            _volumetricFogVerticalBlurPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogVerticalBlur");
            _volumetricFogDepthAwareUpsampleCompositionPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogDepthAwareUpsampleComposition");
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_downsampleDepthMaterial);
            _downsampledCameraDepthRTHandle?.Release();
            CoreUtils.Destroy(_volumetricFogMaterial);
            _volumetricFogRenderRTHandle?.Release();
            _volumetricFogBlurRTHandle?.Release();
            _volumetricFogUpsampleCompositionRTHandle?.Release();
        }
        
        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
	        var cameraData = renderingData.cameraData;
	        // Prepare data
	        PrepareVolumetricFogData(cameraData.cameraTargetDescriptor);
            
            CheckRTHandles(cameraData.cameraTargetDescriptor);

            // 1. Downsample Depth
            RenderDownsampleDepth(cmd);

            // 2. Raymarch
            RTHandle fogHandle = RenderRaymarch(cmd, ref renderingData);

            // 3. Blur
            using (new ProfilingScope(cmd, m_BlurSampler))
            {
                fogHandle = RenderBlur(cmd, fogHandle);
            }

            // 4. Upsample and Composite
            RenderUpsample(cmd, fogHandle, source, destination);
        }

        private void CheckRTHandles(RenderTextureDescriptor descriptor)
        {
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 0;
            
            int width = descriptor.width;
            int height = descriptor.height;
            int halfWidth = width / 2;
            int halfHeight = height / 2;
            
            descriptor.width = halfWidth;
            descriptor.height = halfHeight;
            descriptor.enableRandomWrite = false;
            descriptor.colorFormat = RenderTextureFormat.RFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _downsampledCameraDepthRTHandle, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: DownsampledCameraDepthRTName);
            
            descriptor.enableRandomWrite = true;
            descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _volumetricFogRenderRTHandle, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: VolumetricFogRenderRTName);
            RenderingUtils.ReAllocateHandleIfNeeded(ref _volumetricFogBlurRTHandle, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: VolumetricFogBlurRTName);
            
            descriptor.width = width;
            descriptor.height = height;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _volumetricFogUpsampleCompositionRTHandle, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: VolumetricFogUpsampleCompositionRTName);
        }

        private void RenderDownsampleDepth(CommandBuffer cmd)
        {
            cmd.Blit(null, _downsampledCameraDepthRTHandle, _downsampleDepthMaterial, _downsampleDepthPassIndex);
        }

        private RTHandle RenderRaymarch(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var lightData = renderingData.lightData;
            
            if (_raymarchInCS)
            {
                UpdateVolumetricFogComputeShaderParameters(cmd, _volumetricFogRaymarchCS, postProcessData, lightData.mainLightIndex, lightData.additionalLightsCount, lightData.visibleLights);

                cmd.SetComputeTextureParam(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel, ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);
                cmd.SetComputeTextureParam(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel, Shader.PropertyToID("_ExposureTexture"), postProcessData.GetExposureTexture());
                cmd.SetComputeTextureParam(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel, ShaderIDs._VolumetricFogOutput, _volumetricFogRenderRTHandle);

                int groupsX = PostProcessingUtils.DivRoundUp(_rtWidth / 2, 8);
                int groupsY = PostProcessingUtils.DivRoundUp(_rtHeight / 2, 8);
                cmd.DispatchCompute(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel, groupsX, groupsY, 1);
                
                return _volumetricFogRenderRTHandle;
            }
            else
            {
                UpdateVolumetricFogMaterialParameters(_volumetricFogMaterial, lightData.mainLightIndex, lightData.additionalLightsCount, lightData.visibleLights);
                _volumetricFogMaterial.SetTexture(ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);

                Blitter.BlitCameraTexture(cmd, _downsampledCameraDepthRTHandle, _volumetricFogRenderRTHandle, _volumetricFogMaterial, _volumetricFogRenderPassIndex);
                
                return _volumetricFogRenderRTHandle;
            }
        }

        private RTHandle RenderBlur(CommandBuffer cmd, RTHandle input)
        {
            if (_blurInCS)
            {
                int halfWidth = _rtWidth / 2;
                int halfHeight = _rtHeight / 2;
                Vector2 texelSize = new Vector2(1.0f / halfWidth, 1.0f / halfHeight);
                cmd.SetComputeVectorParam(_volumetricFogBlurCS, ShaderIDs._BlurInputTexelSizeId, texelSize);

                int groupsX = PostProcessingUtils.DivRoundUp(halfWidth, 8);
                int groupsY = PostProcessingUtils.DivRoundUp(halfHeight, 8);

                int iterations = settings.blurIterations.value;
                
                // We need to ping-pong. 
                // Initial input is 'input' (likely _volumetricFogRenderRTHandle).
                // We have _volumetricFogBlurRTHandle as temp.
                
                RTHandle currentSource = input;
                RTHandle currentDest = _volumetricFogBlurRTHandle;

                for (int i = 0; i < iterations; ++i)
                {
                    cmd.SetComputeTextureParam(_volumetricFogBlurCS, _volumetricFogBlurKernel, ShaderIDs._BlurInputTextureId, currentSource);
                    cmd.SetComputeTextureParam(_volumetricFogBlurCS, _volumetricFogBlurKernel, ShaderIDs._BlurOutputTextureId, currentDest);
                    cmd.SetComputeTextureParam(_volumetricFogBlurCS, _volumetricFogBlurKernel, ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);
                    
                    cmd.DispatchCompute(_volumetricFogBlurCS, _volumetricFogBlurKernel, groupsX, groupsY, 1);

                    // Swap handles for next iteration or final result
                    // But wait, the loop in RG copies back: CopyTexture(Temp, Input).
                    // So Input always holds the result of the previous step.
                    // Let's mimic that.
                    
                    if (i < iterations - 1)
                    {
                        cmd.CopyTexture(currentDest, currentSource);
                    }
                    else
                    {
                        // Last iteration, result is in currentDest
                        // But we want to return the result.
                    }
                }
                
                return currentDest;
            }
            else
            {
                int iterations = settings.blurIterations.value;
                for (int i = 0; i < iterations; ++i)
                {
                    Blitter.BlitCameraTexture(cmd, input, _volumetricFogBlurRTHandle, _volumetricFogMaterial, _volumetricFogHorizontalBlurPassIndex);
                    Blitter.BlitCameraTexture(cmd, _volumetricFogBlurRTHandle, input, _volumetricFogMaterial, _volumetricFogVerticalBlurPassIndex);
                }
                return input;
            }
        }

        private void RenderUpsample(CommandBuffer cmd, RTHandle fogTexture, RTHandle source, RTHandle destination)
        {
            if (_upsampleInCS)
            {
                ConstantBuffer.Push(cmd, _shaderVariablesBilateralUpsampleCB, _bilateralUpsampleCS, ShaderIDs.ShaderVariablesBilateralUpsample);

                cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._LowResolutionTexture, fogTexture);
                cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._CameraColorTexture, source);
                cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);
                cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._OutputUpscaledTexture, _volumetricFogUpsampleCompositionRTHandle);

                int groupsX = PostProcessingUtils.DivRoundUp(_rtWidth, 8);
                int groupsY = PostProcessingUtils.DivRoundUp(_rtHeight, 8);
                cmd.DispatchCompute(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, groupsX, groupsY, 1);

                Blitter.BlitCameraTexture(cmd, _volumetricFogUpsampleCompositionRTHandle, destination);
            }
            else
            {
                _volumetricFogMaterial.SetTexture(ShaderIDs._VolumetricFogTexture, fogTexture);
                Blitter.BlitCameraTexture(cmd, source, destination, _volumetricFogMaterial, _volumetricFogDepthAwareUpsampleCompositionPassIndex);
            }
        }

        private static void UpdateVolumetricFogComputeShaderParameters(CommandBuffer cmd,
            ComputeShader volumetricFogCS, PostProcessData rendererData,
            int mainLightIndex, int additionalLightsCount,
            Unity.Collections.NativeArray<VisibleLight> visibleLights)
        {
            var fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFog>();

            bool enableMainLightContribution = fogVolume.enableMainLightContribution.value &&
                                               fogVolume.scattering.value > 0.0f && mainLightIndex > -1;
            bool enableAdditionalLightsContribution =
                fogVolume.enableAdditionalLightsContribution.value && additionalLightsCount > 0;

            CoreUtils.SetKeyword(volumetricFogCS, "_MAIN_LIGHT_CONTRIBUTION_DISABLED", enableMainLightContribution);
            CoreUtils.SetKeyword(volumetricFogCS, "_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED", enableAdditionalLightsContribution);

            if (enableAdditionalLightsContribution)
                volumetricFogCS.DisableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
            else
                volumetricFogCS.EnableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
            
            bool enableProbeVolumeContribution = fogVolume.enableProbeVolumeContribution.value 
                                        && fogVolume.probeVolumeContributionWeight.value > 0.0f
                                        &&  rendererData.SampleProbeVolumes;
            if (enableProbeVolumeContribution)
                volumetricFogCS.EnableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");
            else
                volumetricFogCS.DisableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");

            UpdateLightsParametersCS(cmd, volumetricFogCS,
                fogVolume, enableMainLightContribution,
                enableAdditionalLightsContribution, mainLightIndex, visibleLights);

            cmd.SetComputeIntParam(volumetricFogCS, ShaderIDs.FrameCountId, Time.renderedFrameCount % 64);
            cmd.SetComputeIntParam(volumetricFogCS, ShaderIDs.CustomAdditionalLightsCountId, additionalLightsCount);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.DistanceId, fogVolume.distance.value);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.BaseHeightId, fogVolume.baseHeight.value);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.MaximumHeightId, fogVolume.maximumHeight.value);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.GroundHeightId,
                (fogVolume.enableGround.overrideState && fogVolume.enableGround.value)
                    ? fogVolume.groundHeight.value
                    : float.MinValue);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.DensityId, fogVolume.density.value);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.AbsortionId, 1.0f / fogVolume.attenuationDistance.value);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.ProbeVolumeContributionWeigthId, fogVolume.enableProbeVolumeContribution.value ? fogVolume.probeVolumeContributionWeight.value : 0.0f);
            cmd.SetComputeVectorParam(volumetricFogCS, ShaderIDs.TintId, fogVolume.tint.value);
            cmd.SetComputeIntParam(volumetricFogCS, ShaderIDs.MaxStepsId, fogVolume.maxSteps.value);
            cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.TransmittanceThresholdId, fogVolume.transmittanceThreshold.value);
        }

        private static void UpdateLightsParametersCS(CommandBuffer cmd, ComputeShader volumetricFogCS, VolumetricFog fogVolume, 
            bool enableMainLightContribution,
            bool enableAdditionalLightsContribution,
            int mainLightIndex, Unity.Collections.NativeArray<VisibleLight> visibleLights)
        {
            for (int i = 0; i < UniversalRenderPipeline.maxVisibleAdditionalLights; ++i)
            {
                Anisotropies[i] = Scatterings[i] = RadiiSq[i] = 0;
            }

            if (enableMainLightContribution)
            {
                Anisotropies[visibleLights.Length - 1] = fogVolume.anisotropy.value;
                Scatterings[visibleLights.Length - 1] = fogVolume.scattering.value;
            }

            if (enableAdditionalLightsContribution)
            {
                int additionalLightIndex = 0;

                for (int i = 0; i < visibleLights.Length; ++i)
                {
                    if (i == mainLightIndex)
                        continue;

                    float anisotropy = 0.0f;
                    float scattering = 0.0f;
                    float radius = 0.0f;

                    if (VolumetricLightManager.TryGetVolumetricAdditionalLight(visibleLights[i].light, out var volumetricLight))
                    {
                        if (volumetricLight.gameObject.activeInHierarchy && volumetricLight.enabled)
                        {
                            anisotropy = volumetricLight.Anisotropy;
                            scattering = volumetricLight.Scattering;
                            radius = volumetricLight.Radius;
                        }
                    }

                    Anisotropies[additionalLightIndex] = anisotropy;
                    Scatterings[additionalLightIndex] = scattering;
                    RadiiSq[additionalLightIndex++] = radius * radius;
                }
            }

            cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.AnisotropiesArrayId, Anisotropies);
            cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.ScatteringsArrayId, Scatterings);
            cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.RadiiSqArrayId, RadiiSq);
        }
        
        
        private static class ShaderIDs
		{
			public static readonly int _CameraColorTexture = MemberNameHelpers.ShaderPropertyID();
			public static readonly int _LowResolutionTexture = MemberNameHelpers.ShaderPropertyID();
			public static readonly int _OutputUpscaledTexture = MemberNameHelpers.ShaderPropertyID();
			public static readonly int ShaderVariablesBilateralUpsample = MemberNameHelpers.ShaderPropertyID();
			public static readonly int _DownsampledCameraDepthTexture = MemberNameHelpers.ShaderPropertyID();
			public static readonly int _VolumetricFogTexture = MemberNameHelpers.ShaderPropertyID();
			public static readonly int _VolumetricFogOutput = MemberNameHelpers.ShaderPropertyID();
			public static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");
			public static readonly int CustomAdditionalLightsCountId = Shader.PropertyToID("_CustomAdditionalLightsCount");
			public static readonly int DistanceId = Shader.PropertyToID("_Distance");
			public static readonly int BaseHeightId = Shader.PropertyToID("_BaseHeight");
			public static readonly int MaximumHeightId = Shader.PropertyToID("_MaximumHeight");
			public static readonly int GroundHeightId = Shader.PropertyToID("_GroundHeight");
			public static readonly int DensityId = Shader.PropertyToID("_Density");
			public static readonly int AbsortionId = Shader.PropertyToID("_Absortion");
			public static readonly int ProbeVolumeContributionWeigthId = Shader.PropertyToID("_ProbeVolumeContributionWeight");
			public static readonly int TintId = Shader.PropertyToID("_Tint");
			public static readonly int MaxStepsId = Shader.PropertyToID("_MaxSteps");
			public static readonly int TransmittanceThresholdId = Shader.PropertyToID("_TransmittanceThreshold");
			public static readonly int AnisotropiesArrayId = Shader.PropertyToID("_Anisotropies");
			public static readonly int ScatteringsArrayId = Shader.PropertyToID("_Scatterings");
			public static readonly int RadiiSqArrayId = Shader.PropertyToID("_RadiiSq");
			// Blur compute shader properties
			public static readonly int _BlurInputTextureId = Shader.PropertyToID("_BlurInputTexture");
			public static readonly int _BlurOutputTextureId = Shader.PropertyToID("_BlurOutputTexture");
			public static readonly int _BlurInputTexelSizeId = Shader.PropertyToID("_BlurInputTexelSize");
		}
    }
}