using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: ExposureResources", Order = 2001), HideInInspector]
    public class ExposureResources : IRenderPipelineResources
    {
        public int version => 0;
        
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Exposure/Shaders/Exposure.compute")]
        private ComputeShader m_ExposureCS;

        public ComputeShader exposureCS
        {
            get => m_ExposureCS;
            set => this.SetValueAndNotify(ref m_ExposureCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Exposure/Shaders/HistogramExposure.compute")]
        private ComputeShader m_HistogramExposureCS;
        
        public ComputeShader HistogramExposureCS
        {
            get => m_HistogramExposureCS;
            set => this.SetValueAndNotify(ref m_HistogramExposureCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Exposure/Shaders/DebugHistogramImage.compute")]
        private ComputeShader m_DebugHistogramImageCS;
        
        public ComputeShader DebugHistogramImageCS
        {
            get => m_DebugHistogramImageCS;
            set => this.SetValueAndNotify(ref m_DebugHistogramImageCS, value);
        }
    }
}