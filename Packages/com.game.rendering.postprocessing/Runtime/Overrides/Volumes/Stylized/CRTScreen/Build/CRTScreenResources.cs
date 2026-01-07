using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: CRTScreenResources", Order = 2001), HideInInspector]
    public class CRTScreenResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Stylized/CRTScreen/Shaders/CRTScreen.shader")]
        private Shader m_CRTScreenPS;
        public Shader CRTScreenPS
        {
            get => m_CRTScreenPS;
            set => this.SetValueAndNotify(ref m_CRTScreenPS, value);
        }
    }
}