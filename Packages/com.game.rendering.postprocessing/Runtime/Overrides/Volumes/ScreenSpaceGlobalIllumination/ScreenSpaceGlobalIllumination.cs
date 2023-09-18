using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    // https://www.shadertoy.com/view/fddfDX
    [Serializable, VolumeComponentMenu("Post-processing Custom/全局光照 (Screen Space Global Illumination)")]
    public class ScreenSpaceGlobalIllumination : VolumeSetting
    {
        public ScreenSpaceGlobalIllumination()
        {
            displayName = "全局光照 (Screen Space Global Illumination)";
        }

        public FloatParameter intensity = new FloatParameter(1f);

        public override bool IsActive() => true;
    }


    [PostProcess("Screen Space Global Illumination", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class ScreenSpaceGlobalIlluminationRenderer : PostProcessVolumeRenderer<ScreenSpaceGlobalIllumination>
    {
        static class ShaderConstants
        {

        }


        private Material m_Material;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.ScreenSpaceGlobalIlluminationPS);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            Blit(cmd, source, destination, m_Material, 0);
        }
    }
}
