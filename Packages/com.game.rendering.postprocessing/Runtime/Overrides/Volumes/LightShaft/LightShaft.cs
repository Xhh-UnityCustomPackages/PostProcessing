using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/后处理体积光 (Light Shaft)")]
    public class LightShaft : VolumeSetting
    {
        public LightShaft()
        {
            displayName = "后处理体积光 (Light Shaft)";
        }

        public enum Mode
        {
            Occlusion,
            Bloom
        }

        public enum DownSample
        {
            X1 = 1,
            X2 = 2,
            X3 = 3,
            X4 = 4,
            X5 = 5,
        }
        
        public BoolParameter enable = new (false);
        public EnumParameter<Mode> mode = new(Mode.Occlusion);
        public EnumParameter<DownSample> downSample = new(DownSample.X2);
        public ClampedFloatParameter density = new (2f, 0f, 2f);
        public ClampedFloatParameter weight = new (1f, 0f, 2f);
        public ClampedFloatParameter decay = new (1f, 0f, 2f);
        public ClampedFloatParameter exposure = new (0.5f, 0f, 2f);
        public ColorParameter bloomTintAndThreshold = new (Color.white);

        [Header("Radial Blur")]
        public ClampedFloatParameter radialBlurPower = new (0.6f, 0f, 1f);
        public ClampedFloatParameter radialBlurVectorMin = new (0.17f, 0f, 0.3f);

        public override bool IsActive()
        {
            return enable.value;
        }
    }


    [PostProcess("LightShaft", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public partial class LightShaftRenderer : PostProcessVolumeRenderer<LightShaft>
    {
        static class ShaderConstants
        {
            internal static readonly int lightSource = Shader.PropertyToID("_LightSource");
            internal static readonly int lightShaftParameters = Shader.PropertyToID("_LightShaftParameters");
            internal static readonly int radialBlurParameters = Shader.PropertyToID("_RadialBlurParameters");
            internal static readonly int lightShaftsDensity = Shader.PropertyToID("_ShaftsDensity");
            internal static readonly int lightShaftsWeight = Shader.PropertyToID("_ShaftsWeight");
            internal static readonly int lightShaftsDecay = Shader.PropertyToID("_ShaftsDecay");
            internal static readonly int lightShaftsExposure = Shader.PropertyToID("_ShaftsExposure");
            internal static readonly int bloomTintAndThreshold = Shader.PropertyToID("_BloomTintAndThreshold");
            internal static readonly int LightShafts1 = Shader.PropertyToID("_LightShafts1");
            internal static readonly int Atten = Shader.PropertyToID("_ShaftsAtten");
        }

        enum Pass
        {
            LightShaftsOcclusionPrefilter = 0,
            LightShaftsBloomPrefilter = 1,
            LightShaftsBlur = 2,
            LightShaftsOcclusionBlend = 3,
            LightShaftsBloomBlend = 4
        }


        private Material m_Material;
        private RenderTextureDescriptor m_Descriptor;
        private RTHandle m_LightShaftRT0;
        private RTHandle m_LightShaftRT1;
        private LightShaftInclude m_LightShaftInclude = new();

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.lightShaftPS);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_Descriptor = renderingData.cameraData.cameraTargetDescriptor;
            GetCompatibleDescriptor(ref m_Descriptor, m_Descriptor.graphicsFormat);

            if ((int)settings.downSample.value > 1)
            {
                DescriptorDownSample(ref m_Descriptor, (int)settings.downSample.value);
            }

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_LightShaftRT0, m_Descriptor, FilterMode.Bilinear, name: "_LightShaftRT0");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_LightShaftRT1, m_Descriptor, FilterMode.Bilinear, name: "_LightShaftRT1");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupDirectionLight(ref renderingData);
            SetupMaterials();

            if (Mathf.Approximately(m_LightShaftInclude._ShaftsAtten, 0))
            {
                //TODO 是否直接可以移除掉
                Blit(cmd, source, target);
                return;
            }

            if (settings.mode.value == LightShaft.Mode.Occlusion)
                Blit(cmd, source, m_LightShaftRT0, m_Material, (int)Pass.LightShaftsOcclusionPrefilter);
            else
                Blit(cmd, source, m_LightShaftRT0, m_Material, (int)Pass.LightShaftsBloomPrefilter);

            //do radial blur for 3 times
            RTHandle temp1 = m_LightShaftRT0;
            RTHandle temp2 = m_LightShaftRT1;

            for (int i = 0; i < 3; i++)
            {
                Blit(cmd, temp1, temp2, m_Material, (int)Pass.LightShaftsBlur);

                (temp2, temp1) = (temp1, temp2);
            }

            m_Material.SetTexture(ShaderConstants.LightShafts1, m_LightShaftRT1);
            if (settings.mode.value == LightShaft.Mode.Occlusion)
                Blit(cmd, source, target, m_Material, (int)Pass.LightShaftsOcclusionBlend);
            else
                Blit(cmd, source, target, m_Material, (int)Pass.LightShaftsBloomBlend);
        }


        private void SetupMaterials()
        {
            m_LightShaftInclude._LightShaftParameters = new Vector4(2.81f, 2.76f, 0, 0);
            m_LightShaftInclude._RadialBlurParameters = new Vector4(0f, settings.radialBlurPower.value, settings.radialBlurVectorMin.value, 0f);
            m_LightShaftInclude._ShaftsDensity = settings.density.value;
            m_LightShaftInclude._ShaftsWeight = settings.weight.value;
            m_LightShaftInclude._ShaftsDecay = settings.decay.value;
            m_LightShaftInclude._ShaftsExposure = settings.exposure.value;
            m_LightShaftInclude._BloomTintAndThreshold = settings.bloomTintAndThreshold.value;

            m_Material.SetVector(ShaderConstants.lightShaftParameters, m_LightShaftInclude._LightShaftParameters);
            m_Material.SetVector(ShaderConstants.radialBlurParameters, m_LightShaftInclude._RadialBlurParameters);
            m_Material.SetFloat(ShaderConstants.lightShaftsDensity, m_LightShaftInclude._ShaftsDensity);
            m_Material.SetFloat(ShaderConstants.lightShaftsWeight, m_LightShaftInclude._ShaftsWeight);
            m_Material.SetFloat(ShaderConstants.lightShaftsDecay, m_LightShaftInclude._ShaftsDecay);
            m_Material.SetFloat(ShaderConstants.lightShaftsExposure, m_LightShaftInclude._ShaftsExposure);
            m_Material.SetColor(ShaderConstants.bloomTintAndThreshold, m_LightShaftInclude._BloomTintAndThreshold);


        }

        private void SetupDirectionLight(ref RenderingData renderingData)
        {
            var lightData = renderingData.lightData;
            if (lightData.mainLightIndex == -1)
            {
                return;
            }

            var camera = renderingData.cameraData.camera;

            var mainLight = renderingData.cullResults.visibleLights[renderingData.lightData.mainLightIndex];
            var lightDir = -mainLight.localToWorldMatrix.GetColumn(2);
            Vector4 lightScreenPos = new Vector4(camera.transform.position.x, camera.transform.position.y, camera.transform.position.z, 0) + lightDir * camera.farClipPlane;
            lightScreenPos = camera.WorldToViewportPoint(lightScreenPos);

            m_LightShaftInclude._LightSource = new Vector4(lightScreenPos.x, lightScreenPos.y, lightScreenPos.z, 0);

            m_Material.SetVector(ShaderConstants.lightSource, m_LightShaftInclude._LightSource);

            Vector3 cameraDirWS = renderingData.cameraData.camera.transform.forward;
            float lightAtten = Mathf.Clamp(Vector3.Dot(lightDir, cameraDirWS), 0, 1);
            m_LightShaftInclude._ShaftsAtten = lightAtten;
            m_Material.SetFloat(ShaderConstants.Atten, m_LightShaftInclude._ShaftsAtten);
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);

            m_LightShaftRT0?.Release();
            m_LightShaftRT1?.Release();
        }
    }
}
