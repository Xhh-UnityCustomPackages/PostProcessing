using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/卷积泛光 (Convolution Bloom)")]
    public class BloomConvolution : VolumeSetting
    {
        public BloomConvolution()
        {
            displayName = "卷积泛光 (Convolution Bloom)";
        }

        public override bool IsActive() => false;
    }
}
