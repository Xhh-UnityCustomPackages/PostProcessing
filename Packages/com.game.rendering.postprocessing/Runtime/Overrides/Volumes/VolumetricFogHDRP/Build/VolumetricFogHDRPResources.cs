using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: VolumetricFogHDRPResources", Order = 2001), HideInInspector]
    public class VolumetricFogHDRPResources : IRenderPipelineResources
    {
        public int version => 0;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        // [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFogHDRP/Shaders/AtmosphereScattering.shader")]
        private Shader m_DefaultFogVolumeShader;

        public Shader defaultFogVolumeShader
        {
            get => m_DefaultFogVolumeShader;
            set => this.SetValueAndNotify(ref m_DefaultFogVolumeShader, value);
        }

        // [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFogHDRP/Shaders/AtmosphereScattering.shader")]
        private Shader m_AtmosphereScattering;

        public Shader atmosphereScattering
        {
            get => m_AtmosphereScattering;
            set => this.SetValueAndNotify(ref m_AtmosphereScattering, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFogHDRP/Shaders/GenerateMaxZ.compute")]
        private ComputeShader m_ComputeMaxDepth;

        public ComputeShader computeMaxDepth
        {
            get => m_ComputeMaxDepth;
            set => this.SetValueAndNotify(ref m_ComputeMaxDepth, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFogHDRP/Shaders/VolumetricLighting.compute")]
        private ComputeShader m_VolumetricFogLighting;

        public ComputeShader volumetricFogLighting
        {
            get => m_VolumetricFogLighting;
            set => this.SetValueAndNotify(ref m_VolumetricFogLighting, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFogHDRP/Shaders/VolumetricLightingFiltering.compute")]
        private ComputeShader m_VolumetricLightingFilter;
        public ComputeShader volumetricLightingFilter
        {
            get => m_VolumetricLightingFilter;
            set => this.SetValueAndNotify(ref m_VolumetricLightingFilter, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFogHDRP/Shaders/VolumetricMaterial.compute")]
        private ComputeShader m_VolumetricMaterial;
        public ComputeShader volumetricMaterial
        {
            get => m_VolumetricMaterial;
            set => this.SetValueAndNotify(ref m_VolumetricMaterial, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFogHDRP/Shaders/VolumeVoxelization.compute")]
        private ComputeShader m_VolumeVoxelization;
        public ComputeShader volumeVoxelization
        {
            get => m_VolumeVoxelization;
            set => this.SetValueAndNotify(ref m_VolumeVoxelization, value);
        }
        
    }
}