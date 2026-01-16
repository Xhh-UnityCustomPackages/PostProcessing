using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: PostProcessFeatureRuntimeResources", Order = 2001), HideInInspector]
    public class PostProcessFeatureRuntimeResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        #region Utilities
        [Header("Utilities / Core")]
        [SerializeField, ResourcePath("Runtime/Core/CoreResources/GPUCopy.compute")]
        private ComputeShader m_CopyChannelCS;

        public ComputeShader copyChannelCS
        {
            get => m_CopyChannelCS;
            set => this.SetValueAndNotify(ref m_CopyChannelCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Pass/PyramidDepthGenerator/Shader/DepthPyramid.compute")]
        private ComputeShader m_DepthPyramidCS;
        
        public ComputeShader depthPyramidCS
        {
            get => m_DepthPyramidCS;
            set => this.SetValueAndNotify(ref m_DepthPyramidCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Pass/PyramidColorGenerator/Shader/ColorPyramid.compute")]
        private ComputeShader m_ColorPyramidCS;
        
        public ComputeShader colorPyramidCS
        {
            get => m_ColorPyramidCS;
            set => this.SetValueAndNotify(ref m_ColorPyramidCS, value);
        }
        
        #endregion
    }
}