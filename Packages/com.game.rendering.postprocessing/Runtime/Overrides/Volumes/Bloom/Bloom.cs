using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/泛光 (Bloom)")]
    public class Bloom : VolumeSetting
    {
        public Bloom()
        {
            displayName = "泛光 (Bloom)";
        }



        [Header("Bloom")]
        [Tooltip("Filters out pixels under this level of brightness. Value is in gamma-space.")]
        public MinFloatParameter threshold = new MinFloatParameter(0.9f, 0f);

        /// <summary>
        /// Controls the strength of the bloom filter.
        /// </summary>
        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);

        /// <summary>
        /// Controls the extent of the veiling effect.
        /// </summary>
        [Tooltip("Set the radius of the bloom effect.")]
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0f, 1f);

        /// <summary>
        /// Set the maximum intensity that Unity uses to calculate Bloom.
        /// If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.
        /// </summary>
        [Tooltip("Set the maximum intensity that Unity uses to calculate Bloom. If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.")]
        public MinFloatParameter clamp = new MinFloatParameter(65472f, 0f);

        /// <summary>
        /// Specifies the tint of the bloom filter.
        /// </summary>
        [Tooltip("Use the color picker to select a color for the Bloom effect to tint to.")]
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);

        /// <summary>
        /// Controls whether to use bicubic sampling instead of bilinear sampling for the upsampling passes.
        /// This is slightly more expensive but helps getting smoother visuals.
        /// </summary>
        [Tooltip("Use bicubic sampling instead of bilinear sampling for the upsampling passes. This is slightly more expensive but helps getting smoother visuals.")]
        public BoolParameter highQualityFiltering = new BoolParameter(false);

        /// <summary>
        /// Controls the starting resolution that this effect begins processing.
        /// </summary>
        [Tooltip("The starting resolution that this effect begins processing."), AdditionalProperty]
        public DownscaleParameter downscale = new DownscaleParameter(BloomDownscaleMode.Half);

        /// <summary>
        /// Controls the maximum number of iterations in the effect processing sequence.
        /// </summary>
        [Tooltip("The maximum number of iterations in the effect processing sequence."), AdditionalProperty]
        public ClampedIntParameter maxIterations = new ClampedIntParameter(6, 2, 8);




        public override bool IsActive() => intensity.value > 0f;
    }


    [PostProcess("Bloom", PostProcessInjectionPoint.AfterRenderingPostProcessing)]
    public class BloomRenderer : PostProcessVolumeRenderer<Bloom>
    {
        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
        }
    }
}
