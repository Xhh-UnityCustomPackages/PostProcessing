using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/Weather/Rain")]
    public class Rain : VolumeSetting
    {
        public override bool IsActive() => false;



    }


    [PostProcess("Rain", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class RainRenderer : PostProcessVolumeRenderer<Rain>
    {
        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {

        }
    }
}
