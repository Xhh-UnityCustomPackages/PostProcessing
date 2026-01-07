using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: AtmosphericHeightFogResources", Order = 2001), HideInInspector]
    public class AtmosphericHeightFogResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Weather/AtmosphericHeightFog/Shaders/AtmosphericHeightFog.shader")]
        private Shader m_AtmosphericHeightFogPS;
        public Shader atmosphericHeightFogPS
        {
            get => m_AtmosphericHeightFogPS;
            set => this.SetValueAndNotify(ref m_AtmosphericHeightFogPS, value);
        }
    }
}