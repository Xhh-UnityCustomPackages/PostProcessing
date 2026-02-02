using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
	public partial class VolumetricFogPass
    {
	     /// <summary>
		/// PassData for downsample depth pass.
		/// </summary>
		private class DownsampleDepthPassData
		{
			public Material DownsampleDepthMaterial;
			public int PassIndex;
		}

		/// <summary>
		/// PassData for raymarch pass.
		/// </summary>
		private class RaymarchPassData
		{
			// Fragment shader path
			public Material VolumetricFogMaterial;
			public int PassIndex;
			// Compute shader path
			public ComputeShader RaymarchCS;
			public int RaymarchKernel;
			public int Width;
			public int Height;
			public int ViewCount;
			// Common
			public TextureHandle DownsampledDepthTexture;
			public TextureHandle ExposureTexture;
			public TextureHandle OutputTexture;
			public UniversalLightData LightData;
			public PostProcessData RendererData;
		}

		/// <summary>
		/// PassData for blur pass.
		/// </summary>
		private class BlurPassData
		{
			public int BlurIterations;
			// Fragment shader path
			public Material VolumetricFogMaterial;
			public int HorizontalBlurPassIndex;
			public int VerticalBlurPassIndex;
			// Compute shader path
			public ComputeShader BlurCS;
			public int BlurKernel;
			public int Width;
			public int Height;
			// Common
			public TextureHandle InputTexture;
			public TextureHandle TempBlurTexture;
			public TextureHandle DownsampledDepthTexture;
		}

		/// <summary>
		/// PassData for upsample pass.
		/// </summary>
		private class UpsamplePassData
		{
			// Fragment shader path
			public Material VolumetricFogMaterial;
			public int PassIndex;
			// Compute shader path
			public ComputeShader UpsampleCS;
			public int UpsampleKernel;
			public ShaderVariablesBilateralUpsample UpsampleVariables;
			public int Width;
			public int Height;
			public int ViewCount;
			// Common
			public TextureHandle VolumetricFogTexture;
			public TextureHandle CameraColorTexture;
			public TextureHandle DownsampledDepthTexture;
			public TextureHandle CameraDepthTexture;
			public TextureHandle OutputTexture;
		}

		/// <summary>
		/// PassData for composite pass.
		/// </summary>
		private class CompositePassData
		{
			public TextureHandle SourceTexture;
		}
	    
	    public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
	    {
		    var resource = frameData.Get<UniversalResourceData>();
		    var cameraData = frameData.Get<UniversalCameraData>();
		    // Prepare data
		    PrepareVolumetricFogData(cameraData.cameraTargetDescriptor);
		    
		    // Get input textures from frame resources
		    TextureHandle cameraDepthTexture = resource.cameraDepthTexture;
		    TextureHandle cameraColorTexture = resource.activeColorTexture;
		    
		    // Import external RTHandles
		    TextureHandle exposureTexture = renderGraph.ImportTexture(postProcessData.GetExposureTexture());

		    // Get shadow textures if available
		    TextureHandle mainShadowsTexture = resource.mainShadowsTexture;
		    TextureHandle additionalShadowsTexture = resource.additionalShadowsTexture;
		    
		    // Execute sub-passes in sequence
		    var downsampledDepth = RenderDownsampleDepthPass(renderGraph, cameraDepthTexture);
		    var volumetricFogTexture = RenderRaymarchPass(renderGraph, downsampledDepth, exposureTexture,
			    mainShadowsTexture, additionalShadowsTexture, frameData);
		    volumetricFogTexture = RenderBlurPass(renderGraph, volumetricFogTexture, downsampledDepth);
		    var upsampledTexture = RenderUpsamplePass(renderGraph, volumetricFogTexture, downsampledDepth,
			    cameraColorTexture, cameraDepthTexture);

		    resource.cameraColor = upsampledTexture;
	    }
		
		/// <summary>
		/// Prepares volumetric fog data from rendering data.
		/// </summary>
		/// <param name="cameraData"></param>
		/// <param name="volume"></param>
		private void PrepareVolumetricFogData(RenderTextureDescriptor descriptor)
		{
			// _shadingRateFragmentSize = volume.shadingRate.value;
			// _raymarchInCS = postProcessData.PreferComputeShader && _shadingRateFragmentSize == ShadingRateFragmentSize.FragmentSize1x1;
			_blurInCS = postProcessData.PreferComputeShader;
			_upsampleInCS = postProcessData.PreferComputeShader;

			_rtWidth = descriptor.width;
			_rtHeight = descriptor.height;

			// Prepare bilateral upsample constants
			_shaderVariablesBilateralUpsampleCB._HalfScreenSize = new Vector4((float)_rtWidth / 2, (float)_rtHeight / 2, 
				1.0f / (_rtWidth * 0.5f), 1.0f / (_rtHeight * 0.5f));
			unsafe
			{
				for (int i = 0; i < 16; ++i)
					_shaderVariablesBilateralUpsampleCB._DistanceBasedWeights[i] = BilateralUpsample.distanceBasedWeights_2x2[i];

				for (int i = 0; i < 32; ++i)
					_shaderVariablesBilateralUpsampleCB._TapOffsets[i] = BilateralUpsample.tapOffsets_2x2[i];
			}
		}
		
		private TextureHandle RenderDownsampleDepthPass(RenderGraph renderGraph, TextureHandle cameraDepthTexture)
		{
			using (var builder = renderGraph.AddRasterRenderPass<DownsampleDepthPassData>("Volumetric Fog Downsample Depth", out var passData))
			{
				// Create downsampled depth texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2)
				{
					colorFormat = GraphicsFormat.R32_SFloat,
					name = DownsampledCameraDepthRTName
				};
				var downsampledDepth = renderGraph.CreateTexture(desc);

				passData.DownsampleDepthMaterial = _downsampleDepthMaterial;
				passData.PassIndex = _downsampleDepthPassIndex;

				builder.SetRenderAttachment(downsampledDepth, 0);
				builder.UseTexture(cameraDepthTexture);
				builder.AllowPassCulling(false);

				builder.SetRenderFunc(static (DownsampleDepthPassData data, RasterGraphContext context) =>
				{
					Blitter.BlitTexture(context.cmd, Vector2.one, data.DownsampleDepthMaterial, data.PassIndex);
				});

				return downsampledDepth;
			}
		}

		#region RaymarchPass
		
		private TextureHandle RenderRaymarchPass(RenderGraph renderGraph, TextureHandle downsampledDepth,
			TextureHandle exposureTexture, TextureHandle mainShadowsTexture, TextureHandle additionalShadowsTexture,
			ContextContainer frameData)
		{
			if (_raymarchInCS)
				return RenderRaymarchComputePass(renderGraph, downsampledDepth, exposureTexture,
					mainShadowsTexture, additionalShadowsTexture, frameData);
			return RenderRaymarchFragmentPass(renderGraph, downsampledDepth, mainShadowsTexture,
				additionalShadowsTexture, frameData);
		}
		
		private TextureHandle RenderRaymarchComputePass(RenderGraph renderGraph, TextureHandle downsampledDepth,
			TextureHandle exposureTexture, TextureHandle mainShadowsTexture, TextureHandle additionalShadowsTexture,
			ContextContainer frameData)
		{
			var lightData = frameData.Get<UniversalLightData>();
			using (var builder = renderGraph.AddComputePass<RaymarchPassData>("Volumetric Fog Raymarch (Compute)", out var passData))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					enableRandomWrite = true,
					name = VolumetricFogRenderRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.RaymarchCS = _volumetricFogRaymarchCS;
				passData.RaymarchKernel = _volumetricFogRaymarchKernel;
				passData.Width = _rtWidth / 2;
				passData.Height = _rtHeight / 2;
				passData.ViewCount = 1;
				builder.UseTexture(downsampledDepth);
				passData.DownsampledDepthTexture = downsampledDepth;
				builder.UseTexture(exposureTexture);
				passData.ExposureTexture = exposureTexture;
				builder.UseTexture(outputTexture, AccessFlags.Write);
				passData.OutputTexture = outputTexture;
				passData.LightData = lightData;
				passData.RendererData = postProcessData;

				if (mainShadowsTexture.IsValid())
					builder.UseTexture(mainShadowsTexture);
				if (additionalShadowsTexture.IsValid())
					builder.UseTexture(additionalShadowsTexture);

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc(static (RaymarchPassData data, ComputeGraphContext context) =>
				{
					UpdateVolumetricFogComputeShaderParameters(context.cmd,
						data.RaymarchCS, data.RendererData, data.LightData.mainLightIndex, data.LightData.additionalLightsCount,
						data.LightData.visibleLights);

					context.cmd.SetComputeTextureParam(data.RaymarchCS, data.RaymarchKernel,
						ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);
					context.cmd.SetComputeTextureParam(data.RaymarchCS, data.RaymarchKernel,
						PipelineShaderIDs._ExposureTexture, data.ExposureTexture);
					context.cmd.SetComputeTextureParam(data.RaymarchCS, data.RaymarchKernel,
						ShaderIDs._VolumetricFogOutput, data.OutputTexture);

					int groupsX = PostProcessingUtils.DivRoundUp(data.Width, 8);
					int groupsY = PostProcessingUtils.DivRoundUp(data.Height, 8);
					context.cmd.DispatchCompute(data.RaymarchCS, data.RaymarchKernel, groupsX, groupsY, data.ViewCount);
				});

				return outputTexture;
			}
		}
	
		
		private static void UpdateVolumetricFogComputeShaderParameters(ComputeCommandBuffer cmd,
			ComputeShader volumetricFogCS, PostProcessData rendererData,
			int mainLightIndex, int additionalLightsCount,
			NativeArray<VisibleLight> visibleLights)
		{
			var fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFog>();

			bool enableMainLightContribution = fogVolume.enableMainLightContribution.value &&
											   fogVolume.scattering.value > 0.0f && mainLightIndex > -1;
			bool enableAdditionalLightsContribution =
				fogVolume.enableAdditionalLightsContribution.value && additionalLightsCount > 0;

			// Set compute shader keywords
			CoreUtils.SetKeyword(volumetricFogCS, "_MAIN_LIGHT_CONTRIBUTION_DISABLED", enableMainLightContribution);
			CoreUtils.SetKeyword(volumetricFogCS, "_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED", enableAdditionalLightsContribution);
			
			bool enableProbeVolumeContribution = fogVolume.enableProbeVolumeContribution.value 
										&& fogVolume.probeVolumeContributionWeight.value > 0.0f
										&&  rendererData.SampleProbeVolumes;
			CoreUtils.SetKeyword(volumetricFogCS, "_PROBE_VOLUME_CONTRIBUTION_ENABLED", enableProbeVolumeContribution);

			UpdateLightsParametersCS(cmd, volumetricFogCS,
				fogVolume, enableMainLightContribution,
				enableAdditionalLightsContribution, mainLightIndex, visibleLights);

			// Set compute shader parameters
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
		
		private static void UpdateLightsParametersCS(ComputeCommandBuffer cmd, ComputeShader volumetricFogCS, VolumetricFog fogVolume, 
			bool enableMainLightContribution,
			bool enableAdditionalLightsContribution,
			int mainLightIndex, NativeArray<VisibleLight> visibleLights)
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

			// Always push buffer in CS
			cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.AnisotropiesArrayId, Anisotropies);
			cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.ScatteringsArrayId, Scatterings);
			cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.RadiiSqArrayId, RadiiSq);
		}

		private TextureHandle RenderRaymarchFragmentPass(RenderGraph renderGraph, TextureHandle downsampledDepth,
			TextureHandle mainShadowsTexture, TextureHandle additionalShadowsTexture, ContextContainer frameData)
		{
			var lightData = frameData.Get<UniversalLightData>();
			using (var builder = renderGraph.AddRasterRenderPass<RaymarchPassData>("Volumetric Fog Raymarch (Raster)", out var passData))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = VolumetricFogRenderRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.VolumetricFogMaterial = _volumetricFogMaterial;
				passData.PassIndex = _volumetricFogRenderPassIndex;
				builder.UseTexture(downsampledDepth);
				passData.DownsampledDepthTexture = downsampledDepth;
				builder.SetRenderAttachment(outputTexture, 0);
				passData.OutputTexture = outputTexture;
				passData.LightData = lightData;

				if (mainShadowsTexture.IsValid())
					builder.UseTexture(mainShadowsTexture);
				if (additionalShadowsTexture.IsValid())
					builder.UseTexture(additionalShadowsTexture);

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);
				
				// builder.SetShadingRateFragmentSize(_shadingRateFragmentSize);
				// builder.SetShadingRateCombiner(ShadingRateCombinerStage.Fragment, ShadingRateCombiner.Override);

				builder.SetRenderFunc((RaymarchPassData data, RasterGraphContext context) =>
				{
					UpdateVolumetricFogMaterialParameters(data.VolumetricFogMaterial,
						data.LightData.mainLightIndex, data.LightData.additionalLightsCount, data.LightData.visibleLights);

					data.VolumetricFogMaterial.SetTexture(ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);

					Blitter.BlitTexture(context.cmd, Vector2.one, data.VolumetricFogMaterial, data.PassIndex);
				});

				return outputTexture;
			}
		}
		
		/// <summary>
		/// Updates the volumetric fog material parameters.
		/// </summary>
		/// <param name="volumetricFogMaterial"></param>
		/// <param name="mainLightIndex"></param>
		/// <param name="additionalLightsCount"></param>
		/// <param name="visibleLights"></param>
		private void UpdateVolumetricFogMaterialParameters(Material volumetricFogMaterial,
			int mainLightIndex, int additionalLightsCount,
			NativeArray<VisibleLight> visibleLights)
		{
			bool enableMainLightContribution = settings.enableMainLightContribution.value &&
			                                   settings.scattering.value > 0.0f && mainLightIndex > -1;
			bool enableAdditionalLightsContribution =
				settings.enableAdditionalLightsContribution.value && additionalLightsCount > 0;

			bool enableProbeVolumeContribution = settings.enableProbeVolumeContribution.value
			                                     && settings.probeVolumeContributionWeight.value > 0.0f
			                                     &&  postProcessData.SampleProbeVolumes;
			if (enableProbeVolumeContribution)
				volumetricFogMaterial.EnableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");
			else
				volumetricFogMaterial.DisableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");

			if (enableMainLightContribution)
				volumetricFogMaterial.DisableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");
			else
				volumetricFogMaterial.EnableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");

			if (enableAdditionalLightsContribution)
				volumetricFogMaterial.DisableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
			else
				volumetricFogMaterial.EnableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");

			UpdateLightsParameters(volumetricFogMaterial,
				settings, enableMainLightContribution,
				enableAdditionalLightsContribution, mainLightIndex, visibleLights);

			volumetricFogMaterial.SetInteger(ShaderIDs.FrameCountId, Time.renderedFrameCount % 64);
			volumetricFogMaterial.SetInteger(ShaderIDs.CustomAdditionalLightsCountId, additionalLightsCount);
			volumetricFogMaterial.SetFloat(ShaderIDs.DistanceId, settings.distance.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.BaseHeightId, settings.baseHeight.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.MaximumHeightId, settings.maximumHeight.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.GroundHeightId,
				(settings.enableGround.overrideState && settings.enableGround.value)
					? settings.groundHeight.value
					: float.MinValue);
			volumetricFogMaterial.SetFloat(ShaderIDs.DensityId, settings.density.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.AbsortionId, 1.0f / settings.attenuationDistance.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.ProbeVolumeContributionWeigthId, settings.enableProbeVolumeContribution.value ? settings.probeVolumeContributionWeight.value : 0.0f);
			volumetricFogMaterial.SetColor(ShaderIDs.TintId, settings.tint.value);
			volumetricFogMaterial.SetInteger(ShaderIDs.MaxStepsId, settings.maxSteps.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.TransmittanceThresholdId, settings.transmittanceThreshold.value);
		}
		
		/// <summary>
		/// Updates the lights parameters from the material.
		/// </summary>
		/// <param name="volumetricFogMaterial"></param>
		/// <param name="fogVolume"></param>
		/// <param name="enableMainLightContribution"></param>
		/// <param name="enableAdditionalLightsContribution"></param>
		/// <param name="mainLightIndex"></param>
		/// <param name="visibleLights"></param>
		private static void UpdateLightsParameters(Material volumetricFogMaterial, VolumetricFog fogVolume, 
			bool enableMainLightContribution, bool enableAdditionalLightsContribution,
			int mainLightIndex, NativeArray<VisibleLight> visibleLights)
		{
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

			if (enableMainLightContribution || enableAdditionalLightsContribution)
			{
				volumetricFogMaterial.SetFloatArray(ShaderIDs.AnisotropiesArrayId, Anisotropies);
				volumetricFogMaterial.SetFloatArray(ShaderIDs.ScatteringsArrayId, Scatterings);
				volumetricFogMaterial.SetFloatArray(ShaderIDs.RadiiSqArrayId, RadiiSq);
			}
		}

		#endregion
		
		#region Blur
		
		private TextureHandle RenderBlurPass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth)
		{
			if (_blurInCS)
				return RenderBlurComputePass(renderGraph, volumetricFogTexture, downsampledDepth);
			return RenderBlurFragmentPass(renderGraph, volumetricFogTexture);
		}
		
		private TextureHandle RenderBlurComputePass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth)
		{
			using (var builder = renderGraph.AddComputePass<BlurPassData>("Volumetric Fog Blur (Compute)", out var passData))
			{
				// Create temp blur texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					enableRandomWrite = true,
					name = VolumetricFogBlurRTName
				};
				var tempBlurTexture = renderGraph.CreateTexture(desc);

				passData.BlurCS = _volumetricFogBlurCS;
				passData.BlurKernel = _volumetricFogBlurKernel;
				passData.BlurIterations = settings.blurIterations.value;
				passData.Width = _rtWidth / 2;
				passData.Height = _rtHeight / 2;
				builder.UseTexture(volumetricFogTexture, AccessFlags.ReadWrite);
				passData.InputTexture = volumetricFogTexture;
				builder.UseTexture(tempBlurTexture, AccessFlags.Write);
				passData.TempBlurTexture = tempBlurTexture;
				builder.UseTexture(downsampledDepth);
				passData.DownsampledDepthTexture = downsampledDepth;

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc(static (BlurPassData data, ComputeGraphContext context) =>
				{
					// Ping-pong blur between two textures
					int halfWidth = data.Width;
					int halfHeight = data.Height;
					Vector2 texelSize = new Vector2(1.0f / halfWidth, 1.0f / halfHeight);

					context.cmd.SetComputeVectorParam(data.BlurCS, ShaderIDs._BlurInputTexelSizeId, texelSize);

					int groupsX = PostProcessingUtils.DivRoundUp(halfWidth, 8);
					int groupsY = PostProcessingUtils.DivRoundUp(halfHeight, 8);

					for (int i = 0; i < data.BlurIterations; ++i)
					{
						context.cmd.SetComputeTextureParam(data.BlurCS, data.BlurKernel,
							ShaderIDs._BlurInputTextureId, data.InputTexture);
						context.cmd.SetComputeTextureParam(data.BlurCS, data.BlurKernel,
							ShaderIDs._BlurOutputTextureId, data.TempBlurTexture);
						context.cmd.SetComputeTextureParam(data.BlurCS, data.BlurKernel,
							ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);
						context.cmd.DispatchCompute(data.BlurCS, data.BlurKernel, groupsX, groupsY, 1);

						// Copy back for next iteration
						if (i < data.BlurIterations - 1)
						{
							context.cmd.GetNativeCommandBuffer().CopyTexture(data.TempBlurTexture, data.InputTexture);
						}
					}
				});

				// Return the blurred result (last iteration was written to TempBlurTexture)
				return passData.TempBlurTexture;
			}
		}
		
		private TextureHandle RenderBlurFragmentPass(RenderGraph renderGraph, TextureHandle volumetricFogTexture)
		{
			using (var builder = renderGraph.AddUnsafePass<BlurPassData>("Volumetric Fog Blur (Raster)", out var passData))
			{
				// Create temp blur texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = VolumetricFogBlurRTName
				};
				var tempBlurTexture = renderGraph.CreateTexture(desc);

				passData.VolumetricFogMaterial = _volumetricFogMaterial;
				passData.HorizontalBlurPassIndex = _volumetricFogHorizontalBlurPassIndex;
				passData.VerticalBlurPassIndex = _volumetricFogVerticalBlurPassIndex;
				passData.BlurIterations = settings.blurIterations.value;
				builder.UseTexture(volumetricFogTexture, AccessFlags.ReadWrite);
				passData.InputTexture = volumetricFogTexture;
				builder.UseTexture(tempBlurTexture, AccessFlags.Write);
				passData.TempBlurTexture = tempBlurTexture;
				
				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc(static (BlurPassData data, UnsafeGraphContext context) =>
				{
					CommandBuffer unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

					int blurIterations = data.BlurIterations;

					for (int i = 0; i < blurIterations; ++i)
					{
						Blitter.BlitCameraTexture(unsafeCmd, data.InputTexture, data.TempBlurTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, data.VolumetricFogMaterial, data.HorizontalBlurPassIndex);
						Blitter.BlitCameraTexture(unsafeCmd, data.TempBlurTexture, data.InputTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, data.VolumetricFogMaterial, data.VerticalBlurPassIndex);
					}
				});

				return passData.InputTexture;
			}
		}
		#endregion

		#region RenderUpsamplePass

		private TextureHandle RenderUpsamplePass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth, TextureHandle cameraColorTexture, TextureHandle cameraDepthTexture)
		{
			if (_upsampleInCS)
				return RenderUpsampleComputePass(renderGraph, volumetricFogTexture, downsampledDepth,
					cameraColorTexture, cameraDepthTexture);
			return RenderUpsampleFragmentPass(renderGraph, volumetricFogTexture, downsampledDepth,
				cameraColorTexture, cameraDepthTexture);
		}
		
		private TextureHandle RenderUpsampleComputePass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth, TextureHandle cameraColorTexture, TextureHandle cameraDepthTexture)
		{
			using (var builder = renderGraph.AddComputePass<UpsamplePassData>("Volumetric Fog Upsample (Compute)", out var passData))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth, _rtHeight)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					enableRandomWrite = true,
					name = VolumetricFogUpsampleCompositionRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.UpsampleCS = _bilateralUpsampleCS;
				passData.UpsampleKernel = _bilateralUpSampleColorKernel;
				passData.UpsampleVariables = _shaderVariablesBilateralUpsampleCB;
				passData.Width = _rtWidth;
				passData.Height = _rtHeight;
				passData.ViewCount = 1;
				builder.UseTexture(volumetricFogTexture);
				passData.VolumetricFogTexture = volumetricFogTexture;
				builder.UseTexture(cameraColorTexture);
				passData.CameraColorTexture = cameraColorTexture;
				builder.UseTexture(downsampledDepth);
				passData.DownsampledDepthTexture = downsampledDepth;
				builder.UseTexture(cameraDepthTexture);
				passData.CameraDepthTexture = cameraDepthTexture;
				builder.UseTexture(outputTexture, AccessFlags.Write);
				passData.OutputTexture = outputTexture;

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc(static (UpsamplePassData data, ComputeGraphContext context) =>
				{
					var ncmd= CommandBufferHelpersExtensions.GetNativeCommandBuffer(context.cmd);
					ConstantBuffer.Push(ncmd, data.UpsampleVariables, data.UpsampleCS, ShaderIDs.ShaderVariablesBilateralUpsample);

					// Inject all the input buffers
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._LowResolutionTexture, data.VolumetricFogTexture);
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._CameraColorTexture, data.CameraColorTexture);
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);

					// Inject the output textures
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._OutputUpscaledTexture, data.OutputTexture);

					// Upscale the buffer to full resolution
					int groupsX = PostProcessingUtils.DivRoundUp(data.Width, 8);
					int groupsY = PostProcessingUtils.DivRoundUp(data.Height, 8);
					context.cmd.DispatchCompute(data.UpsampleCS, data.UpsampleKernel, groupsX, groupsY, data.ViewCount);
				});

				return outputTexture;
			}
		}
		
		private TextureHandle RenderUpsampleFragmentPass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth, TextureHandle cameraColorTexture, TextureHandle cameraDepthTexture)
		{
			using (var builder = renderGraph.AddRasterRenderPass<UpsamplePassData>("Volumetric Fog Upsample (Raster)", out var passData))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth, _rtHeight)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = VolumetricFogUpsampleCompositionRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.VolumetricFogMaterial = _volumetricFogMaterial;
				passData.PassIndex = _volumetricFogDepthAwareUpsampleCompositionPassIndex;
				builder.UseTexture(volumetricFogTexture);
				passData.VolumetricFogTexture = volumetricFogTexture;
				builder.UseTexture(cameraColorTexture);
				passData.CameraColorTexture = cameraColorTexture;
				builder.UseTexture(downsampledDepth);
				passData.DownsampledDepthTexture = downsampledDepth;
				builder.UseTexture(cameraDepthTexture);
				passData.CameraDepthTexture = cameraDepthTexture;
				builder.SetRenderAttachment(outputTexture, 0);
				passData.OutputTexture = outputTexture;
				
				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc(static (UpsamplePassData data, RasterGraphContext context) =>
				{
					data.VolumetricFogMaterial.SetTexture(ShaderIDs._VolumetricFogTexture, data.VolumetricFogTexture);
					
					Blitter.BlitTexture(context.cmd, data.CameraColorTexture, Vector2.one, data.VolumetricFogMaterial, data.PassIndex);
				});

				return outputTexture;
			}
		}

		#endregion
    }
}