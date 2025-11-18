using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/体积云 (Volumetric Cloud)")]
    public class VolumetricCloud : VolumeSetting
    {
        public VolumetricCloud()
        {
            displayName = "体积云 (Volumetric Cloud)";
        }

        [Serializable]
        public class GradientParameter : VolumeParameter<Gradient> { }
        [Header("Base")]
        public BoolParameter opened = new BoolParameter(false);
        public BoolParameter useBlur = new BoolParameter(false);
        public ClampedIntParameter blurDownSample = new ClampedIntParameter(1, 1, 4, false);
        public BoolParameter allowInClouds = new BoolParameter(false);
        public ClampedIntParameter steps = new ClampedIntParameter(128, 1, 256);
        [Header("Lighting")]
        public ColorParameter cloudBaseColor = new ColorParameter(new Color32(199, 220, 255, 255), false);
        public ColorParameter cloudTopColor = new ColorParameter(new Color32(255, 255, 255, 255), false);
        public ClampedFloatParameter ambientLightFactor = new ClampedFloatParameter(0.551f, 0, 1, false);
        public ClampedFloatParameter sunLightFactor = new ClampedFloatParameter(0.79f, 0, 1.5f, false);
        public ClampedFloatParameter lightCartoon = new ClampedFloatParameter(0, 0, 0.5f, false);

        public BoolParameter randomUnitSphere = new BoolParameter(true, false);
        public ClampedFloatParameter lightStepLength = new ClampedFloatParameter(64.0f, 0, 200, false);
        public ClampedFloatParameter lightConeRadius = new ClampedFloatParameter(0.4f, 0, 1, false);
        public ClampedFloatParameter henyeyGreensteinGForward = new ClampedFloatParameter(0.4f, 0, 1, false);
        public ClampedFloatParameter henyeyGreensteinGBackward = new ClampedFloatParameter(0.179f, 0, 1, false);
        public ClampedFloatParameter u_SilverIntensity = new ClampedFloatParameter(1, 0, 1, false);
        public ColorParameter mainLightColor = new ColorParameter(Color.white, false);
        public ColorParameter silverColor = new ColorParameter(Color.white, false);


        [Header("Base Shape")]
        public FloatParameter cloudNoiseScale = new FloatParameter(1f, false);
        public ClampedFloatParameter detailNoiseScale = new ClampedFloatParameter(13.9f, 0, 32, false);
        public ClampedFloatParameter curlDistortScale = new ClampedFloatParameter(7.44f, 0, 10, false);
        public ClampedFloatParameter curlDistortAmount = new ClampedFloatParameter(407.0f, 0, 1000, false);
        public Texture3DParameter cloudShapeTex = new Texture3DParameter(null, false);
        public Texture3DParameter cloudDetailShapeTex = new Texture3DParameter(null, false);
        public TextureParameter curlNoiseTex = new TextureParameter(null, false);
        public GradientParameter gradientLow = new GradientParameter();
        public GradientParameter gradientMed = new GradientParameter();
        public GradientParameter gradientHigh = new GradientParameter();
        public ClampedFloatParameter lowFreqMin = new ClampedFloatParameter(0.366f, 0, 1, false);
        public ClampedFloatParameter lowFreqMax = new ClampedFloatParameter(0.8f, 0, 1, false);
        public ClampedFloatParameter highFreqModifier = new ClampedFloatParameter(0.21f, 0, 1, false);
        public FloatParameter cloudSampleMultiplier = new FloatParameter(1, false);

        [Header("Alto Cloud")]
        public TextureParameter altoCloudsTex = new TextureParameter(null, false);
        public ClampedFloatParameter altoCloudIntensity = new ClampedFloatParameter(1, 0, 1, false);
        public FloatParameter highCloudsWindDirection = new FloatParameter(77.8f, false);
        public FloatParameter highCloudsWindSpeed = new FloatParameter(49.2f, false);
        public ClampedFloatParameter highCloudsScale = new ClampedFloatParameter(0.5f, 0, 1, false);
        public ClampedFloatParameter highCoverageScale = new ClampedFloatParameter(1, 0, 2, false);
        public ClampedFloatParameter coverageHigh = new ClampedFloatParameter(1, 0, 2, false);
        public ClampedFloatParameter altoCloudAlphaPow = new ClampedFloatParameter(1, 1, 5, false);

        [Header("Weather")]
        public ClampedFloatParameter coverage = new ClampedFloatParameter(0.92f, 0, 2, false);
        // r: 云形状分布(0的地方挖洞), g: 云类型, b: 云光照厚度(0是薄, 1是厚)
        public TextureParameter weatherTexture = new TextureParameter(null, false);
        public ClampedFloatParameter weatherScale = new ClampedFloatParameter(0.1f, 0, 1, false);
        public FloatParameter coverageWindDirection = new FloatParameter(5.0f, false);
        public FloatParameter coverageWindSpeed = new FloatParameter(25.0f, false);

        [Header("Planet")]
        public Vector3Parameter planetZeroCoordinate = new Vector3Parameter(Vector3.zero, false);
        public FloatParameter planetSize = new FloatParameter(35000, false);
        public FloatParameter startHeight = new FloatParameter(1500, false);
        public FloatParameter thickness = new FloatParameter(4000, false);


        [Header("Wind")]
        public FloatParameter windSpeed = new FloatParameter(15.9f, false);
        public FloatParameter windDirection = new FloatParameter(-22.4f, false);

        public override bool IsActive() => opened.value;
    }

    [PostProcess("Volumetric Cloud", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class VolumetricCloudRenderer : PostProcessVolumeRenderer<VolumetricCloud>
    {
        static class ShaderConstants
        {
            public static readonly int Matrix_ScreenToWorld = Shader.PropertyToID("_ScreenToWorld");
            public static readonly int Matrix_WorldToView = Shader.PropertyToID("_WorldToView");
            // 用来区分高斯模糊是X还是Y
            public static readonly int GaussFilterXYMask = Shader.PropertyToID("_PixelOffsetXY");
        }


        Material m_Material;
        int downSample = 2;

        RTHandle CloudColorBuffer;
        RTHandle bloomRT_Temp0;
        RTHandle bloomRT_Temp1;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.volumetricCloudPS);
        }

        private void SetupMaterials(ref RenderingData renderingData)
        {

        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.graphicsFormat = GraphicsFormat.R8G8B8A8_SRGB;
            desc.depthBufferBits = 0;
            desc.sRGB = false;
            desc.stencilFormat = GraphicsFormat.None;
            FilterMode filterMode = FilterMode.Bilinear;
            // 下采样
            desc.width /= downSample;
            desc.height /= downSample;
            RenderingUtils.ReAllocateHandleIfNeeded(ref CloudColorBuffer, desc, filterMode, TextureWrapMode.Clamp, name: "_VoxCloudColorBuffer");
            desc.width /= settings.blurDownSample.value;
            desc.height /= settings.blurDownSample.value;
            RenderingUtils.ReAllocateHandleIfNeeded(ref bloomRT_Temp0, desc, filterMode, TextureWrapMode.Clamp, name: "_VoxCloudBlurTemp0");
            RenderingUtils.ReAllocateHandleIfNeeded(ref bloomRT_Temp1, desc, filterMode, TextureWrapMode.Clamp, name: "_VoxCloudBlurTemp1");

            // ConfigureTarget(CloudColorBuffer);
            // ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(ref renderingData);


            // Blit(cmd, source, target, m_Material, 0);
        }
    }
}
