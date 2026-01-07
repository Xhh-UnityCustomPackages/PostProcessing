using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Post-processing Custom/八位机效果 (Eight Color)")]
    public class EightColor : VolumeSetting
    {
        public static Color _Color1;
        public static Color _Color2;
        public static Color _Color3;
        public static Color _Color4;
        public static Color _Color5;
        public static Color _Color6;
        public static Color _Color7;
        public static Color _Color8;


        public BoolParameter enable = new(false);
        public ColorParameter Color1 = new(_Color1, false, false, false); //
        public ColorParameter Color2 = new(_Color2, false, false, false); //
        public ColorParameter Color3 = new(_Color3, false, false, false); //
        public ColorParameter Color4 = new(_Color4, false, false, false); //
        public ColorParameter Color5 = new(_Color5, false, false, false); //
        public ColorParameter Color6 = new(_Color6, false, false, false); //
        public ColorParameter Color7 = new(_Color7, false, false, false); //
        public ColorParameter Color8 = new(_Color8, false, false, false); //

        public ClampedFloatParameter Dithering = new(0.05f, 0, 1);
        public ClampedIntParameter Downsampling = new(1, 1, 32);
        public ClampedFloatParameter Opacity = new(1, 0, 1);

        public EightColor()
        {
            displayName = "八位机效果 (Eight Color)";

            ColorUtility.TryParseHtmlString("#2B0F54", out _Color1);
            ColorUtility.TryParseHtmlString("#AB1F65", out _Color2);
            ColorUtility.TryParseHtmlString("#FF4F69", out _Color3);
            ColorUtility.TryParseHtmlString("#FFF7F8", out _Color4);
            ColorUtility.TryParseHtmlString("#FF8142", out _Color5);
            ColorUtility.TryParseHtmlString("#FFDA45", out _Color6);
            ColorUtility.TryParseHtmlString("#3368DC", out _Color7);
            ColorUtility.TryParseHtmlString("#49E7EC", out _Color8);
        }

        public override bool IsActive()
        {
            return enable.value;
        }
    }

    [PostProcess("Eight Color", PostProcessInjectionPoint.AfterRenderingPostProcessing)]
    public partial class EightColorRenderer : PostProcessVolumeRenderer<EightColor>
    {
        private Material m_Material;

        public override void Setup()
        {
            var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<EightColorResources>();
            m_Material = GetMaterial(runtimeResources.EightColorPS);
        }

        private void SetupMaterials()
        {
            if (m_Material == null)
                return;

            var palette1 = new Matrix4x4(settings.Color1.value, settings.Color2.value, settings.Color3.value, settings.Color4.value);
            var palette2 = new Matrix4x4(settings.Color5.value, settings.Color6.value, settings.Color7.value, settings.Color8.value);

            m_Material.SetMatrix(ShaderConstants.Palette1, palette1.transpose);
            m_Material.SetMatrix(ShaderConstants.Palette2, palette2.transpose);
            m_Material.SetFloat(ShaderConstants.Dithering, settings.Dithering.value);
            m_Material.SetFloat(ShaderConstants.Downsampling, settings.Downsampling.value);
            m_Material.SetFloat(ShaderConstants.Opacity, settings.Opacity.value);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            if (m_Material == null)
                return;

            SetupMaterials();

            Blit(cmd, source, target, m_Material);
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
        }

        private static class ShaderConstants
        {
            internal static readonly int Dithering = Shader.PropertyToID("_Dithering");
            internal static readonly int Downsampling = Shader.PropertyToID("_Downsampling");
            internal static readonly int Opacity = Shader.PropertyToID("_Opacity");
            internal static readonly int Palette1 = Shader.PropertyToID("_Palette1");
            internal static readonly int Palette2 = Shader.PropertyToID("_Palette2");
        }
    }
}