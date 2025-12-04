using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/体积雾 (Volumetric Fog)")]
    public class VolumetricFog : VolumeSetting
    {
	    public VolumetricFog()
	    {
		    displayName = "体积雾 (Volumetric Fog)";
	    }
	    
        [Tooltip("Disabling this will completely remove any feature from the volumetric fog from being rendered at all.")]
		public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

		[Header("Distances")]
		[Tooltip("The maximum distance from the camera that the fog will be rendered up to.")]
		public ClampedFloatParameter distance = new(64.0f, 0.0f, 512.0f);

		[Tooltip("The world height at which the fog will have the density specified in the volume.")]
		public FloatParameter baseHeight = new(0.0f, true);

		[Tooltip("The world height at which the fog will have no density at all.")]
		public FloatParameter maximumHeight = new(50.0f, true);

		[Header("Ground")]
		[Tooltip("When enabled, allows to define a world height. Below it, fog will have no density at all.")]
		public BoolParameter enableGround = new(false, BoolParameter.DisplayType.Checkbox, true);

		[Tooltip("Below this world height, fog will have no density at all.")]
		public FloatParameter groundHeight = new(0.0f);

		[Header("Lighting")]
		[Tooltip("How dense is the fog.")]
		public ClampedFloatParameter density = new(0.2f, 0.0f, 1.0f);

		[Tooltip("Value that defines how much the fog attenuates light as distance increases. Lesser values lead to a darker image.")]
		public MinFloatParameter attenuationDistance = new(128.0f, 0.05f);
		
		[Tooltip("When enabled, probe volumes will be sampled to contribute to fog.")]
		public BoolParameter enableProbeVolumeContribution = new(false, BoolParameter.DisplayType.Checkbox, true);

		[Tooltip("A weight factor for the light coming from adaptive probe volumes when the probe volume contribution is enabled.")]
		public ClampedFloatParameter probeVolumeContributionWeight = new(1.0f, 0.0f, 1.0f);

		[Header("Main Light")]
		[Tooltip("Disabling this will avoid computing the main light contribution to fog, which in most cases will lead to better performance.")]
		public BoolParameter enableMainLightContribution = new(false, BoolParameter.DisplayType.Checkbox, true);

		[Tooltip("Higher positive values will make the fog affected by the main light to appear brighter when directly looking to it, while lower negative values will make the fog to appear brighter when looking away from it. The closer the value is closer to 1 or -1, the less the brightness will spread. Most times, positive values higher than 0 and lower than 1 should be used.")]
		public ClampedFloatParameter anisotropy = new(0.4f, -1.0f, 1.0f);

		[Tooltip("Higher values will make fog affected by the main light to appear brighter.")]
		public ClampedFloatParameter scattering = new(0.15f, 0.0f, 1.0f);

		[Tooltip("A multiplier color to tint the main light fog.")]
		public ColorParameter tint = new(Color.white, true, false, true);

		[Header("Additional Lights")]
		[Tooltip("Disabling this will avoid computing additional lights contribution to fog, which in most cases will lead to better performance.")]
		public BoolParameter enableAdditionalLightsContribution = new(false, BoolParameter.DisplayType.Checkbox, true);

		[AdditionalProperty]
		[Header("Performance & Quality")]
		[Tooltip("Raymarching steps. Greater values will increase the fog quality at the expense of performance.")]
		public ClampedIntParameter maxSteps = new(128, 8, 256);

		[AdditionalProperty]
		[Tooltip("The number of times that the fog texture will be blurred. Higher values lead to softer volumetric god rays at the cost of some performance.")]
		public ClampedIntParameter blurIterations = new(2, 1, 4);

		[AdditionalProperty]
		[Tooltip("Early exit threshold for raymarching optimization. When transmittance falls below this value, raymarching stops early. Lower values = better performance but may cause artifacts. Set to 0 to disable early exit.")]
		public ClampedFloatParameter transmittanceThreshold = new(0.01f, 0.0f, 0.1f);
		
		public override bool IsActive() => enable.value;

		private void OnValidate()
		{
			maximumHeight.overrideState = baseHeight.overrideState;
			maximumHeight.value = Mathf.Max(baseHeight.value, maximumHeight.value);
			baseHeight.value = Mathf.Min(baseHeight.value, maximumHeight.value);
		}
    }

    [PostProcess("体积雾 (Volumetric Fog)", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class VolumetricFogRenderer : PostProcessVolumeRenderer<VolumetricFog>
    {
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

	    private Material _downsampleDepthMaterial;
	    private Material _volumetricFogMaterial;
	    
	    private ComputeShader _volumetricFogRaymarchCS;
	    private int _volumetricFogRaymarchKernel;

	    private ComputeShader _volumetricFogBlurCS;
	    private int _volumetricFogBlurKernel;
		
	    private ComputeShader _bilateralUpsampleCS;
	    private int _bilateralUpSampleColorKernel;

	    private RTHandle _downsampledCameraDepthRTHandle;
	    private RTHandle _volumetricFogRenderRTHandle;
	    private RTHandle _volumetricFogBlurRTHandle;
	    private RTHandle _volumetricFogUpsampleCompositionRTHandle;

	    private readonly ProfilingSampler _downsampleDepthProfilingSampler = new("Downsample Depth");
	    private readonly ProfilingSampler _raymarchSampler = new("Raymarch");
	    private readonly ProfilingSampler _blurSampler = new("Blur");
	    private readonly ProfilingSampler _upsampleSampler = new("Upsample");
	    private readonly ProfilingSampler _compositeSampler = new("Composite");
	    
	    private bool _upsampleInCS;
	    private bool _raymarchInCS;
	    private bool _blurInCS;

	    private int _rtWidth;
	    private int _rtHeight;

	    public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth;

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

	    public override void Setup()
	    {
		    profilingSampler = new ProfilingSampler("Volumetric Fog");
		    _bilateralUpsampleCS = postProcessFeatureData.computeShaders.volumetricFogUpsampleCS;
		    _bilateralUpSampleColorKernel = _bilateralUpsampleCS.FindKernel("VolumetricFogBilateralUpSample");
		    _volumetricFogRaymarchCS = postProcessFeatureData.computeShaders.volumetricFogRaymarchCS;
		    _volumetricFogRaymarchKernel = _volumetricFogRaymarchCS.FindKernel("VolumetricFogRaymarch");
		    _volumetricFogBlurCS = postProcessFeatureData.computeShaders.volumetricFogBlurCS;
		    _volumetricFogBlurKernel = _volumetricFogBlurCS.FindKernel("VolumetricFogBlur");
		    
		    _downsampleDepthMaterial = CoreUtils.CreateEngineMaterial(postProcessFeatureData.shaders.DownsampleDepth);
		    _volumetricFogMaterial = CoreUtils.CreateEngineMaterial(postProcessFeatureData.shaders.VolumetricFog);
		    
		    InitializePassesIndices();
	    }

	    public override void Dispose(bool disposing)
	    {
		    CoreUtils.Destroy(_downsampleDepthMaterial);
		    CoreUtils.Destroy(_volumetricFogMaterial);
		    _downsampledCameraDepthRTHandle?.Release();
		    _volumetricFogRenderRTHandle?.Release();
		    _volumetricFogBlurRTHandle?.Release();
		    _volumetricFogUpsampleCompositionRTHandle?.Release();
	    }

	    private void InitializePassesIndices()
	    {
		    _downsampleDepthPassIndex = _downsampleDepthMaterial.FindPass("DownsampleDepth");
		    _volumetricFogRenderPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogRender");
		    _volumetricFogHorizontalBlurPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogHorizontalBlur");
		    _volumetricFogVerticalBlurPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogVerticalBlur");
		    _volumetricFogDepthAwareUpsampleCompositionPassIndex = _volumetricFogMaterial.FindPass("VolumetricFogDepthAwareUpsampleComposition");
	    }

	    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
	    {
		    // _raymarchInCS = postProcessFeatureData.PreferComputeShader;
		    // _blurInCS = postProcessFeatureData.PreferComputeShader;
		    // _upsampleInCS = postProcessFeatureData.PreferComputeShader;
		    
		    var descriptor = renderingData.cameraData.cameraTargetDescriptor;
		    _rtWidth = descriptor.width;
		    _rtHeight = descriptor.height;
		    descriptor.depthBufferBits = (int)DepthBits.None;

		    RenderTextureFormat originalColorFormat = descriptor.colorFormat;
		    Vector2Int originalResolution = new Vector2Int(descriptor.width, descriptor.height);
			DescriptorDownSample(ref descriptor, 2);
		    descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
		    RenderingUtils.ReAllocateHandleIfNeeded(ref _downsampledCameraDepthRTHandle, descriptor, wrapMode: TextureWrapMode.Clamp, name: DownsampledCameraDepthRTName);

		    descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
		    if (_raymarchInCS)
		    {
			    descriptor.enableRandomWrite = true;
		    }
		    RenderingUtils.ReAllocateHandleIfNeeded(ref _volumetricFogRenderRTHandle, descriptor, wrapMode: TextureWrapMode.Clamp, name: VolumetricFogRenderRTName);
		    
		    // Blur RT needs random write access if using compute shader
		    if (_blurInCS)
		    {
			    descriptor.enableRandomWrite = true;
		    }
		    else
		    {
			    descriptor.enableRandomWrite = false;
		    }
		    RenderingUtils.ReAllocateHandleIfNeeded(ref _volumetricFogBlurRTHandle, descriptor, wrapMode: TextureWrapMode.Clamp, name: VolumetricFogBlurRTName);
		    
		    descriptor.width = originalResolution.x;
		    descriptor.height = originalResolution.y;
		    descriptor.colorFormat = originalColorFormat;
		    if (_upsampleInCS)
		    {
			    descriptor.enableRandomWrite = true;
		    }
		    RenderingUtils.ReAllocateHandleIfNeeded(ref _volumetricFogUpsampleCompositionRTHandle, descriptor, wrapMode: TextureWrapMode.Clamp, name: VolumetricFogUpsampleCompositionRTName);

		    // _shaderVariablesBilateralUpsampleCB._HalfScreenSize = new Vector4(_rtWidth / 2, _rtHeight / 2,
			   //  1.0f / (_rtWidth * 0.5f), 1.0f / (_rtHeight * 0.5f));
		    // unsafe
		    // {
			   //  for (int i = 0; i < 16; ++i)
				  //   _shaderVariablesBilateralUpsampleCB._DistanceBasedWeights[i] = BilateralUpsample.distanceBasedWeights_2x2[i];
		    //
			   //  for (int i = 0; i < 32; ++i)
				  //   _shaderVariablesBilateralUpsampleCB._TapOffsets[i] = BilateralUpsample.tapOffsets_2x2[i];
		    // }
	    }

	    public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
	    {
		    // using (new ProfilingScope(cmd, _downsampleDepthProfilingSampler))
		    // {
			   //  Blit(cmd, _downsampledCameraDepthRTHandle, _downsampledCameraDepthRTHandle, _downsampleDepthMaterial, _downsampleDepthPassIndex);
			   //  _volumetricFogMaterial.SetTexture(ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);
		    // }
		    
		    using (new ProfilingScope(cmd, _raymarchSampler))
		    {
			    DoRaymarch(cmd, ref renderingData);
		    }
		    
		    using (new ProfilingScope(cmd, _blurSampler))
		    {
			    // DoBlur(cmd, ref renderingData);
		    }

		    using (new ProfilingScope(cmd, _upsampleSampler))
		    {
			    // DoUpsample(cmd, ref renderingData);
		    }
		    
		    using (new ProfilingScope(cmd, _compositeSampler))
		    {
			    var cameraColorRt = renderingData.cameraData.renderer.cameraColorTargetHandle;
			    Blit(cmd, _volumetricFogUpsampleCompositionRTHandle, cameraColorRt);
		    }
	    }
	    
	    private void DoRaymarch(CommandBuffer cmd, ref RenderingData renderingData)
	    {
		    // if (_raymarchInCS)
		    // {
			   //  UpdateVolumetricFogComputeShaderParameters(cmd, _volumetricLightManager, _volumetricFogRaymarchCS,
				  //   renderingData.lightData.mainLightIndex,
				  //   renderingData.lightData.additionalLightsCount, renderingData.lightData.visibleLights);
		    //
			   //  DoRaymarchComputeShader(cmd, ref renderingData);
		    // }
		    // else
		    // {
			   //  UpdateVolumetricFogMaterialParameters(_volumetricLightManager, _volumetricFogMaterial.Value,
				  //   renderingData.lightData.mainLightIndex,
				  //   renderingData.lightData.additionalLightsCount, renderingData.lightData.visibleLights);
			   //  Blitter.BlitCameraTexture(cmd, _volumetricFogRenderRTHandle, _volumetricFogRenderRTHandle,
				  //   _volumetricFogMaterial.Value, _volumetricFogRenderPassIndex);
		    // }
	    }
    }
}
