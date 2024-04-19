using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [PostProcess("VolumetricCloudsHDRP", PostProcessInjectionPoint.BeforeRenderingPostProcessing)]
    public class VolumetricCloudsHDRPRenderer : PostProcessVolumeRenderer<VolumetricCloudsHDRP>
    {
        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            throw new NotImplementedException();
        }
    }
}
