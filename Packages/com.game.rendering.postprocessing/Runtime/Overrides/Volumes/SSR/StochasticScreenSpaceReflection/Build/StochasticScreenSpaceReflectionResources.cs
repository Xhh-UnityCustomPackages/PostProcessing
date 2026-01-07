using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: StochasticScreenSpaceReflectionResources", Order = 2001), HideInInspector]
    public class StochasticScreenSpaceReflectionResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/SSR/StochasticScreenSpaceReflection/Shaders/StochasticScreenSpaceReflection.shader")]
        private Shader m_StochasticScreenSpaceReflectionPS;
        public Shader stochasticScreenSpaceReflectionPS
        {
            get => m_StochasticScreenSpaceReflectionPS;
            set => this.SetValueAndNotify(ref m_StochasticScreenSpaceReflectionPS, value);
        }
    }
}