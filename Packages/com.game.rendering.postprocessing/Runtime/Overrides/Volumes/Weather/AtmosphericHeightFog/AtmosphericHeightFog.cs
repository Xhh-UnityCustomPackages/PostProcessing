using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/大气高度雾 (Atmospheric Height Fog)")]
    public class AtmosphericHeightFog : VolumeSetting
    {
        public AtmosphericHeightFog()
        {
            displayName = "大气高度雾 (Atmospheric Height Fog)";
        }

        public enum DirectionalFrom
        {
            MainLight,
            Custom
        }
        
        public BoolParameter Enable = new BoolParameter(false);

        [Header("Fog")]
        public ClampedFloatParameter fogIntensity = new ClampedFloatParameter(1f, 0f, 1f);
        public ColorParameter fogColorStart = new ColorParameter(new Color(0.5f, 0.75f, 1.0f, 1.0f), hdr: true, showAlpha: false, showEyeDropper: true);
        public ColorParameter fogColorEnd = new ColorParameter(new Color(0.75f, 1f, 1.25f, 1.0f), hdr: true, showAlpha: false, showEyeDropper: true);
        public ClampedFloatParameter fogColorDuo = new ClampedFloatParameter(0f, 0f, 1f);

        [Space(10)]
        public FloatParameter fogDistanceStart = new FloatParameter(-100);
        public FloatParameter fogDistanceEnd = new FloatParameter(100);
        public ClampedFloatParameter fogDistanceFalloff = new ClampedFloatParameter(1f, 1f, 8f);

        [Space(10)]
        public FloatParameter fogHeightStart = new FloatParameter(0);
        public FloatParameter fogHeightEnd = new FloatParameter(100);
        public ClampedFloatParameter fogHeightFalloff = new ClampedFloatParameter(1f, 1f, 8f);


        [Header("Skybox")]
        public ClampedFloatParameter skyboxFogIntensity = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter skyboxFogHeight = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter skyboxFogFalloff = new ClampedFloatParameter(1f, 1f, 8f);
        public ClampedFloatParameter skyboxFogOffset = new ClampedFloatParameter(0f, -1f, 1f);
        public ClampedFloatParameter skyboxFogFill = new ClampedFloatParameter(0f, 0f, 1f);


        [Header("Directional")]
        public EnumParameter<DirectionalFrom> directionalFrom = new(DirectionalFrom.MainLight);
        public Vector3Parameter customDirectionalDirection = new Vector3Parameter(Vector3.zero);
        public ClampedFloatParameter directionalIntensity = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter directionalFalloff = new ClampedFloatParameter(1f, 1f, 8f);
        public ColorParameter directionalColor = new ColorParameter(new Color(1f, 0.75f, 0.5f, 1f), hdr: true, showAlpha: false, showEyeDropper: true);


        [Header("Noise")]
        public ClampedFloatParameter noiseIntensity = new ClampedFloatParameter(1f, 0f, 1f);
        public FloatParameter noiseDistanceEnd = new FloatParameter(50);
        public FloatParameter noiseScale = new FloatParameter(30);
        public Vector3Parameter noiseSpeed = new Vector3Parameter(new Vector3(0.5f, 0f, 0.5f));

        public override bool IsActive() => Enable.value && fogIntensity.value > 0f;
    }


    [PostProcess("AtmosphericHeightFog", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public partial class AtmosphericHeightFogRenderer : PostProcessVolumeRenderer<AtmosphericHeightFog>
    {
        static class ShaderConstants
        {
            internal static readonly int FogIntensity = Shader.PropertyToID("_FogIntensity");
            internal static readonly int DistanceParam = Shader.PropertyToID("_DistanceParam");
            internal static readonly int FogColorStart = Shader.PropertyToID("_FogColorStart");
            internal static readonly int FogColorEnd = Shader.PropertyToID("_FogColorEnd");
            internal static readonly int DirectionalDir = Shader.PropertyToID("_DirectionalDir");
            internal static readonly int DirectionalParam = Shader.PropertyToID("_DirectionalParam");
            internal static readonly int DirectionalColor = Shader.PropertyToID("_DirectionalColor");
            internal static readonly int HeightParam = Shader.PropertyToID("_HeightParam");
            internal static readonly int SkyboxParam1 = Shader.PropertyToID("_SkyboxParam1");
            internal static readonly int SkyboxParam2 = Shader.PropertyToID("_SkyboxParam2");
        }
        private Material m_GlobalMaterial;

        protected override void Setup()
        {
            var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<AtmosphericHeightFogResources>();
            m_GlobalMaterial = GetMaterial(runtimeResources.atmosphericHeightFogPS);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            SetupMaterial(ref renderingData);

            Blit(cmd, source, destination, m_GlobalMaterial, 0);
        }


        void SetupMaterial(ref RenderingData renderingData)
        {
            m_GlobalMaterial.SetFloat(ShaderConstants.FogIntensity, settings.fogIntensity.value);
            m_GlobalMaterial.SetVector(ShaderConstants.DistanceParam, new Vector4(settings.fogDistanceStart.value, settings.fogDistanceEnd.value, settings.fogDistanceFalloff.value, settings.fogColorDuo.value));
            m_GlobalMaterial.SetColor(ShaderConstants.FogColorStart, settings.fogColorStart.value);
            m_GlobalMaterial.SetColor(ShaderConstants.FogColorEnd, settings.fogColorEnd.value);

            if (settings.directionalFrom == AtmosphericHeightFog.DirectionalFrom.MainLight)
            {
                int mainLightIndex = renderingData.lightData.mainLightIndex;
                if (mainLightIndex != -1)
                {
                    var mainLight = renderingData.lightData.visibleLights[mainLightIndex];
                    m_GlobalMaterial.SetVector(ShaderConstants.DirectionalDir, -mainLight.localToWorldMatrix.GetColumn(2));
                }
            }
            else
            {
                m_GlobalMaterial.SetVector(ShaderConstants.DirectionalDir, settings.customDirectionalDirection.value);
            }
            m_GlobalMaterial.SetVector(ShaderConstants.DirectionalParam, new Vector4(settings.directionalIntensity.value, settings.directionalFalloff.value, 0, 0));
            m_GlobalMaterial.SetColor(ShaderConstants.DirectionalColor, settings.directionalColor.value);

            m_GlobalMaterial.SetVector(ShaderConstants.HeightParam, new Vector4(settings.fogHeightStart.value, settings.fogHeightEnd.value, settings.fogHeightFalloff.value, 0));

            m_GlobalMaterial.SetVector(ShaderConstants.SkyboxParam1, new Vector4(settings.skyboxFogIntensity.value, settings.skyboxFogHeight.value, settings.skyboxFogFalloff.value, settings.skyboxFogOffset.value));
            m_GlobalMaterial.SetVector(ShaderConstants.SkyboxParam2, new Vector4(settings.skyboxFogFill.value, 0, 0, 0));

        }
    }
}
