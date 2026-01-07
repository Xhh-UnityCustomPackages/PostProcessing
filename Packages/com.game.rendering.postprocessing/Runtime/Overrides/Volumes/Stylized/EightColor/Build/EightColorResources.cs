using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: EightColorResources", Order = 2001), HideInInspector]
    public class EightColorResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Stylized/EightColor/Shaders/EightColor.shader")]
        private Shader m_EightColorPS;
        public Shader EightColorPS
        {
            get => m_EightColorPS;
            set => this.SetValueAndNotify(ref m_EightColorPS, value);
        }
    }
}