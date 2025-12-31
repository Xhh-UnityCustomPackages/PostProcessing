using System;
using System.Collections.Generic;
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

		 /// <summary>Fog color mode.</summary>
        public EnumParameter<FogColorMode> colorMode = new (FogColorMode.SkyColor);
        /// <summary>Fog color.</summary>
        [Tooltip("Specifies the constant color of the fog.")]
        public ColorParameter color = new ColorParameter(Color.grey, hdr: true, showAlpha: false, showEyeDropper: true);
        /// <summary>Specifies the tint of the fog when using Sky Color.</summary>
        [Tooltip("Specifies the tint of the fog.")]
        public ColorParameter tint = new ColorParameter(Color.white, hdr: true, showAlpha: false, showEyeDropper: true);
        /// <summary>Maximum fog distance.</summary>
        [Tooltip("Sets the maximum fog distance HDRP uses when it shades the skybox or the Far Clipping Plane of the Camera.")]
        public MinFloatParameter maxFogDistance = new MinFloatParameter(5000.0f, 0.0f);
        /// <summary>Controls the maximum mip map HDRP uses for mip fog (0 is the lowest mip and 1 is the highest mip).</summary>
        [AdditionalProperty]
        [Tooltip("Controls the maximum mip map HDRP uses for mip fog (0 is the lowest mip and 1 is the highest mip).")]
        public ClampedFloatParameter mipFogMaxMip = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        /// <summary>Sets the distance at which HDRP uses the minimum mip image of the blurred sky texture as the fog color.</summary>
        [AdditionalProperty]
        [Tooltip("Sets the distance at which HDRP uses the minimum mip image of the blurred sky texture as the fog color.")]
        public MinFloatParameter mipFogNear = new MinFloatParameter(0.0f, 0.0f);
        /// <summary>Sets the distance at which HDRP uses the maximum mip image of the blurred sky texture as the fog color.</summary>
        [AdditionalProperty]
        [Tooltip("Sets the distance at which HDRP uses the maximum mip image of the blurred sky texture as the fog color.")]
        public MinFloatParameter mipFogFar = new MinFloatParameter(1000.0f, 0.0f);

        // Height Fog
        /// <summary>Height fog base height.</summary>
        public FloatParameter baseHeight = new FloatParameter(0.0f);
        /// <summary>Height fog maximum height.</summary>
        public FloatParameter maximumHeight = new FloatParameter(50.0f);
        /// <summary>Fog mean free path.</summary>
        [DisplayInfo(name = "Fog Attenuation Distance")]
        public MinFloatParameter meanFreePath = new MinFloatParameter(400.0f, 1.0f);

        // Optional Volumetric Fog
        /// <summary>Enable volumetric fog.</summary>
        [DisplayInfo(name = "Volumetric Fog")]
        public BoolParameter enableVolumetricFog = new BoolParameter(false);
        // Common Fog Parameters (Exponential/Volumetric)
        /// <summary>Stores the fog albedo. This defines the color of the fog.</summary>
        public ColorParameter albedo = new ColorParameter(Color.white);
        /// <summary>Multiplier for global illumination (APV or ambient probe).</summary>
        [DisplayInfo(name = "GI Dimmer")]
        public ClampedFloatParameter globalLightProbeDimmer = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        /// <summary>Sets the distance (in meters) from the Camera's Near Clipping Plane to the back of the Camera's volumetric lighting buffer. The lower the distance is, the higher the fog quality is.</summary>
        public MinFloatParameter depthExtent = new MinFloatParameter(64.0f, 0.1f);
        /// <summary>Controls which denoising technique to use for the volumetric effect.</summary>
        /// <remarks>Reprojection mode is effective for static lighting but can lead to severe ghosting artifacts with highly dynamic lighting. Gaussian mode is effective with dynamic lighting. You can also use both modes together which produces high-quality results, but increases the resource intensity of processing the effect.</remarks>
        [Tooltip("Specifies the denoising technique to use for the volumetric effect.")]
        public EnumParameter<FogDenoisingMode> denoisingMode = new (FogDenoisingMode.Gaussian);

        public EnumParameter<FogQualityMode> qualityMode = new (FogQualityMode.Low);

        /// <summary>Controls the angular distribution of scattered light. 0 is isotropic, 1 is forward scattering, and -1 is backward scattering.</summary>
        [AdditionalProperty]
        [Tooltip("Controls the angular distribution of scattered light. 0 is isotropic, 1 is forward scattering, and -1 is backward scattering.")]
        public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.0f, -1.0f, 1.0f);

        /// <summary>Controls the distribution of slices along the Camera's focal axis. 0 is exponential distribution and 1 is linear distribution.</summary>
        [AdditionalProperty]
        [Tooltip("Controls the distribution of slices along the Camera's focal axis. 0 is exponential distribution and 1 is linear distribution.")]
        public ClampedFloatParameter sliceDistributionUniformity = new ClampedFloatParameter(0.75f, 0, 1);

        /// <summary>Controls how much the multiple-scattering will affect the scene. Directly controls the amount of blur depending on the fog density.</summary>
        [AdditionalProperty]
        [Tooltip("Use this value to simulate multiple scattering when combining the fog with the scene color.")]
        public ClampedFloatParameter multipleScatteringIntensity = new ClampedFloatParameter(0.0f, 0.0f, 2.0f);
		
		public override bool IsActive() => enable.value;

		private void OnValidate()
		{
			maximumHeight.overrideState = baseHeight.overrideState;
			maximumHeight.value = Mathf.Max(baseHeight.value, maximumHeight.value);
			baseHeight.value = Mathf.Min(baseHeight.value, maximumHeight.value);
		}
		
		public enum FogColorMode
		{
			/// <summary>Fog is a constant color.</summary>
			ConstantColor,
			/// <summary>Fog uses the current sky to determine its color.</summary>
			SkyColor,
		}
		
		/// <summary>
		/// Options that control which denoising algorithms Unity should use on the volumetric fog signal.
		/// </summary>
		public enum FogDenoisingMode
		{
			/// <summary>
			/// Use this mode to not filter the volumetric fog.
			/// </summary>
			None = 0,
			/// <summary>
			/// Use this mode to reproject data from previous frames to denoise the signal. This is effective for static lighting, but it can lead to severe ghosting artifacts for highly dynamic lighting.
			/// </summary>
			Reprojection = 1 << 0,
			/// <summary>
			/// Use this mode to reduce the aliasing patterns that can appear on the volumetric fog.
			/// </summary>
			Gaussian = 1 << 1,
			/// <summary>
			/// Use this mode to use both Reprojection and Gaussian filtering techniques. This produces high visual quality, but significantly increases the resource intensity of the effect.
			/// </summary>
			Both = Reprojection | Gaussian
		}

		public enum FogQualityMode
		{
			Heigh = 0,

			Medium = 1,

			Low = 2,
		}
    }

    [PostProcess("体积雾 (Volumetric Fog)", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public partial class VolumetricFogRenderer : PostProcessVolumeRenderer<VolumetricFog>
    {
	    static internal ComputeShader m_VolumeVoxelizationCS = null;
	    static internal ComputeShader m_VolumetricLightingCS = null;
	    static internal ComputeShader m_VolumetricLightingFilteringCS = null;
	    
	    static internal List<PerCameraVolumetricFogData> perCameraDatas = new List<PerCameraVolumetricFogData>();
	    
	    public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth;


	    public override void Setup()
	    {
		    var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogResources>();
		    m_VolumeVoxelizationCS = runtimeShaders.volumeVoxelization;
		    m_VolumetricLightingCS = runtimeShaders.volumetricFogLighting;
		    m_VolumetricLightingFilteringCS = runtimeShaders.volumetricLightingFilter;
		    
		    profilingSampler = new ProfilingSampler("Volumetric Fog");
		    
	    }

	    public override void Dispose(bool disposing)
	    {
	    }

	    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
	    {
		   
	    }

	    public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
	    {
		    var camera = renderingData.cameraData.camera;
		    //初始化VBuffer数据
		    // ReinitializeVolumetricBufferParams(camera);
	    }

	    // static internal void ReinitializeVolumetricBufferParams(VolumetricCameraParams hdCamera)
	    // {
		   //  bool init = perCameraDatas[nowCameraIndex].vBufferParams != null;
	    //
	    //
		   //  if (init)
		   //  {
			  //   // Deinitialize.
			  //   perCameraDatas[nowCameraIndex].vBufferParams = null;
		   //  }
		   //  else
		   //  {
			  //   // Initialize.
			  //   // Start with the same parameters for both frames. Then update them one by one every frame.
			  //   var parameters = ComputeVolumetricBufferParameters(hdCamera);
			  //   perCameraDatas[nowCameraIndex].vBufferParams = new VBufferParameters[2];
			  //   perCameraDatas[nowCameraIndex].vBufferParams[0] = parameters;
			  //   perCameraDatas[nowCameraIndex].vBufferParams[1] = parameters;
		   //  }
	    // }
    }
}
