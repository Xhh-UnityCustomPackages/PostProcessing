using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/shiny-ssrr-2-screen-space-raytraced-reflections-188638
    [Serializable, VolumeComponentMenu("Post-processing Custom/屏幕空间反射 (Screen Space Raytraced Reflection)")]
    public class ScreenSpaceRaytracedReflection : VolumeSetting
    {
        public ScreenSpaceRaytracedReflection()
        {
            displayName = "屏幕空间反射 (Screen Space Raytraced Reflection)";
        }

        public enum OutputMode
        {
            Final,
            OnlyReflections,
            SideBySideComparison,
            // DebugDepth = 10,
            // DebugDeferredNormals = 11
        }

        public enum RaytracingPreset
        {
            Fast = 10,
            Medium = 20,
            High = 30,
            Superb = 35,
            Ultra = 40
        }

        [Serializable]
        public sealed class OutputModeParameter : VolumeParameter<OutputMode>
        {
            public OutputModeParameter(OutputMode value, bool overrideState = false) : base(value, overrideState) { }
        }


        [Header("General Settings")]

        [Tooltip("Reflection multiplier")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0, 2f);

        [Tooltip("Show reflections in SceneView window")]
        public BoolParameter showInSceneView = new BoolParameter(true);

        [Header("Quality Settings")]
        [Tooltip("Max number of samples used during the raymarch loop")]
        public ClampedIntParameter sampleCount = new ClampedIntParameter(16, 4, 128);

        [Tooltip("Maximum reflection distance")]
        public FloatParameter maxRayLength = new FloatParameter(24f);

        [Tooltip("Assumed thickness of geometry in the depth buffer before binary search")]
        public FloatParameter thickness = new FloatParameter(0.3f);

        [Tooltip("Number of refinements steps when a reflection hit is found")]
        public ClampedIntParameter binarySearchIterations = new ClampedIntParameter(6, 0, 16);

        [Tooltip("Increase accuracy of reflection hit after binary search by discarding points further than a reduced thickness.")]
        public BoolParameter refineThickness = new BoolParameter(false);

        [Tooltip("Assumed thickness of geometry in the depth buffer after binary search")]
        public ClampedFloatParameter thicknessFine = new ClampedFloatParameter(0.05f, 0.005f, 1f);

        [Tooltip("Jitter helps smoothing edges")]
        public ClampedFloatParameter jitter = new ClampedFloatParameter(0.3f, 0, 1f);

        [Tooltip("Animates jitter every frame")]
        public BoolParameter animatedJitter = new BoolParameter(false);

        [Tooltip("Performs a depth pre-pass to compute true thickness instead of assuming a fixed thickness for all geometry. This option can be expensive since it requires drawing the back depth of all opaque objects in the view frustum. You may need to increase the sample count as well.")]
        public BoolParameter computeBackFaces = new BoolParameter(false);

        [Tooltip("Minimum allowed thickness for any geometry.")]
        public ClampedFloatParameter thicknessMinimum = new ClampedFloatParameter(0.16f, 0.1f, 1f);

        [Tooltip("Downsampling multiplier applied to the final blurred reflections")]
        public ClampedIntParameter downsampling = new ClampedIntParameter(1, 1, 8);

        [Tooltip("Bias applied to depth checking. Increase if reflections desappear at the distance")]
        public FloatParameter depthBias = new FloatParameter(0.03f);

        public Texture2DParameter noiseTex = new Texture2DParameter(null);


        [Header("Reflection Intensity")]
        [Tooltip("Reflection intensity mapping curve.")]
        public AnimationCurveParameter reflectionsIntensityCurve = new AnimationCurveParameter(AnimationCurve.Linear(0, 0, 1, 1));

        [Tooltip("Reflection smooothness mapping curve.")]
        public AnimationCurveParameter reflectionsSmoothnessCurve = new AnimationCurveParameter(new AnimationCurve(new Keyframe(0, 0f, 0, 0.166666f), new Keyframe(0.5f, 0.25f, 0.833333f, 1.166666f), new Keyframe(1, 1f, 1.833333f, 0)));


        [Tooltip("Minimum smoothness to receive reflections")]
        public ClampedFloatParameter smoothnessThreshold = new ClampedFloatParameter(0, 0, 1f);

        [Tooltip("Reflection min intensity")]
        public ClampedFloatParameter reflectionsMinIntensity = new ClampedFloatParameter(0, 0, 1f);

        [Tooltip("Reflection max intensity")]
        public ClampedFloatParameter reflectionsMaxIntensity = new ClampedFloatParameter(1f, 0, 1f);

        [Tooltip("Reduces reflection based on view angle")]
        public ClampedFloatParameter fresnel = new ClampedFloatParameter(0.75f, 0, 1f);

        [Tooltip("Reflection decay with distance to reflective point")]
        public FloatParameter decay = new FloatParameter(2f);

        [Tooltip("Reduces intensity of specular reflections")]
        public BoolParameter specularControl = new BoolParameter(true);

        [Min(0), Tooltip("Power of the specular filter")]
        public FloatParameter specularSoftenPower = new FloatParameter(15f);

        [Tooltip("Skybox reflection intensity. Use only if you wish the sky or camera background to be reflected on the surfaces.")]
        public ClampedFloatParameter skyboxIntensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Controls the attenuation range of effect on screen borders")]
        public ClampedFloatParameter vignetteSize = new ClampedFloatParameter(1.1f, 0.5f, 2f);

        [Tooltip("Controls the attenuation gradient of effect on screen borders")]
        public ClampedFloatParameter vignettePower = new ClampedFloatParameter(1.5f, 0.1f, 10f);

        //--------------------------------------------------------------------------
        [Header("Reflection Sharpness")]
        [Tooltip("Ray dispersion with distance")]
        public FloatParameter fuzzyness = new FloatParameter(0);

        [Tooltip("Makes sharpen reflections near objects")]
        public FloatParameter contactHardening = new FloatParameter(0);
        [Tooltip("Minimum blur to be applied")]
        public ClampedFloatParameter minimumBlur = new ClampedFloatParameter(0.25f, 0, 4f);

        [Tooltip("Downsampling multiplier applied to the blur")]
        public ClampedIntParameter blurDownsampling = new ClampedIntParameter(1, 1, 8);

        [Tooltip("Custom directional blur strength")]
        public Vector2Parameter blurStrength = new Vector2Parameter(Vector2.one);

        //--------------------------------------------------------------------------
        [Header("Advanced Options")]
        [Tooltip("Show final result / debug view or compare view")]
        public OutputModeParameter outputMode = new OutputModeParameter(OutputMode.Final);

        [Tooltip("Position of the dividing line")]
        public ClampedFloatParameter separationPos = new ClampedFloatParameter(0.5f, -0.01f, 1.01f);

        [Tooltip("HDR reflections")]
        public BoolParameter lowPrecision = new BoolParameter(false);

        public override bool IsActive() => intensity.value > 0;

        public static float metallicGradientCachedId, smoothnessGradientCachedId;
    }


    [PostProcess("ScreenSpaceRaytracedReflection", PostProcessInjectionPoint.BeforeRenderingPostProcessing)]
    public class ScreenSpaceRaytracedReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceRaytracedReflection>
    {
        static class ShaderConstants
        {
            internal static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
            internal static readonly int MetallicGradientTex = Shader.PropertyToID("_MetallicGradientTex");
            internal static readonly int SmoothnessGradientTex = Shader.PropertyToID("_SmoothnessGradientTex");



            // shader uniforms
            internal static readonly int Color = Shader.PropertyToID("_Color");
            internal static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
            internal static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
            public static int Reflectivity = Shader.PropertyToID("_Metallic");
            public static int SmoothnessMap = Shader.PropertyToID("_SmoothnessMap");
            public static int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
            public static int MaterialData = Shader.PropertyToID("_MaterialData");
            public static int DistortionData = Shader.PropertyToID("_DistortionData");
            public static int SSRSettings = Shader.PropertyToID("_SSRSettings");
            public static int SSRSettings2 = Shader.PropertyToID("_SSRSettings2");
            public static int SSRSettings3 = Shader.PropertyToID("_SSRSettings3");
            public static int SSRSettings4 = Shader.PropertyToID("_SSRSettings4");
            public static int SSRSettings5 = Shader.PropertyToID("_SSRSettings5");
            public static int SSRBlurStrength = Shader.PropertyToID("_SSRBlurStrength");
            public static int WorldToViewMatrix = Shader.PropertyToID("_WorldToViewDir");
            public static int MinimumBlur = Shader.PropertyToID("_MinimumBlur");
            public static int StencilValue = Shader.PropertyToID("_StencilValue");
            public static int StencilCompareFunction = Shader.PropertyToID("_StencilCompareFunction");
            public static int TemporalResponseSpeed = Shader.PropertyToID("_TemporalResponseSpeed");
            internal static readonly int MinimumThickness = Shader.PropertyToID("_MinimumThickness");
            internal static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrix");

            //targets
            public static int DownscaledDepthRT = Shader.PropertyToID("_DownscaledShinyDepthRT");

            // shader keywords
            internal static readonly string SKW_BACK_FACES = "SSR_BACK_FACES";
        }

        enum Pass
        {
            CopyDepth = 0,
            GBufferPass = 1,
        }

        const float GOLDEN_RATIO = 0.618033989f;

        Material m_Material;
        RTHandle m_RayCastTargetHandle;
        RTHandle m_DownscaleTargetHandle;

        Texture2D metallicGradientTex, smoothnessGradientTex;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor sourceDesc = renderingData.cameraData.cameraTargetDescriptor;
            sourceDesc.colorFormat = settings.lowPrecision.value ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
            sourceDesc.width /= settings.downsampling.value;
            sourceDesc.height /= settings.downsampling.value;
            sourceDesc.msaaSamples = 1;
            sourceDesc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref m_RayCastTargetHandle, sourceDesc, FilterMode.Point, name: "_SSR_RayCastRT");



            sourceDesc.colorFormat = settings.computeBackFaces.value ? RenderTextureFormat.RGHalf : RenderTextureFormat.RHalf;
            sourceDesc.sRGB = false;
            RenderingUtils.ReAllocateIfNeeded(ref m_DownscaleTargetHandle, sourceDesc, FilterMode.Point, name: "_SSR_DownscaleDepthRT");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;

            if (camera.cameraType == CameraType.SceneView)
            {
                if (!settings.showInSceneView.value) return;
            }
            else
            {
                // ignore any camera other than GameView
                if (camera.cameraType != CameraType.Game) return;
            }

            if (m_Material == null)
                m_Material = new Material(Shader.Find("Hidden/PostProcessing/ScreenSpaceRaytracedReflection"));

            SetupMaterials(ref renderingData, m_Material);

            Blit(cmd, source, m_DownscaleTargetHandle, m_Material, (int)Pass.CopyDepth);
            cmd.SetGlobalTexture(ShaderConstants.DownscaledDepthRT, m_DownscaleTargetHandle);
            Blit(cmd, source, m_RayCastTargetHandle, m_Material, (int)Pass.GBufferPass);

            Blit(cmd, m_RayCastTargetHandle, target);
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            CoreUtils.Destroy(metallicGradientTex);
            CoreUtils.Destroy(smoothnessGradientTex);

            m_RayCastTargetHandle?.Release();
            m_DownscaleTargetHandle?.Release();
        }


        private void SetupMaterials(ref RenderingData renderingData, Material material)
        {
            if (m_Material == null)
                return;

            var camera = renderingData.cameraData.camera;

            material.SetTexture(ShaderConstants.NoiseTex, settings.noiseTex.value);

            float goldenFactor = GOLDEN_RATIO;
            if (settings.animatedJitter.value)
            {
                goldenFactor *= (Time.frameCount % 480);
            }

            // set global settings
            material.SetVector(ShaderConstants.MaterialData, new Vector4(0, settings.fresnel.value, settings.fuzzyness.value + 1f, settings.decay.value));
            material.SetVector(ShaderConstants.SSRSettings, new Vector4(settings.thickness.value, settings.sampleCount.value, settings.binarySearchIterations.value, settings.maxRayLength.value));
            material.SetVector(ShaderConstants.SSRSettings2, new Vector4(settings.jitter.value, settings.contactHardening.value + 0.0001f, settings.intensity.value, 0));
            material.SetVector(ShaderConstants.SSRSettings3, new Vector4(m_RayCastTargetHandle.referenceSize.x, m_RayCastTargetHandle.referenceSize.y, goldenFactor, settings.depthBias.value));
            material.SetVector(ShaderConstants.SSRSettings4, new Vector4(settings.separationPos.value, settings.reflectionsMinIntensity.value, settings.reflectionsMaxIntensity.value, settings.specularSoftenPower.value));
            material.SetVector(ShaderConstants.SSRSettings5, new Vector4(settings.thicknessFine.value * settings.thickness.value, settings.smoothnessThreshold.value, settings.skyboxIntensity.value, 0));
            material.SetVector(ShaderConstants.SSRBlurStrength, new Vector4(settings.blurStrength.value.x, settings.blurStrength.value.y, settings.vignetteSize.value, settings.vignettePower.value));

            if (settings.computeBackFaces.value)
            {
                Shader.EnableKeyword(ShaderConstants.SKW_BACK_FACES);
                Shader.SetGlobalFloat(ShaderConstants.MinimumThickness, settings.thicknessMinimum.value);
            }
            else
            {
                Shader.DisableKeyword(ShaderConstants.SKW_BACK_FACES);
            }

            material.SetMatrix(ShaderConstants.WorldToViewMatrix, camera.worldToCameraMatrix);

            var SSR_ProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            material.SetMatrix(ShaderConstants.InverseProjectionMatrix, SSR_ProjectionMatrix.inverse);

            UpdateGradientTexture();
        }


        void UpdateGradientTexture()
        {
            UpdateGradientTexture(ref metallicGradientTex, settings.reflectionsIntensityCurve.value, ref ScreenSpaceRaytracedReflection.metallicGradientCachedId);
            UpdateGradientTexture(ref smoothnessGradientTex, settings.reflectionsSmoothnessCurve.value, ref ScreenSpaceRaytracedReflection.smoothnessGradientCachedId);
            Shader.SetGlobalTexture(ShaderConstants.MetallicGradientTex, metallicGradientTex);
            Shader.SetGlobalTexture(ShaderConstants.SmoothnessGradientTex, smoothnessGradientTex);
        }

        Color[] colors;
        void UpdateGradientTexture(ref Texture2D tex, AnimationCurve curve, ref float cachedId)
        {
            if (colors == null || colors.Length != 256)
            {
                colors = new Color[256];
                cachedId = -1;
            }
            if (tex == null)
            {
                tex = new Texture2D(256, 1, TextureFormat.RHalf, false, true);
                cachedId = -1;
            }
            // quick test, evaluate 3 curve points
            float sum = curve.Evaluate(0) + curve.Evaluate(0.5f) + curve.Evaluate(1f) + 1;
            if (sum == cachedId) return;
            cachedId = sum;

            for (int k = 0; k < 256; k++)
            {
                float t = (float)k / 255;
                float v = curve.Evaluate(t);
                colors[k].r = v;
            }

            tex.SetPixels(colors);
            tex.Apply();
        }
    }
}
