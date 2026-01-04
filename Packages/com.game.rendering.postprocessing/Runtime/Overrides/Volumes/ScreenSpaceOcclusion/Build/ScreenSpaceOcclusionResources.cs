using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: ScreenSpaceOcclusionResources", Order = 2001), HideInInspector]
    public class ScreenSpaceOcclusionResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/ScreenSpaceOcclusion/Shaders/ScreenSpaceOcclusion.shader")]
        public Shader m_ScreenSpaceOcclusionPS;
        
        public Shader ScreenSpaceOcclusionPS
        {
            get => m_ScreenSpaceOcclusionPS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceOcclusionPS, value);
        }
    }
}
