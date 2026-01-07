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
        #endregion
    }
}