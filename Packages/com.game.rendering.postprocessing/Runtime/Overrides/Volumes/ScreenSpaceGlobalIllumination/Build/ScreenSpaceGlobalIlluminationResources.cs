using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: VolumetricFogResources", Order = 2001), HideInInspector]
    public class ScreenSpaceGlobalIlluminationResources : IRenderPipelineResources
    {
        public int version => 0;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/ScreenSpaceGlobalIllumination/Shaders/ScreenSpaceGlobalIllumination.compute")]
        private ComputeShader m_SSGIComputeShader;
        public ComputeShader screenSpaceGlobalIlluminationCS
        {
            get => m_SSGIComputeShader;
            set => this.SetValueAndNotify(ref m_SSGIComputeShader, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/ScreenSpaceGlobalIllumination/Shaders/DiffuseDenoiser.compute")]
        private ComputeShader m_DiffuseDenoiserCS;
        public ComputeShader diffuseDenoiserCS
        {
            get => m_DiffuseDenoiserCS;
            set => this.SetValueAndNotify(ref m_DiffuseDenoiserCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/ScreenSpaceGlobalIllumination/Shaders/BilateralUpsample.compute")]
        private ComputeShader m_BilateralUpsampleCS;
        public ComputeShader bilateralUpsampleCS
        {
            get => m_BilateralUpsampleCS;
            set => this.SetValueAndNotify(ref m_BilateralUpsampleCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/ScreenSpaceGlobalIllumination/Shaders/TemporalFilter.compute")]
        private ComputeShader m_TemporalFilterCS;
        public ComputeShader temporalFilterCS
        {
            get => m_TemporalFilterCS;
            set => this.SetValueAndNotify(ref m_TemporalFilterCS, value);
        }
    }
}