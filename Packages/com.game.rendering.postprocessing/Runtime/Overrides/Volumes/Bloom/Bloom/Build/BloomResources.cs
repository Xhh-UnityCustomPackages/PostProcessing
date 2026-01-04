using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: BloomResources", Order = 2001), HideInInspector]
    public class BloomResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Bloom/Bloom/Shaders/Bloom.shader")]
        private Shader m_BloomPS;

        public Shader bloomPS
        {
            get => m_BloomPS;
            set => this.SetValueAndNotify(ref m_BloomPS, value);
        }
    }
}
