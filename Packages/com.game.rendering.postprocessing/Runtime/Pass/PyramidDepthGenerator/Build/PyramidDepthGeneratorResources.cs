using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: PyramidDepthGeneratorResources", Order = 2001), HideInInspector]
    public class PyramidDepthGeneratorResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Pass/PyramidDepthGenerator/Shader/PyramidDepth.compute")]
        private ComputeShader m_HiZCS;
        
        public ComputeShader hiZCS
        {
            get => m_HiZCS;
            set => this.SetValueAndNotify(ref m_HiZCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Pass/PyramidDepthGenerator/Shader/PyramidDepthV2.compute")]
        private ComputeShader m_HiZV2CS;
        
        public ComputeShader hiZV2CS
        {
            get => m_HiZV2CS;
            set => this.SetValueAndNotify(ref m_HiZV2CS, value);
        }
    }
}