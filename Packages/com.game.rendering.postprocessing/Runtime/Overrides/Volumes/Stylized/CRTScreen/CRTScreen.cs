using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //https://zhuanlan.zhihu.com/p/436954775
    [Serializable, VolumeComponentMenu("Post-processing Custom/风格化 (Stylized)/CRT Screen")]
    public class CRTScreen : VolumeSetting
    {
        public CRTScreen()
        {
            displayName = "CRT Screen";
        }

        public override bool IsActive() => true;

        [Header("屏幕扭曲")]
        public Vector2Parameter Curvature = new Vector2Parameter(Vector2.one);
        [Header("屏幕扫描线")]
        public Vector2Parameter Resolution = new Vector2Parameter(Vector2.one * 480);
        public Vector4Parameter PexelScanlineBrightness = new Vector4Parameter(new Vector4(0.225f, 0.85f, 0.05f, 0.95f));
        [Header("RGB分离")]
        public ClampedFloatParameter RGBSplitOffset = new ClampedFloatParameter(0.003f, 0.001f, 0.01f);
        [Header("暗角")]
        public ClampedFloatParameter VignetteIntensity = new ClampedFloatParameter(0.3f, 0, 1);
        public MinFloatParameter VignetteBrightness = new MinFloatParameter(16f, 0);

        // public ColorParameter baseColor = new ColorParameter(Color.black, hdr: false, showAlpha: false, showEyeDropper: true);
    }

    [PostProcess("CRTScreen", PostProcessInjectionPoint.AfterRenderingPostProcessing)]
    public class CRTScreenRenderer : PostProcessVolumeRenderer<CRTScreen>
    {
        static class ShaderConstants
        {
            public static readonly int _Curvature = Shader.PropertyToID("_Curvature");
            public static readonly int _Resolution = Shader.PropertyToID("_Resolution");
            public static readonly int _PexelScanlineBrightness = Shader.PropertyToID("_PexelScanlineBrightness");
            public static readonly int _RGBSplitOffset = Shader.PropertyToID("_RGBSplitOffset");
            public static readonly int _VignetteParam = Shader.PropertyToID("_VignetteParam");
        }

        private Material m_Material;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.CRTScreenPS);
        }

        private void SetupMaterials(ref RenderingData renderingData, Material material)
        {
            if (material == null)
                return;

            material.SetVector(ShaderConstants._Curvature, settings.Curvature.value);
            material.SetVector(ShaderConstants._Resolution, settings.Resolution.value);
            material.SetVector(ShaderConstants._PexelScanlineBrightness, settings.PexelScanlineBrightness.value);
            material.SetFloat(ShaderConstants._RGBSplitOffset, settings.RGBSplitOffset.value);
            material.SetVector(ShaderConstants._VignetteParam, new Vector4(settings.VignetteIntensity.value, settings.VignetteBrightness.value, 0, 0));
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            if (m_Material == null)
                return;

            SetupMaterials(ref renderingData, m_Material);

            Blit(cmd, source, destination, m_Material);

        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
        }
    }
}
