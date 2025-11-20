using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/暗角 (Vignette)")]
    public class Vignette : VolumeSetting
    {
        public Vignette()
        {
            displayName = "暗角 (Vignette)";
        }
        
        /// <summary>
        /// Specifies the color of the vignette.
        /// </summary>
        [Tooltip("Vignette color.")]
        public ColorParameter color = new ColorParameter(Color.black, false, false, true);

        /// <summary>
        /// Sets the center point for the vignette.
        /// </summary>
        [Tooltip("Sets the vignette center point (screen center is [0.5,0.5]).")]
        public Vector2Parameter center = new Vector2Parameter(new Vector2(0.5f, 0.5f));

        /// <summary>
        /// Controls the strength of the vignette effect.
        /// </summary>
        [Tooltip("Use the slider to set the strength of the Vignette effect.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        /// <summary>
        /// Controls the smoothness of the vignette borders.
        /// </summary>
        [Tooltip("Smoothness of the vignette borders.")]
        public ClampedFloatParameter smoothness = new ClampedFloatParameter(0.2f, 0.01f, 1f);

        /// <summary>
        /// Controls how round the vignette is, lower values result in a more square vignette.
        /// </summary>
        [Tooltip("Should the vignette be perfectly round or be dependent on the current aspect ratio?")]
        public BoolParameter rounded = new BoolParameter(false);


        public override bool IsActive() => false;
    }

    // [PostProcess("Vignette", PostProcessInjectionPoint.AfterRenderingPostProcessing)]
    // public partial class VignetteRenderer : PostProcessVolumeRenderer<Vignette>
    // {
    //     static class ShaderConstants
    //     {
    //     }
    //
    //     public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
    //     {
    //     }
    // }

    static class VignetteRenderer
    {
        public static class ShaderConstants
        {
            public static readonly int _Vignette_Params1 = Shader.PropertyToID("_Vignette_Params1");
            public static readonly int _Vignette_Params2 = Shader.PropertyToID("_Vignette_Params2");
        }

        static public void ExecutePass(CommandBuffer cmd, RenderTextureDescriptor desc, Material material, Vignette settings)
        {
            var color = settings.color.value;
            var center = settings.center.value;
            var aspectRatio = desc.width / (float)desc.height;
            
            var v1 = new Vector4(
                color.r, color.g, color.b,
                settings.rounded.value ? aspectRatio : 1f
            );
            var v2 = new Vector4(
                center.x, center.y,
                settings.intensity.value * 3f,
                settings.smoothness.value * 5f
            );
            material.SetVector(ShaderConstants._Vignette_Params1, v1);
            material.SetVector(ShaderConstants._Vignette_Params2, v2);

        }
    }
}
