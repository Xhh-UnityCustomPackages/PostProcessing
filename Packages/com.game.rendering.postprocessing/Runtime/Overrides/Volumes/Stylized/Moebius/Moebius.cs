using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

        public override bool IsActive() => Enable.value;

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

        public BoolParameter Enable = new (false);
        public EnumParameter<SobelSource> sobelSource = new(SobelSource.Depth);
        [Tooltip("用于手绘效果的Noise")]
        public TextureParameter noise = new (null);
        public ClampedFloatParameter noiseIntensity = new (1f, 0f, 5f);

        [Header("Debug")]
        public EnumParameter<DebugMode> debugMode = new(DebugMode.Disabled);
    }


    [PostProcess("Moebius", PostProcessInjectionPoint.AfterRenderingPostProcessing, SupportRenderPath.Forward)]
    public partial class MoebiusRenderer : PostProcessVolumeRenderer<Moebius>
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
            var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<MoebiusResources>();
            m_Material = GetMaterial(runtimeResources.MoebiusPS);
        }

        public override ScriptableRenderPassInput input
        {
            get
            {
                if (settings.sobelSource.value == Moebius.SobelSource.Depth)
                    return ScriptableRenderPassInput.Depth;
                else
                {
                    return ScriptableRenderPassInput.Normal;
                }
            }
        }

        private void SetupMaterials(Camera camera, Material material)
        {
            if (material == null)
                return;
            float invFocalLenX = 1.0f / camera.projectionMatrix.m00;
            float invFocalLenY = 1.0f / camera.projectionMatrix.m11;

            material.SetVector(ShaderConstants.UVToView, new Vector4(2.0f * invFocalLenX, -2.0f * invFocalLenY, -1.0f * invFocalLenX, 1.0f * invFocalLenY));

            material.SetTexture(ShaderConstants.NoiseMap, settings.noise.value == null ? postProcessFeatureData.textures.blueNoiseTex : settings.noise.value);
            material.SetFloat(ShaderConstants.NoiseIntensity, settings.noiseIntensity.value);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            GetCompatibleDescriptor(ref desc, GraphicsFormat.B10G11R11_UFloatPack32);
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SobelResultRT, desc, name: "_SobelResultRT");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            if (m_Material == null)
                return;

            var camera = renderingData.cameraData.camera;
            SetupMaterials(camera, m_Material);

            // Step 1 Sobel Filter
            RTHandle sobelSourceTexture;
            if (settings.sobelSource.value == Moebius.SobelSource.Depth)
            {
                sobelSourceTexture = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            }
            else
            {
                // 从Depth创建出法线还是直接拿GBuffer的法线?
                //利用深度重建法线, 可以消除原始法线带来的一些不必要的高频信息, 有额外消耗 参考SSAO
                sobelSourceTexture = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            }
            Blit(cmd, sobelSourceTexture, m_SobelResultRT, m_Material, 0);

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
