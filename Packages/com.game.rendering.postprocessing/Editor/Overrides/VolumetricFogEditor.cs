using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Game.Core.PostProcessing.UnityEditor
{
    [CustomEditor(typeof(VolumetricFog))]
    public class VolumetricFogEditor : VolumeComponentEditor
    {
         protected SerializedDataParameter m_Enabled;
        protected SerializedDataParameter m_MaxFogDistance;
        protected SerializedDataParameter m_ColorMode;
        protected SerializedDataParameter m_Color;
        protected SerializedDataParameter m_Tint;
        protected SerializedDataParameter m_MipFogNear;
        protected SerializedDataParameter m_MipFogFar;
        protected SerializedDataParameter m_MipFogMaxMip;
        protected SerializedDataParameter m_Albedo;
        protected SerializedDataParameter m_MeanFreePath;
        protected SerializedDataParameter m_BaseHeight;
        protected SerializedDataParameter m_MaximumHeight;

        protected SerializedDataParameter m_EnableVolumetricFog;
        protected SerializedDataParameter m_Anisotropy;
        protected SerializedDataParameter m_MultipleScatteringIntensity;
        protected SerializedDataParameter m_DepthExtent;
        protected SerializedDataParameter m_GlobalLightProbeDimmer;
        protected SerializedDataParameter m_SliceDistributionUniformity;
        protected SerializedDataParameter m_FogControlMode;
        protected SerializedDataParameter m_ScreenResolutionPercentage;
        protected SerializedDataParameter m_VolumeSliceCount;
        protected SerializedDataParameter m_VolumetricFogBudget;
        protected SerializedDataParameter m_ResolutionDepthRatio;
        protected SerializedDataParameter m_DirectionalLightsOnly;
        protected SerializedDataParameter m_DenoisingMode;

        static GUIContent s_Enabled = new GUIContent("State", "When set to Enabled, HDRP renders fog in your scene.");
        static GUIContent s_AlbedoLabel = new GUIContent("Albedo", "Specifies the color this fog scatters light to.");
        static GUIContent s_MeanFreePathLabel = new GUIContent("Fog Attenuation Distance", "Controls the density at the base level (per color channel). Distance at which fog reduces background light intensity by 63%. Units: m.");
        static GUIContent s_BaseHeightLabel = new GUIContent("Base Height", "Reference height (e.g. sea level). Sets the height of the boundary between the constant and exponential fog.");
        static GUIContent s_MaximumHeightLabel = new GUIContent("Maximum Height", "Max height of the fog layer. Controls the rate of height-based density falloff. Units: m.");
        static GUIContent s_GlobalLightProbeDimmerLabel = new GUIContent("GI Dimmer", "Controls the intensity reduction of the global illumination contribution to volumetric fog. This is either APV (if enabled and present) or the global light probe that the sky produces.");
        static GUIContent s_EnableVolumetricFog = new GUIContent("Volumetric Fog", "When enabled, activates volumetric fog.");
        static GUIContent s_DepthExtentLabel = new GUIContent("Volumetric Fog Distance", "Sets the distance (in meters) from the Camera's Near Clipping Plane to the back of the Camera's volumetric lighting buffer. The lower the distance is, the higher the fog quality is.");

        public override void OnEnable()
        {
            var o = new PropertyFetcher<VolumetricFog>(serializedObject);

            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_MaxFogDistance = Unpack(o.Find(x => x.maxFogDistance));

            // Fog Color
            m_ColorMode = Unpack(o.Find(x => x.colorMode));
            m_Color = Unpack(o.Find(x => x.color));
            m_Tint = Unpack(o.Find(x => x.tint));
            m_MipFogNear = Unpack(o.Find(x => x.mipFogNear));
            m_MipFogFar = Unpack(o.Find(x => x.mipFogFar));
            m_MipFogMaxMip = Unpack(o.Find(x => x.mipFogMaxMip));
            m_Albedo = Unpack(o.Find(x => x.albedo));
            m_MeanFreePath = Unpack(o.Find(x => x.meanFreePath));
            m_BaseHeight = Unpack(o.Find(x => x.baseHeight));
            m_MaximumHeight = Unpack(o.Find(x => x.maximumHeight));
            m_Anisotropy = Unpack(o.Find(x => x.anisotropy));
            m_MultipleScatteringIntensity = Unpack(o.Find(x => x.multipleScatteringIntensity));
            m_GlobalLightProbeDimmer = Unpack(o.Find(x => x.globalLightProbeDimmer));

            m_EnableVolumetricFog = Unpack(o.Find(x => x.enableVolumetricFog));
            m_DepthExtent = Unpack(o.Find(x => x.depthExtent));
            m_SliceDistributionUniformity = Unpack(o.Find(x => x.sliceDistributionUniformity));
            m_FogControlMode = Unpack(o.Find(x => x.fogControlMode));
            m_ScreenResolutionPercentage = Unpack(o.Find(x => x.screenResolutionPercentage));
            m_VolumeSliceCount = Unpack(o.Find(x => x.volumeSliceCount));
            m_VolumetricFogBudget = Unpack(o.Find(x => x.volumetricFogBudget));
            m_ResolutionDepthRatio = Unpack(o.Find(x => x.resolutionDepthRatio));
            m_DirectionalLightsOnly = Unpack(o.Find(x => x.directionalLightsOnly));
            m_DenoisingMode = Unpack(o.Find(x => x.denoisingMode));

            base.OnEnable();
        }
    }
}