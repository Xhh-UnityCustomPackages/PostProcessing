using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/体积光 (Volumetric Light)")]
    public class VolumetricLight : VolumeSetting
    {
        public VolumetricLight()
        {
            displayName = "体积光 (Volumetric Light)";
        }

        public enum DownSample
        {
            Full,
            Half,
        }

        [Serializable]
        public sealed class DownSampleParameter : VolumeParameter<DownSample> { }

        [Header("质量 (Quality)")]
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);
        public DownSampleParameter downSample = new DownSampleParameter() { value = DownSample.Full };
        public ClampedIntParameter SampleCount = new ClampedIntParameter(64, 1, 128);
        public MinFloatParameter maxRayLength = new MinFloatParameter(100f, 0f);

        [Header("散射 (Scattering)")]
        public ClampedFloatParameter scatteringCoef = new ClampedFloatParameter(0.5f, 0f, 1f);
        public ClampedFloatParameter extinctionCoef = new ClampedFloatParameter(0.01f, 0f, 0.5f);
        public ClampedFloatParameter skyBackgroundExtinctionCoef = new ClampedFloatParameter(0.9f, 0f, 1f);
        public ClampedFloatParameter MieG = new ClampedFloatParameter(0.5f, 0.0f, 0.999f);

        [Header("抖动 (Jitter)")]
        public BoolParameter useJitter = new BoolParameter(false);
        public Texture2DParameter jitterTex = new Texture2DParameter(null);

        [Header("噪音 (Noise)")]
        public BoolParameter useNoise = new BoolParameter(false);
        public Texture3DParameter noiseTex = new Texture3DParameter(null);
        public MinFloatParameter noiseIntensity = new MinFloatParameter(1f, 0f);
        public MinFloatParameter noiseScale = new MinFloatParameter(1f, 0f);
        public Vector2Parameter noiseOffset = new Vector2Parameter(Vector2.zero);
        public Vector3Parameter noiseVelocity = new Vector3Parameter(Vector2.one);

        [Space(10)]
        public BoolParameter debug = new BoolParameter(true);

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
            // Quality
            internal static readonly int Intensity = Shader.PropertyToID("_Intensity");
            internal static readonly int SampleCount = Shader.PropertyToID("_SampleCount");
            internal static readonly int MaxRayLength = Shader.PropertyToID("_MaxRayLength");
            // Scattering
            internal static readonly int SkyboxExtinction = Shader.PropertyToID("_SkyboxExtinction");
            internal static readonly int ScatteringCoef = Shader.PropertyToID("_ScatteringCoef");
            internal static readonly int ExtinctionCoef = Shader.PropertyToID("_ExtinctionCoef");
            internal static readonly int MieG = Shader.PropertyToID("_MieG");
            // Noise
            internal static readonly int NoiseTexture = Shader.PropertyToID("_NoiseTexture");
            internal static readonly int NoiseIntensity = Shader.PropertyToID("_NoiseIntensity");
            internal static readonly int NoiseScale = Shader.PropertyToID("_NoiseScale");
            internal static readonly int NoiseOffset = Shader.PropertyToID("_NoiseOffset");
            internal static readonly int NoiseVelocity = Shader.PropertyToID("_NoiseVelocity");

            internal static readonly int LightDirection = Shader.PropertyToID("_LightDirection");
            internal static readonly int LightColor = Shader.PropertyToID("_LightColor");
            internal static readonly int LightTex = Shader.PropertyToID("_LightTex");
            internal static readonly int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");//实际使用的是half depth
        }


        Material m_Material;
        RenderTextureDescriptor m_Descriptor;
        RenderTextureDescriptor m_DepthDescriptor;
        RTHandle m_VolumetricLightRT;
        RTHandle m_TempRT;
        private VolumetricLightInclude m_VolumetricLightInclude;

        RTHandle m_HalfDepthRT;

        private Vector4[] frustumCorners = new Vector4[4];


        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.volumetricLightPS);
        }

        private void SetupMaterials(ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;

            // Quality
            m_VolumetricLightInclude._Intensity = settings.intensity.value;
            m_VolumetricLightInclude._MaxRayLength = Mathf.Min(settings.maxRayLength.value, QualitySettings.shadowDistance);
            m_VolumetricLightInclude._SampleCount = settings.SampleCount.value;
            // 
            m_VolumetricLightInclude._ExtinctionCoef = settings.extinctionCoef.value;
            m_VolumetricLightInclude._ScatteringCoef = settings.scatteringCoef.value;
            m_VolumetricLightInclude._SkyboxExtinction = settings.skyBackgroundExtinctionCoef.value;
            m_VolumetricLightInclude._MieG = settings.MieG.value;
            // Noise
            m_VolumetricLightInclude._NoiseIntensity = settings.noiseIntensity.value;
            m_VolumetricLightInclude._NoiseScale = settings.noiseScale.value;
            m_VolumetricLightInclude._NoiseOffset = settings.noiseOffset.value;
            m_VolumetricLightInclude._NoiseVelocity = settings.noiseVelocity.value;


            var fov = camera.fieldOfView;
            var near = camera.nearClipPlane;
            var far = camera.farClipPlane;
            var aspect = camera.aspect;

            var halfHeight = far * Mathf.Tan(fov / 2 * Mathf.Deg2Rad);
            var toRight = camera.transform.right * halfHeight * aspect;
            var toTop = camera.transform.up * halfHeight;
            var toForward = camera.transform.forward * far;

            var topLeft = toForward + toTop - toRight;
            var topRight = toForward + toTop + toRight;
            var bottomLeft = toForward - toTop - toRight;
            var bottomRight = toForward - toTop + toRight;

            frustumCorners[0] = bottomLeft;
            frustumCorners[1] = topLeft;
            frustumCorners[2] = bottomRight;
            frustumCorners[3] = topRight;

            // frustumCorners[0] = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.farClipPlane));
            // frustumCorners[2] = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.farClipPlane));
            // frustumCorners[3] = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.farClipPlane));
            // frustumCorners[1] = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.farClipPlane));

            CoreUtils.SetKeyword(m_Material, "_NOISE", settings.useNoise.value);
            if (settings.useNoise.value)
            {
                m_Material.SetTexture(ShaderConstants.NoiseTexture, settings.noiseTex.value);
                m_Material.SetFloat(ShaderConstants.NoiseIntensity, m_VolumetricLightInclude._NoiseIntensity);
                m_Material.SetFloat(ShaderConstants.NoiseScale, m_VolumetricLightInclude._NoiseScale);
                m_Material.SetVector(ShaderConstants.NoiseOffset, m_VolumetricLightInclude._NoiseOffset);
                m_Material.SetVector(ShaderConstants.NoiseVelocity, m_VolumetricLightInclude._NoiseVelocity);
            }

            CoreUtils.SetKeyword(m_Material, "_JITTER", settings.useJitter.value);
            if (settings.useJitter.value)
                m_Material.SetTexture("_DitherTexture", postProcessFeatureData.textures.DitherTexture);

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

            var mainLight = renderingData.cullResults.visibleLights[renderingData.lightData.mainLightIndex];
            m_Material.SetVector(ShaderConstants.LightDirection, -mainLight.localToWorldMatrix.GetColumn(2));
            m_Material.SetVector(ShaderConstants.LightColor, mainLight.finalColor);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_Descriptor = renderingData.cameraData.cameraTargetDescriptor;
            m_Descriptor.msaaSamples = 1;
            m_Descriptor.depthBufferBits = 0;

            m_DepthDescriptor = m_Descriptor;


            if (settings.downSample.value == VolumetricLight.DownSample.Half)
            {
                DescriptorDownSample(ref m_Descriptor, 2);
            }

            RenderingUtils.ReAllocateIfNeeded(ref m_VolumetricLightRT, m_Descriptor, FilterMode.Bilinear, name: "_VolumetricLightRT");
            RenderingUtils.ReAllocateIfNeeded(ref m_TempRT, m_Descriptor, FilterMode.Bilinear, name: "_VolumetricLightTempRT");


            m_DepthDescriptor.colorFormat = RenderTextureFormat.RFloat;
            DescriptorDownSample(ref m_DepthDescriptor, 2);
            RenderingUtils.ReAllocateIfNeeded(ref m_HalfDepthRT, m_DepthDescriptor, FilterMode.Bilinear, name: "_HalfDepthRT");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(ref renderingData);

            //计算体积光
            Blit(cmd, source, m_VolumetricLightRT, m_Material, 0);

            //模糊 
            if (settings.downSample.value == VolumetricLight.DownSample.Half)
            {
                cmd.BeginSample("Bilateral Blur");
                // 得到Half Depth Texture
                Blit(cmd, m_HalfDepthRT, m_HalfDepthRT, postProcessFeatureData.materials.BilateralBlurMaterial, 4);

                postProcessFeatureData.materials.BilateralBlurMaterial.SetTexture("_SourceTex", m_VolumetricLightRT);
                postProcessFeatureData.materials.BilateralBlurMaterial.SetTexture("_QuarterResDepthBuffer", m_HalfDepthRT);
                Blit(cmd, m_VolumetricLightRT, m_TempRT, postProcessFeatureData.materials.BilateralBlurMaterial, 2);
                Blit(cmd, m_TempRT, m_VolumetricLightRT, postProcessFeatureData.materials.BilateralBlurMaterial, 3);
                Blit(cmd, m_VolumetricLightRT, m_VolumetricLightRT, postProcessFeatureData.materials.BilateralBlurMaterial, 5);
                cmd.EndSample("Bilateral Blur");
            }


            m_Material.SetTexture(ShaderConstants.LightTex, m_VolumetricLightRT);

            // 合并
            if (settings.debug.value)
                Blit(cmd, target, target, m_Material, 1);
            else
                Blit(cmd, source, target, m_Material, 1);


            // Blit(cmd, m_VolumetricLightRT, target);

        }

        public void Dispose()
        {
            m_VolumetricLightRT?.Release();
            m_TempRT?.Release();
        }
    }
}
