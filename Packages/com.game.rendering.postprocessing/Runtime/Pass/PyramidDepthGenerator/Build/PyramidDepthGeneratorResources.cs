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
        
        [SerializeField, ResourcePath("Runtime/Pass/PyramidDepthGenerator/Shader/DepthPyramid.compute")]
        private ComputeShader m_DepthPyramidCS;
        
        public ComputeShader depthPyramidCS
        {
            get => m_DepthPyramidCS;
            set => this.SetValueAndNotify(ref m_DepthPyramidCS, value);
        }
    }
}