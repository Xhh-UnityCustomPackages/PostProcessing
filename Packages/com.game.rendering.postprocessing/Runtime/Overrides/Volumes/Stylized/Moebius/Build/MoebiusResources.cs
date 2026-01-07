using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: MoebiusResources", Order = 2001), HideInInspector]
    public class MoebiusResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Stylized/Moebius/Shaders/Moebius.shader")]
        private Shader m_MoebiusPS;
        public Shader MoebiusPS
        {
            get => m_MoebiusPS;
            set => this.SetValueAndNotify(ref m_MoebiusPS, value);
        }
    }
}