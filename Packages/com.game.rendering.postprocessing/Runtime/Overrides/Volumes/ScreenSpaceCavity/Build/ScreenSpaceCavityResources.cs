using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: ScreenSpaceCavityResources", Order = 2001), HideInInspector]
    public class ScreenSpaceCavityResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/ScreenSpaceCavity/Shaders/ScreenSpaceCavity.shader")]
        public Shader m_ScreenSpaceCavityPS;
        
        public Shader ScreenSpaceCavityPS
        {
            get => m_ScreenSpaceCavityPS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceCavityPS, value);
        }
    }
}
