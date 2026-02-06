using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/体积雾HDRP (Volumetric Fog HDRP)")]
    public class VolumetricFogHDRP : VolumeSetting
    {
	    public VolumetricFogHDRP()
	    {
		    displayName = "体积雾HDRP (Volumetric Fog HDRP)";
	    }
	    
	    public override bool IsActive() => enabled.value;
	    
        [Tooltip("Disabling this will completely remove any feature from the volumetric fog from being rendered at all.")]
		public BoolParameter enabled = new(false, BoolParameter.DisplayType.EnumPopup);

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
		
        // Limit parameters for the fog quality
        internal const float minFogScreenResolutionPercentage = (1.0f / 16.0f) * 100;
        internal const float optimalFogScreenResolutionPercentage = (1.0f / 8.0f) * 100;
        internal const float maxFogScreenResolutionPercentage = 0.5f * 100;
        internal const int maxFogSliceCount = 512;
        
        // [AdditionalProperty]
        [Tooltip("Specifies which method to use to control the performance and quality of the volumetric fog.")]
        public EnumParameter<FogControl> fogControlMode = new (FogControl.Balance);
		
        /// <summary>Stores the resolution of the volumetric buffer (3D texture) along the x-axis and y-axis relative to the resolution of the screen.</summary>
        // [AdditionalProperty]
        [Tooltip("Controls the resolution of the volumetric buffer (3D texture) along the x-axis and y-axis relative to the resolution of the screen.")]
        public ClampedFloatParameter screenResolutionPercentage = new ClampedFloatParameter(optimalFogScreenResolutionPercentage, minFogScreenResolutionPercentage, maxFogScreenResolutionPercentage);
        /// <summary>Number of slices of the volumetric buffer (3D texture) along the camera's focal axis.</summary>
        // [AdditionalProperty]
        [Tooltip("Controls the number of slices to use the volumetric buffer (3D texture) along the camera's focal axis.")]
        public ClampedIntParameter volumeSliceCount = new ClampedIntParameter(64, 1, maxFogSliceCount);


        [Header("MainLight")] 
        public ClampedFloatParameter mainLightMultiplier = new ClampedFloatParameter(1, 0, 16);
        public ClampedFloatParameter mainLightShadowDimmer = new ClampedFloatParameter(1, 0, 1);

        public float volumetricFogBudget
        {
	        get
	        {
		        switch (qualityMode.value)
		        {
			        case FogQualityMode.Heigh:
				        m_VolumetricFogBudget.value = 0.666f;    
				        break;

			        case FogQualityMode.Medium:
				        m_VolumetricFogBudget.value = 0.33f;
				        break;
			        case FogQualityMode.Low:
				        m_VolumetricFogBudget.value = 0.166f;
				        break;
		        }
		        return m_VolumetricFogBudget.value;
	        }
	        set { m_VolumetricFogBudget.value = value; }
        }
        [AdditionalProperty]
        [SerializeField, FormerlySerializedAs("volumetricFogBudget")]
        [Tooltip("Controls the performance to quality ratio of the volumetric fog. A value of 0 being the least resource-intensive and a value of 1 being the highest quality.")]
        public ClampedFloatParameter m_VolumetricFogBudget = new ClampedFloatParameter(0.25f, 0.0f, 1.0f);

        public float resolutionDepthRatio
        {
	        get
	        {
		        switch (qualityMode.value)
		        {
			        case FogQualityMode.Heigh:
				        m_ResolutionDepthRatio.value = 0.50f;
				        break;

			        case FogQualityMode.Medium:
				        m_ResolutionDepthRatio.value = 0.666f;
				        break;
			        case FogQualityMode.Low:
				        m_ResolutionDepthRatio.value = 0.666f;
				        break;

		        }
		        return m_ResolutionDepthRatio.value;
	        }
	        set { m_ResolutionDepthRatio.value = value; }
        }
        
        /// <summary>Controls how Unity shares resources between Screen (XY) and Depth (Z) resolutions.</summary>
        [AdditionalProperty]
        [SerializeField, FormerlySerializedAs("resolutionDepthRatio")]
        [Tooltip("Controls how Unity shares resources between Screen (x-axis and y-axis) and Depth (z-axis) resolutions.")]
        public ClampedFloatParameter m_ResolutionDepthRatio = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        
        /// <summary>Indicates whether Unity includes or excludes non-directional light types when it evaluates the volumetric fog. Including non-directional lights increases the resource intensity of the effect.</summary>
        [AdditionalProperty]
        [Tooltip("When enabled, HDRP only includes directional Lights when it evaluates volumetric fog.")]
        public BoolParameter directionalLightsOnly = new BoolParameter(false);
        
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
		
		/// <summary>
		/// Options that control the quality and resource intensity of the volumetric fog.
		/// </summary>
		public enum FogControl
		{
			/// <summary>
			/// Use this mode if you want to change the fog control properties based on a higher abstraction level centered around performance.
			/// </summary>
			Balance,

			/// <summary>
			/// Use this mode if you want to have direct access to the internal properties that control volumetric fog.
			/// </summary>
			Manual
		}
		
		internal static bool IsFogEnabled()
		{
			var stack = VolumeManager.instance.stack;
			return stack.GetComponent<VolumetricFogHDRP>().enabled.value;
		}

		internal static bool IsVolumetricFogEnabled(Camera camera)
		{
			var stack = VolumeManager.instance.stack;
			var fog = stack.GetComponent<VolumetricFogHDRP>();

			bool a = fog.enableVolumetricFog.value;
			bool c = CoreUtils.IsSceneViewFogEnabled(camera);
			bool d = fog.enabled.value;
			return a && c && d;
		}
		
		internal static bool IsVolumetricReprojectionEnabled()
		{
			var stack = VolumeManager.instance.stack;
			var fog = stack.GetComponent<VolumetricFogHDRP>();

			return (fog.denoisingMode.value & FogDenoisingMode.Reprojection) != 0;
		}
		
		internal static void UpdateShaderVariablesGlobalCB(ref VolumetricGlobalParams cb)
		{
			// TODO Handle user override
			var fogSettings = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();

			// Those values are also used when fog is disabled
			cb._PBRFogEnabled = 0;
			cb._MaxFogDistance = fogSettings.maxFogDistance.value;


			fogSettings.UpdateShaderVariablesGlobalCBFogParameters(ref cb);
		}
		
		static void UpdateShaderVariablesGlobalCBNeutralParameters(ref VolumetricGlobalParams cb)
		{
			cb._FogEnabled = 0;
			cb._EnableVolumetricFog = 0;
			cb._HeightFogBaseScattering = Vector3.zero;
			cb._HeightFogBaseExtinction = 0.0f;
			cb._HeightFogExponents = Vector2.one;
			cb._HeightFogBaseHeight = 0.0f;
			cb._GlobalFogAnisotropy = 0.0f;
		}
		
		void UpdateShaderVariablesGlobalCBFogParameters(ref VolumetricGlobalParams cb)
		{
			bool enableVolumetrics = enableVolumetricFog.value;

			cb._FogEnabled = 1;
			cb._EnableVolumetricFog = enableVolumetrics ? 1 : 0;

			Color fogColor = (colorMode.value == FogColorMode.ConstantColor) ? color.value : tint.value;
			cb._FogColorMode = (float)colorMode.value;
			cb._FogColor = new Color(fogColor.r, fogColor.g, fogColor.b, 0.0f);
			cb._MipFogParameters = new Vector4(mipFogNear.value, mipFogFar.value, mipFogMaxMip.value, 0.0f);

			LocalVolumetricFogArtistParameters param = new LocalVolumetricFogArtistParameters(albedo.value, meanFreePath.value, anisotropy.value);
			LocalVolumetricFogEngineData data = param.ConvertToEngineData();

			// When volumetric fog is disabled, we don't want its color to affect the heightfog. So we pass neutral values here.
			var extinction = VolumeRenderingUtils.ExtinctionFromMeanFreePath(param.meanFreePath);
			cb._HeightFogBaseScattering = enableVolumetrics ? data.scattering : Vector4.one * extinction;
			cb._HeightFogBaseExtinction = extinction;

			float crBaseHeight = baseHeight.value;
			//URP当前未支持相机相对渲染
			//if (ShaderConfig.s_CameraRelativeRendering != 0)
			//  crBaseHeight -= cb._PlanetUpAltitude.w;

			float layerDepth = Mathf.Max(0.01f, maximumHeight.value - baseHeight.value);
			float H = VolumetricNormalFunctions.ScaleHeightFromLayerDepth(layerDepth);
			cb._HeightFogExponents = new Vector2(1.0f / H, H);
			cb._HeightFogBaseHeight = crBaseHeight;
			cb._GlobalFogAnisotropy = anisotropy.value;
			cb._VolumetricFilteringEnabled = ((int)denoisingMode.value & (int)FogDenoisingMode.Gaussian) != 0 ? 1 : 0;
			cb._FogDirectionalOnly = directionalLightsOnly.value ? 1 : 0;
		}
		
    }
}
