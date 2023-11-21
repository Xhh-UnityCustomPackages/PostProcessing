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

        public enum ReflectionsWorkflow
        {
            SmoothnessOnly,
            MetallicAndSmoothness = 10
        }

        [Serializable]
        public sealed class OutputModeParameter : VolumeParameter<OutputMode>
        {
            public OutputModeParameter(OutputMode value, bool overrideState = false) : base(value, overrideState) { }
        }

        [Serializable] public sealed class ReflectionsWorkflowParameter : VolumeParameter<ReflectionsWorkflow> { }



        [Header("General Settings")]

        [Tooltip("Reflection multiplier")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0, 2f);

        [Tooltip("Show reflections in SceneView window")]
        public BoolParameter showInSceneView = new BoolParameter(true);

        [Tooltip("The 'Smoothness Only' workflow only takes into account a single value for both reflections intensity and roughness. The 'Metallic And Smoothness' workflow uses full physically based material properties where the metallic property defines the intensity of the reflection and the smoothness property its roughness.")]
        public ReflectionsWorkflowParameter reflectionsWorkflow = new ReflectionsWorkflowParameter { value = ReflectionsWorkflow.SmoothnessOnly };

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

        [Tooltip("Enables temporal filter which reduces flickering")]
        public BoolParameter temporalFilter = new BoolParameter(false);

        [Tooltip("Temporal filter response speed determines how fast the history buffer is discarded")]
        public FloatParameter temporalFilterResponseSpeed = new FloatParameter(1f);

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
        public MinFloatParameter decay = new MinFloatParameter(2f, 0);

        [Tooltip("Reduces intensity of specular reflections")]
        public BoolParameter specularControl = new BoolParameter(true);

        [Min(0), Tooltip("Power of the specular filter")]
        public FloatParameter specularSoftenPower = new FloatParameter(15f);

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
        public BoolParameter isHDR = new BoolParameter(true);

        public override bool IsActive() => intensity.value > 0;

        public static float metallicGradientCachedId, smoothnessGradientCachedId;



        public void ApplyRaytracingPreset(RaytracingPreset preset)
        {
            switch (preset)
            {
                case RaytracingPreset.Fast:
                    sampleCount.Override(16);
                    maxRayLength.Override(6);
                    binarySearchIterations.Override(4);
                    downsampling.Override(3);
                    thickness.Override(0.5f);
                    refineThickness.Override(false);
                    jitter.Override(0.3f);
                    // temporalFilter.Override(false);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.Medium:
                    sampleCount.Override(24);
                    maxRayLength.Override(12);
                    binarySearchIterations.Override(5);
                    downsampling.Override(2);
                    refineThickness.Override(false);
                    // temporalFilter.Override(false);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.High:
                    sampleCount.Override(48);
                    maxRayLength.Override(24);
                    binarySearchIterations.Override(6);
                    downsampling.Override(1);
                    refineThickness.Override(false);
                    thicknessFine.Override(0.05f);
                    // temporalFilter.Override(false);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.Superb:
                    sampleCount.Override(88);
                    maxRayLength.Override(48);
                    binarySearchIterations.Override(7);
                    downsampling.Override(1);
                    refineThickness.Override(true);
                    thicknessFine.Override(0.02f);
                    // temporalFilter.Override(true);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.Ultra:
                    sampleCount.Override(128);
                    maxRayLength.Override(64);
                    binarySearchIterations.Override(8);
                    downsampling.Override(1);
                    refineThickness.Override(true);
                    thicknessFine.Override(0.02f);
                    // temporalFilter.Override(true);
                    computeBackFaces.Override(true);
                    break;
            }
            // Reflections.needUpdateMaterials = true;
        }
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
            public static int RayCast = Shader.PropertyToID("_RayCastRT");
            public static int PrevResolveNameId = Shader.PropertyToID("_PrevResolve");

            // shader keywords
            internal static readonly string SKW_JITTER = "SSR_JITTER";
            internal static readonly string SKW_BACK_FACES = "SSR_BACK_FACES";
            internal static readonly string SKW_DENOISE = "SSR_DENOISE";
            internal static readonly string SKW_REFINE_THICKNESS = "SSR_THICKNESS_FINE";
            internal static readonly string SKW_METALLIC_WORKFLOW = "SSR_METALLIC_WORKFLOW";
        }

        enum Pass
        {
            CopyDepth = 0,
            GBufferPass = 1,
            Resolve = 2,
            BlurH = 3,
            BlurV = 4,
            Combine = 5,
            CombineWithCompare = 6,
            Debug = 7,
            Copy = 8,
            TemporalAccum = 9,

        }

        const float GOLDEN_RATIO = 0.618033989f;

        Material m_Material;
        RTHandle m_RayCastTargetHandle;
        RTHandle m_DownscaleDepthTargetHandle;
        RTHandle m_ReflectionTargetHandle;
        RTHandle[] m_BlurMipDownTargetHandles;
        RTHandle[] m_BlurMipDownTargetHandles2;
        const int MIP_COUNT = 4;//这边可以优化

        Texture2D metallicGradientTex, smoothnessGradientTex;

        //temporalFilter
        readonly Dictionary<Camera, RTHandle> prevs = new Dictionary<Camera, RTHandle>();
        RTHandle m_TempAcumTargetHandle;
        bool m_FirstTemporal = false;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;

            RenderTextureDescriptor sourceDesc = renderingData.cameraData.cameraTargetDescriptor;
            sourceDesc.colorFormat = !settings.isHDR.value ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
            DescriptorDownSample(ref sourceDesc, settings.downsampling.value);
            sourceDesc.msaaSamples = 1;
            sourceDesc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref m_RayCastTargetHandle, sourceDesc, FilterMode.Point, name: "_SSR_RayCastRT");
            RenderingUtils.ReAllocateIfNeeded(ref m_ReflectionTargetHandle, sourceDesc, FilterMode.Point, name: "_SSR_ReflectionRT");


            var depthDesc = sourceDesc;
            depthDesc.colorFormat = settings.computeBackFaces.value ? RenderTextureFormat.RGHalf : RenderTextureFormat.RHalf;
            depthDesc.sRGB = false;
            // sourceDesc.depthBufferBits = 32;
            RenderingUtils.ReAllocateIfNeeded(ref m_DownscaleDepthTargetHandle, depthDesc, FilterMode.Point, name: "_SSR_DownscaleDepthRT");


            // temporalFilter
            if (settings.temporalFilter.value)
            {
                prevs.TryGetValue(camera, out RTHandle prev);
                RenderingUtils.ReAllocateIfNeeded(ref prev, sourceDesc, FilterMode.Bilinear, name: "_SSR_TemporalFilterRT");
                prevs[camera] = prev;
                RenderingUtils.ReAllocateIfNeeded(ref m_TempAcumTargetHandle, sourceDesc, FilterMode.Bilinear, name: "_SSR_TempAcumRT");
            }


            if (m_BlurMipDownTargetHandles == null || m_BlurMipDownTargetHandles.Length != MIP_COUNT)
            {
                m_BlurMipDownTargetHandles = new RTHandle[MIP_COUNT];
                m_BlurMipDownTargetHandles2 = new RTHandle[MIP_COUNT];
            }

            var blurDesc = sourceDesc;
            // blurDesc.width = renderingData.cameraData.cameraTargetDescriptor.width;
            // blurDesc.height = renderingData.cameraData.cameraTargetDescriptor.height;
            DescriptorDownSample(ref blurDesc, settings.blurDownsampling.value);


            for (int k = 0; k < MIP_COUNT; k++)
            {
                blurDesc.width = Mathf.Max(2, blurDesc.width / 2);
                blurDesc.height = Mathf.Max(2, blurDesc.height / 2);
                RenderingUtils.ReAllocateIfNeeded(ref m_BlurMipDownTargetHandles[k], blurDesc, FilterMode.Bilinear, name: "_SSR_BlurMip" + k);
                RenderingUtils.ReAllocateIfNeeded(ref m_BlurMipDownTargetHandles2[k], blurDesc, FilterMode.Bilinear, name: "_SSR_BlurMip" + k);
            }

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
                m_Material = GetMaterial(postProcessFeatureData.shaders.screenSpaceRaytracedReflectionPS);

            SetupMaterials(ref renderingData, m_Material);

            Blit(cmd, source, m_DownscaleDepthTargetHandle, m_Material, (int)Pass.CopyDepth);
            cmd.SetGlobalTexture(ShaderConstants.DownscaledDepthRT, m_DownscaleDepthTargetHandle);
            Blit(cmd, source, m_RayCastTargetHandle, m_Material, (int)Pass.GBufferPass);
            cmd.SetGlobalTexture(ShaderConstants.RayCast, m_RayCastTargetHandle);
            Blit(cmd, source, m_ReflectionTargetHandle, m_Material, (int)Pass.Resolve);

            var input = m_ReflectionTargetHandle;
            // temporalFilter
            if (settings.temporalFilter.value)
            {
                prevs.TryGetValue(camera, out RTHandle prev);

                int pass = (int)Pass.TemporalAccum;
                if (m_FirstTemporal == false)
                {
                    m_FirstTemporal = true;
                    pass = (int)Pass.Copy;
                }
                m_Material.SetFloat(ShaderConstants.TemporalResponseSpeed, settings.temporalFilterResponseSpeed.value);
                m_Material.SetTexture(ShaderConstants.PrevResolveNameId, prev);

                Blit(cmd, m_ReflectionTargetHandle, m_TempAcumTargetHandle, m_Material, pass);
                Blit(cmd, m_TempAcumTargetHandle, prev, m_Material, (int)Pass.Copy); // do not use CopyExact as its fragment clamps color values - also, cmd.CopyTexture does not work correctly here
                input = m_TempAcumTargetHandle;
            }

            // Pyramid Blur
            for (int k = 0; k < MIP_COUNT; k++)
            {
                Blit(cmd, input, m_BlurMipDownTargetHandles2[k], m_Material, (int)Pass.BlurH);
                Blit(cmd, m_BlurMipDownTargetHandles2[k], m_BlurMipDownTargetHandles[k], m_Material, (int)Pass.BlurV);

                input = m_BlurMipDownTargetHandles[k];
                cmd.SetGlobalTexture("_BlurRTMip" + k, m_BlurMipDownTargetHandles[k]);
            }

            // Output
            int finalPass;
            switch (settings.outputMode.value)
            {
                case ScreenSpaceRaytracedReflection.OutputMode.Final: finalPass = (int)Pass.Combine; break;
                case ScreenSpaceRaytracedReflection.OutputMode.SideBySideComparison: finalPass = (int)Pass.CombineWithCompare; break;
                default:
                    finalPass = (int)Pass.Debug; break;
            }

            Blit(cmd, m_ReflectionTargetHandle, target, m_Material, finalPass);

            // Blit(cmd, source, target);
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            CoreUtils.Destroy(metallicGradientTex);
            CoreUtils.Destroy(smoothnessGradientTex);

            m_RayCastTargetHandle?.Release();
            m_DownscaleDepthTargetHandle?.Release();

            if (m_BlurMipDownTargetHandles != null)
                for (int k = 0; k < MIP_COUNT; k++)
                {
                    m_BlurMipDownTargetHandles[k]?.Release();
                    m_BlurMipDownTargetHandles2[k]?.Release();
                }
        }


        private void SetupMaterials(ref RenderingData renderingData, Material material)
        {
            if (m_Material == null)
                return;

            var camera = renderingData.cameraData.camera;

            material.SetTexture(ShaderConstants.NoiseTex, settings.noiseTex.value == null ? postProcessFeatureData.textures.blueNoise16RGBTex[0] : settings.noiseTex.value);

            float goldenFactor = GOLDEN_RATIO;
            if (settings.animatedJitter.value)
            {
                goldenFactor *= Time.frameCount % 480;
            }

            // set global settings
            material.SetVector(ShaderConstants.MaterialData, new Vector4(0, settings.fresnel.value, settings.fuzzyness.value + 1f, settings.decay.value));
            material.SetVector(ShaderConstants.SSRSettings, new Vector4(settings.thickness.value, settings.sampleCount.value, settings.binarySearchIterations.value, settings.maxRayLength.value));
            material.SetVector(ShaderConstants.SSRSettings2, new Vector4(settings.jitter.value, settings.contactHardening.value + 0.0001f, settings.intensity.value, 0));
            material.SetVector(ShaderConstants.SSRSettings3, new Vector4(m_RayCastTargetHandle.referenceSize.x, m_RayCastTargetHandle.referenceSize.y, goldenFactor, settings.depthBias.value));
            material.SetVector(ShaderConstants.SSRSettings4, new Vector4(settings.separationPos.value, settings.reflectionsMinIntensity.value, settings.reflectionsMaxIntensity.value, settings.specularSoftenPower.value));
            material.SetVector(ShaderConstants.SSRSettings5, new Vector4(settings.thicknessFine.value * settings.thickness.value, settings.smoothnessThreshold.value, 0, 0));
            material.SetVector(ShaderConstants.SSRBlurStrength, new Vector4(settings.blurStrength.value.x, settings.blurStrength.value.y, settings.vignetteSize.value, settings.vignettePower.value));

            CoreUtils.SetKeyword(material, ShaderConstants.SKW_DENOISE, settings.specularControl.value);
            material.SetFloat(ShaderConstants.MinimumBlur, settings.minimumBlur.value);
            // material.SetInt(ShaderConstants.StencilValue, settings.stencilValue.value);

            CoreUtils.SetKeyword(material, ShaderConstants.SKW_JITTER, settings.jitter.value > 0);
            CoreUtils.SetKeyword(material, ShaderConstants.SKW_BACK_FACES, settings.computeBackFaces.value);
            CoreUtils.SetKeyword(material, ShaderConstants.SKW_REFINE_THICKNESS, settings.refineThickness.value);
            material.SetFloat(ShaderConstants.MinimumThickness, settings.thicknessMinimum.value);


            material.SetMatrix(ShaderConstants.WorldToViewMatrix, camera.worldToCameraMatrix);

            var SSR_ProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            material.SetMatrix(ShaderConstants.InverseProjectionMatrix, SSR_ProjectionMatrix.inverse);


            if (settings.reflectionsWorkflow.value == ScreenSpaceRaytracedReflection.ReflectionsWorkflow.MetallicAndSmoothness)
            {
                material.EnableKeyword(ShaderConstants.SKW_METALLIC_WORKFLOW);
                UpdateGradientTextures();
            }
            else
            {
                material.DisableKeyword(ShaderConstants.SKW_METALLIC_WORKFLOW);
            }
        }


        void UpdateGradientTextures()
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
