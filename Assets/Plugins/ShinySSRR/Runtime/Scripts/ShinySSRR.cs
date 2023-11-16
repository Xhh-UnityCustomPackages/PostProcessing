/// <summary>
/// Shiny SSRR - Screen Space Reflections for URP - (c) 2021-2022 Kronnect
/// </summary>

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShinySSRR
{

    public class ShinySSRR : ScriptableRendererFeature
    {

        public class SmoothnessMetallicPass : ScriptableRenderPass
        {

            const string m_ProfilerTag = "Shiny SSRR Smoothness Metallic Pass";

            static class ShaderParams
            {
                public const string SmoothnessMetallicRTName = "_SmoothnessMetallicRT";
                public static int SmoothnessMetallicRT = Shader.PropertyToID(SmoothnessMetallicRTName);
            }

            FilteringSettings m_FilteringSettings;
            readonly List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
            RTHandle m_SmootnessMetallicRT;

            public SmoothnessMetallicPass()
            {
                RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.SmoothnessMetallicRT, 0, CubemapFace.Unknown, -1);
                m_SmootnessMetallicRT = RTHandles.Alloc(rti, name: ShaderParams.SmoothnessMetallicRTName);
                m_ShaderTagIdList.Add(new ShaderTagId("SmoothnessMetallic"));
                m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque, 0);
            }


            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {

                RenderTextureDescriptor desc = cameraTextureDescriptor;
                desc.colorFormat = RenderTextureFormat.RGHalf; // r = smoothness, g = metallic
                desc.depthBufferBits = 24;
                desc.msaaSamples = 1;

                cmd.GetTemporaryRT(ShaderParams.SmoothnessMetallicRT, desc, FilterMode.Point);
                cmd.SetGlobalTexture(ShaderParams.SmoothnessMetallicRT, m_SmootnessMetallicRT);
                ConfigureTarget(m_SmootnessMetallicRT);
                ConfigureClear(ClearFlag.All, Color.black);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {

                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);

                SortingCriteria sortingCriteria = SortingCriteria.CommonOpaque;
                var drawSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);

                context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }

            public override void FrameCleanup(CommandBuffer cmd)
            {
                if (cmd == null) return;
                cmd.ReleaseTemporaryRT(ShaderParams.SmoothnessMetallicRT);
            }

            public void CleanUp()
            {
                RTHandles.Release(m_SmootnessMetallicRT);
            }
        }

        class SSRPass : ScriptableRenderPass
        {

            enum Pass
            {
                CopyExact = 0,
                SSRSurf = 1,
                Resolve = 2,
                BlurHoriz = 3,
                BlurVert = 4,
                Debug = 5,
                Combine = 6,
                CombineWithCompare = 7,
                GBuffPass = 8,
                Copy = 9,
                TemporalAccum = 10,
                DebugDepth = 11,
                DebugNormals = 12,
                CopyDepth = 13
            }

            const string SHINY_CBUFNAME = "Shiny SSRR";
            const float GOLDEN_RATIO = 0.618033989f;
            ScriptableRenderer renderer;
            Material sMat;
            Texture noiseTex;
            [NonSerialized]
            public ShinyScreenSpaceRaytracedReflections settings;
            ShinySSRR ssrFeature;
            readonly Plane[] frustumPlanes = new Plane[6];
            const int MIP_COUNT = 5;
            int[] rtPyramid;
            readonly Dictionary<Camera, RenderTexture> prevs = new Dictionary<Camera, RenderTexture>();
            Texture2D metallicGradientTex, smoothnessGradientTex;

            public bool Setup(ScriptableRenderer renderer, ShinySSRR ssrFeature)
            {

                settings = VolumeManager.instance.stack.GetComponent<ShinyScreenSpaceRaytracedReflections>();
                if (settings == null || !settings.IsActive()) return false;
                this.ssrFeature = ssrFeature;
                this.renderer = renderer;
                this.renderPassEvent = ssrFeature.renderPassEvent;
                if (sMat == null)
                {
                    Shader shader = Shader.Find("Hidden/Kronnect/SSR_URP");
                    sMat = CoreUtils.CreateEngineMaterial(shader);
                }
                if (noiseTex == null)
                {
                    noiseTex = Resources.Load<Texture>("SSR/blueNoiseSSR64");
                }
                sMat.SetTexture(ShaderParams.NoiseTex, noiseTex);

                // set global settings
                sMat.SetVector(ShaderParams.SSRSettings2, new Vector4(settings.jitter.value, settings.contactHardening.value + 0.0001f, settings.reflectionsMultiplier.value, 0));
                sMat.SetVector(ShaderParams.SSRSettings4, new Vector4(settings.separationPos.value, 0, 0, settings.specularSoftenPower.value));
                sMat.SetVector(ShaderParams.SSRBlurStrength, new Vector4(settings.blurStrength.value.x, settings.blurStrength.value.y, settings.vignetteSize.value, settings.vignettePower.value));
                sMat.SetVector(ShaderParams.SSRSettings5, new Vector4(settings.thicknessFine.value * settings.thickness.value, 0, 0, 0));

                CoreUtils.SetKeyword(sMat, ShaderParams.SKW_DENOISE, settings.specularControl.value);
                sMat.SetFloat(ShaderParams.MinimumBlur, settings.minimumBlur.value);
                sMat.SetInt(ShaderParams.StencilValue, settings.stencilValue.value);
                sMat.SetInt(ShaderParams.StencilCompareFunction, settings.stencilCheck.value ? (int)settings.stencilCompareFunction.value : (int)CompareFunction.Always);

                if (settings.computeBackFaces.value)
                {
                    Shader.EnableKeyword(ShaderParams.SKW_BACK_FACES);
                    Shader.SetGlobalFloat(ShaderParams.MinimumThickness, settings.thicknessMinimum.value);
                }
                else
                {
                    Shader.DisableKeyword(ShaderParams.SKW_BACK_FACES);
                }




                CoreUtils.SetKeyword(sMat, ShaderParams.SKW_JITTER, settings.jitter.value > 0);
                CoreUtils.SetKeyword(sMat, ShaderParams.SKW_REFINE_THICKNESS, settings.refineThickness.value);

                sMat.SetVector(ShaderParams.SSRSettings, new Vector4(settings.thickness.value, settings.sampleCount.value, settings.binarySearchIterations.value, settings.maxRayLength.value));
                sMat.SetVector(ShaderParams.MaterialData, new Vector4(0, settings.fresnel.value, settings.fuzzyness.value + 1f, settings.decay.value));



                UpdateGradientTextures();

                if (rtPyramid == null || rtPyramid.Length != MIP_COUNT)
                {
                    rtPyramid = new int[MIP_COUNT];
                    for (int k = 0; k < rtPyramid.Length; k++)
                    {
                        rtPyramid[k] = Shader.PropertyToID("_BlurRTMip" + k);
                    }
                }

                return true;
            }


            void UpdateGradientTextures()
            {
                UpdateGradientTexture(ref metallicGradientTex, settings.reflectionsIntensityCurve.value, ref ShinyScreenSpaceRaytracedReflections.metallicGradientCachedId);
                UpdateGradientTexture(ref smoothnessGradientTex, settings.reflectionsSmoothnessCurve.value, ref ShinyScreenSpaceRaytracedReflections.smoothnessGradientCachedId);
                Shader.SetGlobalTexture(ShaderParams.MetallicGradientTex, metallicGradientTex);
                Shader.SetGlobalTexture(ShaderParams.SmoothnessGradientTex, smoothnessGradientTex);
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



            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                ScriptableRenderPassInput inputs = ScriptableRenderPassInput.Depth;
                if (ssrFeature.enableScreenSpaceNormalsPass && !ssrFeature.useDeferred)
                {
                    inputs |= ScriptableRenderPassInput.Normal;
                }
#if UNITY_2021_3_OR_NEWER
                if (settings.temporalFilter.value)
                {
                    inputs |= ScriptableRenderPassInput.Motion;
                }
#endif
                ConfigureInput(inputs);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {

                Camera cam = renderingData.cameraData.camera;

                // ignore SceneView depending on setting
                if (cam.cameraType == CameraType.SceneView)
                {
                    if (!settings.showInSceneView.value) return;
                }
                else
                {
                    // ignore any camera other than GameView
                    if (cam.cameraType != CameraType.Game) return;
                }

                RenderTextureDescriptor sourceDesc = renderingData.cameraData.cameraTargetDescriptor;
                sourceDesc.colorFormat = settings.lowPrecision.value ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
                sourceDesc.width /= settings.downsampling.value;
                sourceDesc.height /= settings.downsampling.value;
                sourceDesc.msaaSamples = 1;

                float goldenFactor = GOLDEN_RATIO;
                if (settings.animatedJitter.value)
                {
                    goldenFactor *= (Time.frameCount % 480);
                }
                Shader.SetGlobalVector(ShaderParams.SSRSettings3, new Vector4(sourceDesc.width, sourceDesc.height, goldenFactor, settings.depthBias.value));

                CommandBuffer cmd = null;

                RTHandle source = renderer.cameraColorTargetHandle;


                bool useReflectionsScripts = true;
                // int count = Reflections.instances.Count;
                bool usingDeferredPass = ssrFeature.useDeferred && !settings.skipDeferredPass.value;
                if (usingDeferredPass)
                {
                    useReflectionsScripts = settings.useReflectionsScripts.value;

                    // init command buffer
                    cmd = CommandBufferPool.Get(SHINY_CBUFNAME);

                    ComputeDepth(cmd, sourceDesc);

                    // pass UNITY_MATRIX_V
                    sMat.SetMatrix(ShaderParams.WorldToViewDir, cam.worldToCameraMatrix);

                    // prepare ssr target
                    cmd.GetTemporaryRT(ShaderParams.RayCast, sourceDesc, FilterMode.Point);
                    // if (count > 0 && useReflectionsScripts)
                    // {
                    //     cmd.SetRenderTarget(ShaderParams.RayCast);
                    //     cmd.ClearRenderTarget(true, false, new Color(0, 0, 0, 0));
                    // }

                    // raytrace using gbuffers
                    FullScreenBlit(cmd, source, ShaderParams.RayCast, Pass.GBuffPass);

                }


                // Resolve reflections
                RenderTextureDescriptor copyDesc = sourceDesc;
                copyDesc.depthBufferBits = 0;

                cmd.GetTemporaryRT(ShaderParams.ReflectionsTex, copyDesc);
                FullScreenBlit(cmd, source, ShaderParams.ReflectionsTex, Pass.Resolve);
                RenderTargetIdentifier input = ShaderParams.ReflectionsTex;

                prevs.TryGetValue(cam, out RenderTexture prev);

                if (settings.temporalFilter.value)
                {

                    if (prev != null && (prev.width != copyDesc.width || prev.height != copyDesc.height))
                    {
                        prev.Release();
                        prev = null;
                    }

                    RenderTextureDescriptor acumDesc = copyDesc;
                    Pass acumPass = Pass.TemporalAccum;

                    if (prev == null)
                    {
                        prev = new RenderTexture(acumDesc);
                        prev.Create();
                        prevs[cam] = prev;
                        acumPass = Pass.Copy;
                    }

                    sMat.SetFloat(ShaderParams.TemporalResponseSpeed, settings.temporalFilterResponseSpeed.value);
                    Shader.SetGlobalTexture(ShaderParams.PrevResolveNameId, prev);
                    cmd.GetTemporaryRT(ShaderParams.TempAcum, acumDesc, FilterMode.Bilinear);
                    FullScreenBlit(cmd, ShaderParams.ReflectionsTex, ShaderParams.TempAcum, acumPass);
                    FullScreenBlit(cmd, ShaderParams.TempAcum, prev, Pass.Copy); // do not use CopyExact as its fragment clamps color values - also, cmd.CopyTexture does not work correctly here
                    input = ShaderParams.TempAcum;
                }
                else if (prev != null)
                {
                    prev.Release();
                    DestroyImmediate(prev);
                }

                RenderTargetIdentifier reflectionsTex = input;

                // Pyramid blur
                int blurDownsampling = settings.blurDownsampling.value;
                copyDesc.width /= blurDownsampling;
                copyDesc.height /= blurDownsampling;
                for (int k = 0; k < MIP_COUNT; k++)
                {
                    copyDesc.width = Mathf.Max(2, copyDesc.width / 2);
                    copyDesc.height = Mathf.Max(2, copyDesc.height / 2);
                    cmd.GetTemporaryRT(rtPyramid[k], copyDesc, FilterMode.Bilinear);
                    cmd.GetTemporaryRT(ShaderParams.BlurRT, copyDesc, FilterMode.Bilinear);
                    FullScreenBlit(cmd, input, ShaderParams.BlurRT, Pass.BlurHoriz);
                    FullScreenBlit(cmd, ShaderParams.BlurRT, rtPyramid[k], Pass.BlurVert);
                    cmd.ReleaseTemporaryRT(ShaderParams.BlurRT);
                    input = rtPyramid[k];
                }

                // Output
                int finalPass;
                switch (settings.outputMode.value)
                {
                    case OutputMode.Final: finalPass = (int)Pass.Combine; break;
                    case OutputMode.SideBySideComparison: finalPass = (int)Pass.CombineWithCompare; break;
                    case OutputMode.DebugDepth: finalPass = (int)Pass.DebugDepth; break;
                    case OutputMode.DebugDeferredNormals: finalPass = (int)Pass.DebugNormals; break;
                    default:
                        finalPass = (int)Pass.Debug; break;
                }
                FullScreenBlit(cmd, reflectionsTex, source, (Pass)finalPass);

                if (settings.stopNaN.value)
                {
                    RenderTextureDescriptor nanDesc = renderingData.cameraData.cameraTargetDescriptor;
                    nanDesc.depthBufferBits = 0;
                    nanDesc.msaaSamples = 1;
                    cmd.GetTemporaryRT(ShaderParams.NaNBuffer, nanDesc);
                    FullScreenBlit(cmd, source, ShaderParams.NaNBuffer, Pass.CopyExact);
                    FullScreenBlit(cmd, ShaderParams.NaNBuffer, source, Pass.CopyExact);
                }

                // Clean up
                for (int k = 0; k < rtPyramid.Length; k++)
                {
                    cmd.ReleaseTemporaryRT(rtPyramid[k]);
                }
                cmd.ReleaseTemporaryRT(ShaderParams.ReflectionsTex);
                cmd.ReleaseTemporaryRT(ShaderParams.RayCast);
                cmd.ReleaseTemporaryRT(ShaderParams.DownscaledDepthRT);

                context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }

            void ComputeDepth(CommandBuffer cmd, RenderTextureDescriptor desc)
            {
                desc.colorFormat = settings.computeBackFaces.value ? RenderTextureFormat.RGHalf : RenderTextureFormat.RHalf;
                desc.sRGB = false;
                desc.depthBufferBits = 0;
                cmd.GetTemporaryRT(ShaderParams.DownscaledDepthRT, desc, FilterMode.Point);
                FullScreenBlit(cmd, ShaderParams.DownscaledDepthRT, Pass.CopyDepth);
            }


            static Mesh _fullScreenMesh;

            Mesh fullscreenMesh
            {
                get
                {
                    if (_fullScreenMesh != null)
                    {
                        return _fullScreenMesh;
                    }
                    float num = 1f;
                    float num2 = 0f;
                    Mesh val = new Mesh();
                    _fullScreenMesh = val;
                    _fullScreenMesh.SetVertices(new List<Vector3> {
            new Vector3 (-1f, -1f, 0f),
            new Vector3 (-1f, 1f, 0f),
            new Vector3 (1f, -1f, 0f),
            new Vector3 (1f, 1f, 0f)
        });
                    _fullScreenMesh.SetUVs(0, new List<Vector2> {
            new Vector2 (0f, num2),
            new Vector2 (0f, num),
            new Vector2 (1f, num2),
            new Vector2 (1f, num)
        });
                    _fullScreenMesh.SetIndices(new int[6] { 0, 1, 2, 2, 1, 3 }, (MeshTopology)0, 0, false);
                    _fullScreenMesh.UploadMeshData(true);
                    return _fullScreenMesh;
                }
            }

            void FullScreenBlit(CommandBuffer cmd, RenderTargetIdentifier destination, Pass pass)
            {
                cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
                cmd.DrawMesh(fullscreenMesh, Matrix4x4.identity, sMat, 0, (int)pass);
            }

            void FullScreenBlit(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, Pass pass)
            {
                cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
                cmd.SetGlobalTexture(ShaderParams.MainTex, source);
                cmd.DrawMesh(fullscreenMesh, Matrix4x4.identity, sMat, 0, (int)pass);
            }

            /// Cleanup any allocated resources that were created during the execution of this render pass.
            public override void FrameCleanup(CommandBuffer cmd)
            {
            }


            public void CleanUp()
            {
                CoreUtils.Destroy(sMat);
                if (prevs != null)
                {
                    foreach (RenderTexture rt in prevs.Values)
                    {
                        if (rt != null)
                        {
                            rt.Release();
                            DestroyImmediate(rt);
                        }
                    }
                    prevs.Clear();
                }
                CoreUtils.Destroy(metallicGradientTex);
                CoreUtils.Destroy(smoothnessGradientTex);
            }
        }

        class SSRBackfacesPass : ScriptableRenderPass
        {

            const string m_ProfilerTag = "Shiny SSR Backfaces Pass";
            const string m_DepthOnlyShader = "Universal Render Pipeline/Unlit";

            FilteringSettings m_FilteringSettings;
            readonly List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
            Material depthOnlyMaterial;
            RTHandle m_Depth;
            ShinyScreenSpaceRaytracedReflections settings;

            public SSRBackfacesPass()
            {
                RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.DownscaledBackDepthRT, 0, CubemapFace.Unknown, -1);
                m_Depth = RTHandles.Alloc(rti, name: ShaderParams.DownscaledBackDepthTextureName);
                m_ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);
            }

            public void Setup(ShinyScreenSpaceRaytracedReflections settings)
            {
                this.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                this.settings = settings;
                m_FilteringSettings.layerMask = settings.computeBackFacesLayerMask.value;
            }

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                RenderTextureDescriptor depthDesc = cameraTextureDescriptor;
                int downsampling = settings.downsampling.value;
                depthDesc.width = Mathf.CeilToInt(depthDesc.width / downsampling);
                depthDesc.height = Mathf.CeilToInt(depthDesc.height / downsampling);
                depthDesc.colorFormat = RenderTextureFormat.Depth;
                depthDesc.depthBufferBits = 24;
                depthDesc.msaaSamples = 1;

                cmd.GetTemporaryRT(ShaderParams.DownscaledBackDepthRT, depthDesc, FilterMode.Point);
                cmd.SetGlobalTexture(ShaderParams.DownscaledBackDepthRT, m_Depth);
                ConfigureTarget(m_Depth);
                ConfigureClear(ClearFlag.All, Color.black);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                SortingCriteria sortingCriteria = SortingCriteria.CommonOpaque;
                var drawSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
                drawSettings.perObjectData = PerObjectData.None;
                if (depthOnlyMaterial == null)
                {
                    Shader depthOnly = Shader.Find(m_DepthOnlyShader);
                    depthOnlyMaterial = new Material(depthOnly);
                }
                drawSettings.overrideMaterial = depthOnlyMaterial;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);

                context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }

            public override void FrameCleanup(CommandBuffer cmd)
            {
                if (cmd == null) return;
                cmd.ReleaseTemporaryRT(ShaderParams.DownscaledBackDepthRT);
            }


            public void CleanUp()
            {
                RTHandles.Release(m_Depth);
            }

        }

        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        [Tooltip("Use deferred g-buffers (requires deferred rendering in URP 12 or later)")]
        public bool useDeferred;

        [Tooltip("Requests screen space normals to be used with Reflections scripts that need them")]
        public bool enableScreenSpaceNormalsPass;

        [Tooltip("On which cameras should Shiny effects be applied")]
        public LayerMask cameraLayerMask = -1;

        SSRPass renderPass;
        SSRBackfacesPass backfacesPass;
        SmoothnessMetallicPass smoothnessMetallicPass;

        public static bool installed;
        public static bool isDeferredActive;
        public static bool isUsingScreenSpaceNormals;
        public static bool isEnabled = true;


        void OnDestroy()
        {
            installed = false;
            if (renderPass != null)
            {
                renderPass.CleanUp();
            }
            if (backfacesPass != null)
            {
                backfacesPass.CleanUp();
            }
            if (smoothnessMetallicPass != null)
            {
                smoothnessMetallicPass.CleanUp();
            }
        }

        public override void Create()
        {
            if (renderPass == null)
            {
                renderPass = new SSRPass();
            }
            if (backfacesPass == null)
            {
                backfacesPass = new SSRBackfacesPass();
            }
            if (smoothnessMetallicPass == null)
            {
                smoothnessMetallicPass = new SmoothnessMetallicPass();
                smoothnessMetallicPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }
        }


        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            installed = true;
            if (!isEnabled) return;
            isDeferredActive = useDeferred;
            isUsingScreenSpaceNormals = enableScreenSpaceNormalsPass && !useDeferred;

            if (!renderingData.postProcessingEnabled) return;

            Camera cam = renderingData.cameraData.camera;
            if ((cameraLayerMask.value & (1 << cam.gameObject.layer)) == 0) return;

            if (renderPass.Setup(renderer, this))
            {
                if (renderPass.settings.computeBackFaces.value)
                {
                    backfacesPass.Setup(renderPass.settings);
                    renderer.EnqueuePass(backfacesPass);
                }
                renderer.EnqueuePass(renderPass);
            }
        }

        /// <summary>
        /// Performs a refresh of the Reflections script and the materials used
        /// </summary>
        public void Refresh()
        {
            Reflections.needUpdateMaterials = true;
        }
    }

}
