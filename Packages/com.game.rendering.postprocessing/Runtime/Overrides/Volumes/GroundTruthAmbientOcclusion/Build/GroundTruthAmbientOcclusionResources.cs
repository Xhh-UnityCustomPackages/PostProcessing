using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: GroundTruthAmbientOcclusionResources", Order = 2001), HideInInspector]
    public class GroundTruthAmbientOcclusionResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/GroundTruthAmbientOcclusion/Shaders/GTAO.compute")]
        private ComputeShader m_GTAOCS;
        public ComputeShader GTAOCS
        {
            get => m_GTAOCS;
            set => this.SetValueAndNotify(ref m_GTAOCS, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/GroundTruthAmbientOcclusion/Shaders/GTAOSpatialDenoise.compute")]
        private ComputeShader m_GTAOSpatialDenoiseCS;
        public ComputeShader GTAOSpatialDenoiseCS
        {
            get => m_GTAOSpatialDenoiseCS;
            set => this.SetValueAndNotify(ref m_GTAOSpatialDenoiseCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/GroundTruthAmbientOcclusion/Shaders/GTAOTemporalDenoise.compute")]
        private ComputeShader m_GTAOTemporalDenoiseCS;
        public ComputeShader GTAOTemporalDenoiseCS
        {
            get => m_GTAOTemporalDenoiseCS;
            set => this.SetValueAndNotify(ref m_GTAOTemporalDenoiseCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/GroundTruthAmbientOcclusion/Shaders/GTAOCopyHistory.compute")]
        private ComputeShader m_GTAOCopyHistoryCS;
        public ComputeShader GTAOCopyHistoryCS 
        {
            get => m_GTAOCopyHistoryCS;
            set => this.SetValueAndNotify(ref m_GTAOCopyHistoryCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/GroundTruthAmbientOcclusion/Shaders/GTAOBlurAndUpsample.compute")]
        private ComputeShader m_GTAOBlurAndUpsample;
        public ComputeShader GTAOBlurAndUpsample
        {
            get => m_GTAOBlurAndUpsample;
            set => this.SetValueAndNotify(ref m_GTAOBlurAndUpsample, value);
        }
    }
}