using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/Volumetric Light")]
    public class VolumetricLight : VolumeSetting
    {
        public VolumetricLight()
        {
            displayName = "体积光 (Volumetric Light)";
        }

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
        public ClampedIntParameter SampleCount = new ClampedIntParameter(8, 1, 64);
        public ClampedFloatParameter scatteringCoef = new ClampedFloatParameter(0.5f, 0f, 1f);
        public ClampedFloatParameter extinctionCoef = new ClampedFloatParameter(0.01f, 0f, 0.1f);
        public ClampedFloatParameter skyBackgroundExtinctionCoef = new ClampedFloatParameter(0.9f, 0f, 1f);
        public ClampedFloatParameter MieG = new ClampedFloatParameter(0.5f, 0.0f, 0.999f);
        public MinFloatParameter maxRayLength = new MinFloatParameter(400f, 0f);
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
            internal static readonly int Intensity = Shader.PropertyToID("_Intensity");
            internal static readonly int SampleCount = Shader.PropertyToID("_SampleCount");
            internal static readonly int MaxRayLength = Shader.PropertyToID("_MaxRayLength");
            internal static readonly int SkyboxExtinction = Shader.PropertyToID("_SkyboxExtinction");
            internal static readonly int ScatteringCoef = Shader.PropertyToID("_ScatteringCoef");
            internal static readonly int ExtinctionCoef = Shader.PropertyToID("_ExtinctionCoef");
            internal static readonly int MieG = Shader.PropertyToID("_MieG");
            internal static readonly int LightDirection = Shader.PropertyToID("_LightDirection");
            internal static readonly int LightTex = Shader.PropertyToID("_LightTex");
            internal static readonly int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");//实际使用的是half depth
        }


        Material m_Material;
        Material m_BlurMaterial;
        VisibleLight m_MainLight;
        RenderTextureDescriptor m_Descriptor;
        RenderTextureDescriptor m_DepthDescriptor;
        RTHandle m_VolumetricLightDownRT;
        RTHandle m_VolumetricLightRT;
        RTHandle m_HalfDepthRT;
        RTHandle m_TempRT;
        private VolumetricLightInclude m_VolumetricLightInclude;

        private Vector4[] frustumCorners = new Vector4[4];


        public override void Setup()
        {
            m_Material = GetMaterial(m_PostProcessFeatureData.shaders.volumetricLightPS);
            m_BlurMaterial = GetMaterial(m_PostProcessFeatureData.shaders.BilateralBlur);
        }

        private void SetupMaterials(ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;

            m_VolumetricLightInclude._Intensity = settings.intensity.value;
            m_VolumetricLightInclude._MaxRayLength = Mathf.Min(settings.maxRayLength.value, QualitySettings.shadowDistance);
            m_VolumetricLightInclude._SampleCount = settings.SampleCount.value;
            m_VolumetricLightInclude._ExtinctionCoef = settings.extinctionCoef.value;
            m_VolumetricLightInclude._ScatteringCoef = settings.scatteringCoef.value;
            m_VolumetricLightInclude._SkyboxExtinction = settings.skyBackgroundExtinctionCoef.value;
            m_VolumetricLightInclude._MieG = settings.MieG.value;


            frustumCorners[0] = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.farClipPlane));
            frustumCorners[2] = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.farClipPlane));
            frustumCorners[3] = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.farClipPlane));
            frustumCorners[1] = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.farClipPlane));

            m_Material.SetVectorArray("_FrustumCorners", frustumCorners);
            m_Material.SetFloat(ShaderConstants.Intensity, m_VolumetricLightInclude._Intensity);
            m_Material.SetFloat(ShaderConstants.MaxRayLength, m_VolumetricLightInclude._MaxRayLength);
            m_Material.SetInt(ShaderConstants.SampleCount, m_VolumetricLightInclude._SampleCount);
            m_Material.SetFloat(ShaderConstants.ScatteringCoef, m_VolumetricLightInclude._ScatteringCoef);
            m_Material.SetFloat(ShaderConstants.ExtinctionCoef, m_VolumetricLightInclude._ExtinctionCoef);
            m_Material.SetFloat(ShaderConstants.SkyboxExtinction, m_VolumetricLightInclude._SkyboxExtinction);
            m_Material.SetFloat(ShaderConstants.MieG, m_VolumetricLightInclude._MieG);

            SetupDirectionLight(ref renderingData);
        }

        void SetupDirectionLight(ref RenderingData renderingData)
        {
            if (renderingData.lightData.mainLightIndex == -1)
                return;

            m_MainLight = renderingData.cullResults.visibleLights[renderingData.lightData.mainLightIndex];
            m_Material.SetVector(ShaderConstants.LightDirection, m_MainLight.localToWorldMatrix.GetColumn(2));
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_Descriptor = renderingData.cameraData.cameraTargetDescriptor;
            m_Descriptor.msaaSamples = 1;
            m_Descriptor.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref m_VolumetricLightRT, m_Descriptor, FilterMode.Bilinear, name: "VolumetricLightRT");
            m_Material.SetTexture(ShaderConstants.LightTex, m_VolumetricLightRT);

            if ((int)settings.downSample.value > 1)
            {
                DescriptorDownSample(ref m_Descriptor, (int)settings.downSample.value);

                m_DepthDescriptor = m_Descriptor;
                m_DepthDescriptor.colorFormat = RenderTextureFormat.RFloat;

                RenderingUtils.ReAllocateIfNeeded(ref m_VolumetricLightDownRT, m_Descriptor, FilterMode.Bilinear, name: "VolumetricLightRT");
                RenderingUtils.ReAllocateIfNeeded(ref m_TempRT, m_Descriptor, FilterMode.Bilinear, name: "VolumetricLightTempRT");
                RenderingUtils.ReAllocateIfNeeded(ref m_HalfDepthRT, m_DepthDescriptor, FilterMode.Bilinear, name: "HalfDepthRT");
            }


        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(ref renderingData);



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
