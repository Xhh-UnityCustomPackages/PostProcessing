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
    }
}