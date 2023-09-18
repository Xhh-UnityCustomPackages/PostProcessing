using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/大气高度雾 (Atmospheric Height Fog)")]
    public class AtmosphericHeightFog : VolumeSetting
    {
        public AtmosphericHeightFog()
        {
            displayName = "大气高度雾 (Atmospheric Height Fog)";
        }


        public override bool IsActive() => false;
    }
}
