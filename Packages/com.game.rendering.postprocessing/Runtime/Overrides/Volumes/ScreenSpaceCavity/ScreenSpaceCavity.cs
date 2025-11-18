using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/screen-space-cavity-curvature-built-in-urp-hdrp-216995#releases
    [Serializable, VolumeComponentMenu("Post-processing Custom/(Screen Space Cavity)")]
    public class ScreenSpaceCavity : VolumeSetting
    {
        public ScreenSpaceCavity()
        {
            displayName = "(Screen Space Cavity)";
        }

        public enum PerPixelNormals
        {
            ReconstructedFromDepth,
            Camera
        }

        public enum CavityResolution
        {
            Full,
            [InspectorName("Half Upscaled")] HalfUpscaled,
            Half
        }

        public enum CavitySamples
        {
            Low6 = 6,
            Medium8 = 8,
            High12 = 12,
            VeryHigh20 = 20
        }

        public enum OutputEffectTo
        {
            Screen,
            [InspectorName("_ScreenSpaceCavityRT in shaders")] 
            _ScreenSpaceCavityRT
        }

        public enum DebugMode { Disabled, EffectOnly, ViewNormals }


        [Header("(Make sure Post Processing and Depth Texture are enabled.)")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter distanceFade = new ClampedFloatParameter(0f, 0f, 1f);

        [Space(6)]
        [Tooltip("The radius of curvature calculations in pixels.")]
        public ClampedIntParameter curvaturePixelRadius = new ClampedIntParameter(2, 0, 4);
        [Tooltip("How bright does curvature get.")]
        public ClampedFloatParameter curvatureBrights = new ClampedFloatParameter(2f, 0f, 5f);
        [Tooltip("How dark does curvature get.")]
        public ClampedFloatParameter curvatureDarks = new ClampedFloatParameter(3f, 0f, 5f);


        [Space(6)]

        [Tooltip("The amount of samples used for cavity calculation.")]
        public EnumParameter<CavitySamples> cavitySamples = new(CavitySamples.High12);
        [Tooltip("True: Use pow() blending to make colors more saturated in bright/dark areas of cavity.\nFalse: Use additive blending.\n\nWarning: This option being enabled may mess with bloom post processing.")]
        public BoolParameter saturateCavity = new BoolParameter(true);
        [Tooltip("The radius of cavity calculations in world units.")]
        public ClampedFloatParameter cavityRadius = new ClampedFloatParameter(0.25f, 0f, 0.5f);
        [Tooltip("How bright does cavity get.")]
        public ClampedFloatParameter cavityBrights = new ClampedFloatParameter(3f, 0f, 5f);
        [Tooltip("How dark does cavity get.")]
        public ClampedFloatParameter cavityDarks = new ClampedFloatParameter(2f, 0f, 5f);
        [Tooltip("With this option enabled, cavity can be downsampled to massively improve performance at a cost to visual quality. Recommended for mobile platforms.\n\nNon-upscaled half may introduce aliasing.")]
        public EnumParameter<CavityResolution> cavityResolution = new(CavityResolution.Full);
        public EnumParameter<PerPixelNormals> normalsSource = new(PerPixelNormals.Camera);


        [Space(6)]

        [Header("Debug")]
        public EnumParameter<DebugMode> debugMode = new(DebugMode.Disabled);


        [Space(5)]
        [Tooltip("Screen: Applies the effect over the entire screen.\n\n_ScreenSpaceCavityRT: Instead of writing the effect to the screen, will write the effect into a global shader texture named _SSCCTexture, so you can sample it selectively in your shaders and exclude certain objects from receiving outlines etc. See \"Output To Texture Examples\" folder for example shaders.")]
        public EnumParameter<OutputEffectTo> output = new(OutputEffectTo.Screen);


        public override bool IsActive() => intensity.value > 0;
    }


    [PostProcess("ScreenSpaceCavity", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public partial class ScreenSpaceCavityRenderer : PostProcessVolumeRenderer<ScreenSpaceCavity>
    {
        static class ShaderConstants
        {
            public static int inputTexelSize = Shader.PropertyToID("_Input_TexelSize");
            public static int cavityTexTexelSize = Shader.PropertyToID("_CavityTex_TexelSize");
            public static int worldToCameraMatrix = Shader.PropertyToID("_WorldToCameraMatrix");
            //public static int uvToView = Shader.PropertyToID("_UVToView");

            public static int effectIntensity = Shader.PropertyToID("_EffectIntensity");
            public static int distanceFade = Shader.PropertyToID("_DistanceFade");

            public static int curvaturePixelRadius = Shader.PropertyToID("_CurvaturePixelRadius");
            public static int curvatureRidge = Shader.PropertyToID("_CurvatureBrights");
            public static int curvatureValley = Shader.PropertyToID("_CurvatureDarks");
            public static int cavitySampleCount = Shader.PropertyToID("_CavitySamplesCount");

            public static int cavityWorldRadius = Shader.PropertyToID("_CavityWorldRadius");
            public static int cavityRidge = Shader.PropertyToID("_CavityBrights");
            public static int cavityValley = Shader.PropertyToID("_CavityDarks");

            public static int globalSSCCTexture = Shader.PropertyToID("_ScreenSpaceCavityRT");
        }

        static class Pass
        {
            // public const int Copy = 0;
            public const int GenerateCavity = 0;
            // public const int HorizontalBlur = 2;
            // public const int VerticalBlur = 3;
            public const int Final = 1;
        }

        private Material m_Material;
        private RTHandle m_CavityFinalRT;
        private RTHandle m_TempRT;
        string[] m_ShaderKeywords = new string[5];

        public override bool renderToCamera => settings.output.value == ScreenSpaceCavity.OutputEffectTo.Screen;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.ScreenSpaceCavityPS);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            var desc = cameraTargetDescriptor;
            GetCompatibleDescriptor(ref desc, desc.graphicsFormat);
            desc.colorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.RHalf;

            var resolution = settings.cavityResolution.value;
            if (resolution != ScreenSpaceCavity.CavityResolution.Full)
            {
                DescriptorDownSample(ref desc, 2);
            }

            if (settings.output.value == ScreenSpaceCavity.OutputEffectTo._ScreenSpaceCavityRT)
            {
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_CavityFinalRT, desc, FilterMode.Bilinear, name: "_ScreenSpaceCavityRT");
            }

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TempRT, desc, FilterMode.Bilinear, name: "_ScreenSpaceCavityTempRT");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            if (m_Material == null)
                return;

            SetupMaterials(ref renderingData, m_Material);


            Blit(cmd, source, m_TempRT, m_Material, Pass.GenerateCavity);
            // Blit(cmd, m_TempRT, destination);
            m_Material.SetTexture("_CavityTex", source);
            if (settings.output.value == ScreenSpaceCavity.OutputEffectTo.Screen)
            {
                Blit(cmd, m_TempRT, destination, m_Material, Pass.Final);
            }
            else
            {
                Blit(cmd, m_TempRT, m_CavityFinalRT, m_Material, Pass.Final);
                cmd.SetGlobalTexture(ShaderConstants.globalSSCCTexture, m_CavityFinalRT);
            }
        }


        private void SetupMaterials(ref RenderingData renderingData, Material material)
        {
            var cameraData = renderingData.cameraData;
            var sourceWidth = cameraData.cameraTargetDescriptor.width;
            var sourceHeight = cameraData.cameraTargetDescriptor.height;

            //float tanHalfFovY = Mathf.Tan(0.5f * cameraData.camera.fieldOfView * Mathf.Deg2Rad);
            //float invFocalLenX = 1.0f / (1.0f / tanHalfFovY * (sourceHeight / (float)sourceWidth));
            //float invFocalLenY = 1.0f / (1.0f / tanHalfFovY);
            //material.SetVector(ShaderProperties.uvToView, new Vector4(2.0f * invFocalLenX, -2.0f * invFocalLenY, -1.0f * invFocalLenX, 1.0f * invFocalLenY));

            material.SetVector(ShaderConstants.inputTexelSize, new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
            int div = settings.cavityResolution.value == ScreenSpaceCavity.CavityResolution.Full ? 1 : settings.cavityResolution.value == ScreenSpaceCavity.CavityResolution.HalfUpscaled ? 2 : 2;
            material.SetVector(ShaderConstants.cavityTexTexelSize, new Vector4(1f / (sourceWidth / div), 1f / (sourceHeight / div), sourceWidth / div, sourceHeight / div));
            material.SetMatrix(ShaderConstants.worldToCameraMatrix, cameraData.camera.worldToCameraMatrix);

            material.SetFloat(ShaderConstants.effectIntensity, settings.intensity.value);
            material.SetFloat(ShaderConstants.distanceFade, settings.distanceFade.value);
            material.SetInt(ShaderConstants.cavitySampleCount, (int)settings.cavitySamples.value);

            material.SetFloat(ShaderConstants.curvaturePixelRadius, new float[] { 0f, 0.5f, 1f, 1.5f, 2.5f }[settings.curvaturePixelRadius.value]);
            material.SetFloat(ShaderConstants.curvatureRidge, settings.curvatureBrights.value == 0f ? 999f : (5f - settings.curvatureBrights.value));
            material.SetFloat(ShaderConstants.curvatureValley, settings.curvatureDarks.value == 0f ? 999f : (5f - settings.curvatureDarks.value));

            material.SetFloat(ShaderConstants.cavityWorldRadius, settings.cavityRadius.value);
            material.SetFloat(ShaderConstants.cavityRidge, settings.cavityBrights.value * 2f);
            material.SetFloat(ShaderConstants.cavityValley, settings.cavityDarks.value * 2f);


            m_ShaderKeywords[0] = settings.debugMode.value == ScreenSpaceCavity.DebugMode.EffectOnly ? "DEBUG_EFFECT" : settings.debugMode.value == ScreenSpaceCavity.DebugMode.ViewNormals ? "DEBUG_NORMALS" : "__";
            m_ShaderKeywords[1] = settings.normalsSource.value == ScreenSpaceCavity.PerPixelNormals.ReconstructedFromDepth ? "NORMALS_RECONSTRUCT" : "__";
            m_ShaderKeywords[2] = settings.saturateCavity.value ? "SATURATE_CAVITY" : "__";
            m_ShaderKeywords[3] = settings.output == ScreenSpaceCavity.OutputEffectTo._ScreenSpaceCavityRT ? "OUTPUT_TO_TEXTURE" : "__";
            m_ShaderKeywords[4] = settings.cavityResolution.value == ScreenSpaceCavity.CavityResolution.HalfUpscaled ? "UPSCALE_CAVITY" : "__";


            material.shaderKeywords = m_ShaderKeywords;
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            m_CavityFinalRT?.Release();
        }
    }
}
