using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //https://www.bilibili.com/video/BV1sN41147sP/?spm_id_from=333.788.recommend_more_video.2&vd_source=489c2e01ca37c86552ed1b83386b1ea6
    [Serializable, VolumeComponentMenu("Post-processing Custom/风格化 (Stylized)/莫比斯 (Moebius)")]
    public class Moebius : VolumeSetting
    {
        public Moebius()
        {
            displayName = "莫比斯 (Moebius)";
        }

        public override bool IsActive() => true;

        public enum SobelSource
        {
            Depth,
            Normal
        }

        public enum DebugMode
        {
            Disabled,
            Sobel,
            Normal,
        }

        [Serializable] public sealed class SobelSourceParameter : VolumeParameter<SobelSource> { public SobelSourceParameter(SobelSource value, bool overrideState = false) : base(value, overrideState) { } }
        [Serializable] public sealed class DebugModeParameter : VolumeParameter<DebugMode> { public DebugModeParameter(DebugMode value, bool overrideState = false) : base(value, overrideState) { } }


        public SobelSourceParameter sobelSource = new SobelSourceParameter(SobelSource.Depth);
        [Tooltip("用于手绘效果的Noise")]
        public TextureParameter noise = new TextureParameter(null);
        public ClampedFloatParameter noiseIntensity = new ClampedFloatParameter(1f, 0f, 5f);

        [Header("Debug")]
        public DebugModeParameter debugMode = new DebugModeParameter(DebugMode.Disabled);
    }


    [PostProcess("Moebius", PostProcessInjectionPoint.AfterRenderingPostProcessing)]
    public class MoebiusRenderer : PostProcessVolumeRenderer<Moebius>
    {
        static class ShaderConstants
        {
            internal static readonly int UVToView = Shader.PropertyToID("_UVToView");
            internal static readonly int SobelResultRT = Shader.PropertyToID("_SobelResultRT");
            internal static readonly int NoiseMap = Shader.PropertyToID("_NoiseMap");
            internal static readonly int NoiseIntensity = Shader.PropertyToID("_NoiseIntensity");
        }

        private Material m_Material;
        private RTHandle m_SobelResultRT;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.MoebiusPS);
        }

        private void SetupMaterials(ref RenderingData renderingData, Material material)
        {
            if (material == null)
                return;
            var cameraData = renderingData.cameraData;
            float invFocalLenX = 1.0f / cameraData.camera.projectionMatrix.m00;
            float invFocalLenY = 1.0f / cameraData.camera.projectionMatrix.m11;

            material.SetVector(ShaderConstants.UVToView, new Vector4(2.0f * invFocalLenX, -2.0f * invFocalLenY, -1.0f * invFocalLenX, 1.0f * invFocalLenY));

            material.SetTexture(ShaderConstants.NoiseMap, settings.noise.value == null ? postProcessFeatureData.textures.blueNoiseTex : settings.noise.value);
            material.SetFloat(ShaderConstants.NoiseIntensity, settings.noiseIntensity.value);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.ARGB32;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_SobelResultRT, desc, name: "_SobelResultRT");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            if (m_Material == null)
                return;

            SetupMaterials(ref renderingData, m_Material);

            // Step 1 Sobel Filter
            if (settings.sobelSource.value == Moebius.SobelSource.Depth)
            {
                var depthRT = renderingData.cameraData.renderer.cameraDepthTargetHandle;
                Blit(cmd, depthRT, m_SobelResultRT, m_Material, 0);
            }
            else
            {
                // 从Depth创建出法线还是直接拿GBuffer的法线?
                //利用深度重建法线, 可以消除原始法线带来的一些不必要的高频信息, 有额外消耗 参考SSAO
                var depthRT = renderingData.cameraData.renderer.cameraDepthTargetHandle;
                Blit(cmd, depthRT, m_SobelResultRT, m_Material, 0);
            }

            #region Debug
            if (settings.debugMode.value == Moebius.DebugMode.Sobel)
            {
                Blit(cmd, m_SobelResultRT, destination);
                return;
            }
            else if (settings.debugMode.value == Moebius.DebugMode.Normal)
            {
                Blit(cmd, m_SobelResultRT, destination);
                return;
            }
            #endregion

            //合并
            m_Material.SetTexture(ShaderConstants.SobelResultRT, m_SobelResultRT);
            Blit(cmd, source, destination, m_Material, 2);
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            m_SobelResultRT?.Release();
        }
    }
}
