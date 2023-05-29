using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Post-processing Custom/Volumetric Light", typeof(UniversalRenderPipeline))]
    public class VolumetricLight : VolumeSetting
    {
        public enum DownSample
        {
            X1 = 1,
            X2 = 2,
            X3 = 3,
            X4 = 4,
            X8 = 8,
            X16 = 16
        }

        [Serializable]
        public sealed class DownSampleParameter : VolumeParameter<DownSample> { }

        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);
        public DownSampleParameter downSample = new DownSampleParameter() { value = DownSample.X2 };
        public ClampedIntParameter SampleCount = new ClampedIntParameter(128, 1, 1024);
        public ClampedFloatParameter scatterDensity = new ClampedFloatParameter(1, 0, 3);
        public ClampedFloatParameter MieG = new ClampedFloatParameter(0.5f, 0.0f, 0.999f);
        public MinFloatParameter maxRayLength = new MinFloatParameter(50f, 0f);
        public BoolParameter debug = new BoolParameter(false);
        public BoolParameter jitter = new BoolParameter(false);

        public override bool IsActive()
        {
            return intensity.value > 0;
        }
    }

    [PostProcess("VolumetricLight", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class VolumetricLightRenderer : PostProcessVolumeRenderer<VolumetricLight>
    {
        static class ShaderConstants
        {
            // internal static readonly int InvVP = Shader.PropertyToID("_InvVP");
            internal static readonly int MaxRayLength = Shader.PropertyToID("_MaxRayLength");
            internal static readonly int Density = Shader.PropertyToID("_Density");
            internal static readonly int RandomNumber = Shader.PropertyToID("_RandomNumber");
            internal static readonly int MieG = Shader.PropertyToID("_MieG");
            internal static readonly int Intensity = Shader.PropertyToID("_Intensity");
            internal static readonly int SampleCount = Shader.PropertyToID("_SampleCount");
            internal static readonly int JitterOffset = Shader.PropertyToID("_JitterOffset");
            internal static readonly int LightTex = Shader.PropertyToID("_LightTex");
            internal static readonly int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");//实际使用的是half depth
        }


        Material m_Material;
        Material m_BlurMaterial;
        Light m_MainLight;
        RenderTextureDescriptor m_Descriptor;
        RenderTextureDescriptor m_DepthDescriptor;
        RTHandle m_VolumetricLightDownRT;
        RTHandle m_VolumetricLightRT;
        RTHandle m_HalfDepthRT;
        RTHandle m_TempRT;


        public override void Setup()
        {
            m_Material = GetMaterial(m_PostProcessFeatureData.shaders.volumetricLightPS);
            m_BlurMaterial = GetMaterial(m_PostProcessFeatureData.shaders.BilateralBlur);
        }

        private void SetupMaterials(ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            // m_Material.SetMatrix(ShaderConstants.InvVP, (renderingData.cameraData.GetGPUProjectionMatrix() * camera.worldToCameraMatrix).inverse);
            float maxRayLength = Mathf.Min(settings.maxRayLength.value, QualitySettings.shadowDistance);
            m_Material.SetFloat(ShaderConstants.MaxRayLength, maxRayLength);
            m_Material.SetInt(ShaderConstants.SampleCount, settings.SampleCount.value);
            m_Material.SetFloat(ShaderConstants.Density, settings.scatterDensity.value);
            m_Material.SetVector(ShaderConstants.RandomNumber, new Vector2(UnityEngine.Random.Range(0f, 1000f), Vector3.Dot(Vector3.Cross(camera.transform.position, camera.transform.eulerAngles), Vector3.one)));
            float MieG = settings.MieG.value;
            m_Material.SetVector(ShaderConstants.MieG, new Vector4(1 - (MieG * MieG), 1 + (MieG * MieG), 2 * MieG, 1.0f / (4.0f * Mathf.PI)));
            m_Material.SetFloat(ShaderConstants.Intensity, settings.intensity.value);
            SetupDirectionLight(ref renderingData);

            if (settings.jitter.value)
            {
                Vector2 jitter;
                jitter.x = 0.1f / camera.pixelWidth;
                jitter.y = 0.1f / camera.pixelHeight;
                m_Material.SetVector(ShaderConstants.JitterOffset, jitter);
            }
            else
            {
                m_Material.SetVector(ShaderConstants.JitterOffset, Vector2.zero);
            }

        }

        void SetupDirectionLight(ref RenderingData renderingData)
        {
            if (renderingData.lightData.mainLightIndex == -1)
                return;
            var mainlight = renderingData.cullResults.visibleLights[renderingData.lightData.mainLightIndex];

            var light = mainlight.light;

            m_MainLight = light;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_Descriptor = renderingData.cameraData.cameraTargetDescriptor;
            m_Descriptor.msaaSamples = 1;
            m_Descriptor.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref m_VolumetricLightRT, m_Descriptor, FilterMode.Bilinear, name: "VolumetricLightRT");

            DescriptorDownSample(ref m_Descriptor, (int)settings.downSample.value);

            m_DepthDescriptor = m_Descriptor;
            m_DepthDescriptor.colorFormat = RenderTextureFormat.RFloat;

            RenderingUtils.ReAllocateIfNeeded(ref m_VolumetricLightDownRT, m_Descriptor, FilterMode.Bilinear, name: "VolumetricLightRT");
            RenderingUtils.ReAllocateIfNeeded(ref m_TempRT, m_Descriptor, FilterMode.Bilinear, name: "VolumetricLightTempRT");
            RenderingUtils.ReAllocateIfNeeded(ref m_HalfDepthRT, m_DepthDescriptor, FilterMode.Bilinear, name: "HalfDepthRT");

            m_Material.SetTexture(ShaderConstants.LightTex, m_VolumetricLightRT);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(ref renderingData);

            if (m_MainLight != null && m_MainLight.shadows != LightShadows.None)
            {
                CoreUtils.SetKeyword(m_Material, "SHADOWS_DEPTH_ON", true);
            }

            if ((int)settings.downSample.value > 1)
            {
                //half depth
                Blit(cmd, null, m_HalfDepthRT, m_BlurMaterial, 4);

                //计算体积光
                m_Material.SetTexture(ShaderConstants.CameraDepthTexture, m_HalfDepthRT);
                Blit(cmd, source, m_VolumetricLightDownRT, m_Material, 0);

                //模糊 需要使用联合双边 模糊 来保证边缘
                m_BlurMaterial.SetTexture("_SourceTex", m_HalfDepthRT);
                Blit(cmd, m_VolumetricLightDownRT, m_HalfDepthRT, m_BlurMaterial, 2);
                Blit(cmd, m_VolumetricLightDownRT, m_TempRT, m_BlurMaterial, 3);

                m_BlurMaterial.SetTexture("_HalfResColor", m_VolumetricLightDownRT);
                Blit(cmd, null, m_VolumetricLightRT, m_BlurMaterial, 5);


                //暂时使用 高斯模糊
                // Blit(cmd, m_VolumetricLightRT, m_TempRT, m_Material, 1);
                // Blit(cmd, m_TempRT, m_VolumetricLightRT, m_Material, 2);
            }
            else
            {
                Blit(cmd, source, m_VolumetricLightRT, m_Material, 0);
            }


            //合并
            if (settings.debug.value)
                Blit(cmd, null, target, m_Material, 3);
            else
                Blit(cmd, source, target, m_Material, 3);

        }

        public void Dispose()
        {
            m_VolumetricLightDownRT?.Release();
            m_VolumetricLightRT?.Release();
            m_TempRT?.Release();
        }
    }
}
