using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: ContactShadowResources", Order = 2001), HideInInspector]
    public class ContactShadowResources : IRenderPipelineResources
    {
        public int version => 0;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Shadows/ContactShadows/Shaders/ContactShadows.compute")]
        private ComputeShader m_ContractShadowCS;
        
        public ComputeShader contractShadowCS
        {
            get => m_ContractShadowCS;
            set => this.SetValueAndNotify(ref m_ContractShadowCS, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Shadows/ContactShadows/Shaders/DiffuseShadowDenoiser.compute")]
        private ComputeShader m_DiffuseShadowDenoiserCS;
        
        public ComputeShader diffuseShadowDenoiserCS
        {
            get => m_DiffuseShadowDenoiserCS;
            set => this.SetValueAndNotify(ref m_DiffuseShadowDenoiserCS, value);
        }
    }
}