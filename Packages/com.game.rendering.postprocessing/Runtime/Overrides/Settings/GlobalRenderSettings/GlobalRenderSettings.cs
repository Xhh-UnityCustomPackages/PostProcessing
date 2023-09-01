using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Settings/GlobalRenderSettings")]
    public class GlobalRenderSettings : VolumeSetting
    {
        public override bool IsActive() => true;

    }

    [PostProcess("GlobalRenderSettings", PostProcessInjectionPoint.BeforeRenderingGBuffer)]
    public class GlobalRenderSettingsRenderer : PostProcessVolumeRenderer<GlobalRenderSettings>
    {
        public override bool renderToCamera => false;
        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData) { }
    }
}
