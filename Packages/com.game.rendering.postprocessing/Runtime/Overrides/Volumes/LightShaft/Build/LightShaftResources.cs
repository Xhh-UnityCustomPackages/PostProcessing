using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: LightShaftResources", Order = 2001), HideInInspector]
    public class LightShaftResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/LightShaft/Shaders/LightShaft.shader")]
        private Shader m_LightShaftPS;

        public Shader lightShaftPS
        {
            get => m_LightShaftPS;
            set => this.SetValueAndNotify(ref m_LightShaftPS, value);
        }
    }
}
