using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: VolumetricFogResources", Order = 2001), HideInInspector]
    public class VolumetricFogResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFog/Shaders/VolumetricFogUpsample.compute")]
        private ComputeShader m_VolumetricFogUpsampleCS;

        public ComputeShader volumetricFogUpsampleCS
        {
            get => m_VolumetricFogUpsampleCS;
            set => this.SetValueAndNotify(ref m_VolumetricFogUpsampleCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFog/Shaders/VolumetricFogRaymarch.compute")]
        private ComputeShader m_VolumetricFogRaymarchCS;

        public ComputeShader volumetricFogRaymarchCS
        {
            get => m_VolumetricFogRaymarchCS;
            set => this.SetValueAndNotify(ref m_VolumetricFogRaymarchCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFog/Shaders/VolumetricFogBlur.compute")]
        private ComputeShader m_VolumetricFogBlurCS;

        public ComputeShader volumetricFogBlurCS
        {
            get => m_VolumetricFogBlurCS;
            set => this.SetValueAndNotify(ref m_VolumetricFogBlurCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/VolumetricFog/Shaders/VolumetricFog.shader")]
        private Shader m_VolumetricFogPS;

        public Shader volumetricFogPS
        {
            get => m_VolumetricFogPS;
            set => this.SetValueAndNotify(ref m_VolumetricFogPS, value);
        }
        
    }
}
