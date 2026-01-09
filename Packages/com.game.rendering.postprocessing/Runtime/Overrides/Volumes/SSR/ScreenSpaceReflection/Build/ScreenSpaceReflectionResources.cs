using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: ScreenSpaceReflectionResources", Order = 2001), HideInInspector]
    public class ScreenSpaceReflectionResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/SSR/ScreenSpaceReflection/Shaders/ScreenSpaceReflection.shader")]
        private Shader m_ScreenSpaceReflectionPS;
        public Shader screenSpaceReflectionPS
        {
            get => m_ScreenSpaceReflectionPS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceReflectionPS, value);
        }
        
        //----------------------------------
        // ComputeShader 方法
        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/SSR/ScreenSpaceReflection/Shaders/ScreenSpaceReflection.compute")]
        private ComputeShader m_ScreenSpaceReflectionCS;
        public ComputeShader screenSpaceReflectionCS
        {
            get => m_ScreenSpaceReflectionCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceReflectionCS, value);
        }
    }
}