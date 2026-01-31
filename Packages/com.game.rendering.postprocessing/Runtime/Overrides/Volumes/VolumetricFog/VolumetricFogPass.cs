using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [PostProcess("体积雾 (Volumetric Fog)", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class VolumetricFogPass : PostProcessVolumeRenderer<VolumetricFog>
    {
        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
        }
    }
}