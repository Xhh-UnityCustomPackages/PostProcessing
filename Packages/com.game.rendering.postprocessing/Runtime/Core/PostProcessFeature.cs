using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //后处理插入点 支持位操作 所以一个效果可以选择配置在不同的位置
    [Flags]
    public enum PostProcessInjectionPoint
    {
        BeforeRenderingGBuffer = 1 << 4,
        BeforeRenderingDeferredLights = 1 << 0,
        AfterRenderingSkybox = 1 << 1,
        BeforeRenderingPostProcessing = 1 << 2,
        AfterRenderingPostProcessing = 1 << 3,
        BeforeRenderingOpaques = 1 << 5,
        AfterRenderingOpaques = 1 << 6,
    }

    [Flags]
    public enum SupportRenderPath
    {
        Forward = 1 << 0,
        Deferred = 1 << 1,
    }

    [DisallowMultipleRendererFeature]
    public class PostProcessFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PostProcessSettings
        {
            [SerializeField]
            public PostProcessFeatureData m_PostProcessFeatureData;

            public bool GeneratorPyramidDepth = false;

            //各个阶段
            [SerializeField] public List<string> m_RenderersBeforeRenderingOpaques;
            [SerializeField] public List<string> m_RenderersAfterRenderingOpaques;
            [SerializeField] public List<string> m_RenderersBeforeRenderingGBuffer;
            [SerializeField] public List<string> m_RenderersBeforeRenderingDeferredLights;
            [SerializeField] public List<string> m_RenderersAfterRenderingSkybox;
            [SerializeField] public List<string> m_RenderersBeforeRenderingPostProcessing;
            [SerializeField] public List<string> m_RenderersAfterRenderingPostProcessing;

            public PostProcessSettings()
            {
                m_RenderersBeforeRenderingOpaques = new List<string>();
                m_RenderersAfterRenderingOpaques = new List<string>();
                
                m_RenderersBeforeRenderingGBuffer = new List<string>();
                m_RenderersBeforeRenderingDeferredLights = new List<string>();
                
                m_RenderersAfterRenderingSkybox = new List<string>();
                m_RenderersBeforeRenderingPostProcessing = new List<string>();
                m_RenderersAfterRenderingPostProcessing = new List<string>();
            }
        }

        [SerializeField] public PostProcessSettings m_Settings = new PostProcessSettings();

        private PostProcessRenderPass m_BeforeRenderingGBuffer, m_BeforeRenderingDeferredLights;
        private PostProcessRenderPass m_BeforeRenderingOpaques, m_AfterRenderingOpaques;
        private PostProcessRenderPass m_AfterRenderingSkybox, m_BeforeRenderingPostProcessing, m_AfterRenderingPostProcessing;
        UberPostProcess m_UberPostProcessing;
        PyramidDepthGenerator m_HizDepthGenerator;

        private bool m_CheckedRenderingMode = false;
        private RenderingMode m_RenderingMode;
        public RenderingMode RenderingMode => m_RenderingMode;
        
#if UNITY_EDITOR
        DebugHandler m_DebugHandler;
#endif

        public override void Create()
        {
            var postProcessFeatureData = m_Settings.m_PostProcessFeatureData;
            Dictionary<string, PostProcessRenderer> shared = new Dictionary<string, PostProcessRenderer>();
            m_BeforeRenderingGBuffer = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingGBuffer,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingGBuffer, shared),
                postProcessFeatureData);
            m_BeforeRenderingDeferredLights = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingDeferredLights,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingDeferredLights, shared),
                postProcessFeatureData);

            m_BeforeRenderingOpaques = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingOpaques,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingOpaques, shared),
                postProcessFeatureData);
            m_AfterRenderingOpaques = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingOpaques,
                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingOpaques, shared),
                postProcessFeatureData);

            m_AfterRenderingSkybox = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingSkybox,
                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingSkybox, shared),
                postProcessFeatureData);
            // 外挂后处理目前只放在这个位置
            m_BeforeRenderingPostProcessing = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingPostProcessing,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingPostProcessing, shared),
                postProcessFeatureData);
            m_AfterRenderingPostProcessing = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingPostProcessing,
                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingPostProcessing, shared),
                postProcessFeatureData);


            m_UberPostProcessing = new UberPostProcess(postProcessFeatureData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
            };

            PyramidBlur.Initialize(postProcessFeatureData.materials.DualBlur);

#if UNITY_EDITOR
            m_DebugHandler = new DebugHandler();
            m_DebugHandler.Init();
#endif
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Settings.m_PostProcessFeatureData == null)
            {
#if UNITY_EDITOR
                m_Settings.m_PostProcessFeatureData = UnityEditor.AssetDatabase.LoadAssetAtPath<PostProcessFeatureData>(PostProcessingUtils.packagePath + "/Runtime/Core/PostProcessFeatureData.asset");
#endif

                Debug.LogError("Please Add PostProcessFeatureData To PostProcessFeature");

                return;
            }

            CheckRenderingMode(renderer);

            var camera = renderingData.cameraData.camera;
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.VR)
            {
                return;
            }

            if (renderingData.cameraData.postProcessEnabled)
            {
                if (m_RenderingMode == RenderingMode.Deferred)
                {
                    //SupportRenderPath 为 Deferred|Both 才能加入这两个
                    m_BeforeRenderingGBuffer.AddRenderPasses(ref renderingData);
                    m_BeforeRenderingDeferredLights.AddRenderPasses(ref renderingData);
                }
                else
                {
                    //SupportRenderPath 为 Forward|Both 才能加入这两个
                    m_BeforeRenderingOpaques.AddRenderPasses(ref renderingData);
                    m_AfterRenderingOpaques.AddRenderPasses(ref renderingData);
                }
                
                m_AfterRenderingSkybox.AddRenderPasses(ref renderingData);
                m_BeforeRenderingPostProcessing.AddRenderPasses(ref renderingData);
                // 暂时不考虑 Camera stack 的情况
                m_AfterRenderingPostProcessing.AddRenderPasses(ref renderingData);

                renderer.EnqueuePass(m_UberPostProcessing);
            }

            //TODO 这里改为类似与CameraMode 那种请求模式
            if (m_Settings.GeneratorPyramidDepth)
            {
                if (m_HizDepthGenerator == null)
                    m_HizDepthGenerator = new PyramidDepthGenerator(m_Settings.m_PostProcessFeatureData.computeShaders.pyramidDepthGeneratorCS);
                renderer.EnqueuePass(m_HizDepthGenerator);
            }

#if UNITY_EDITOR
            m_DebugHandler.EnqueuePass(renderer);
#endif
        }

        private void CheckRenderingMode(ScriptableRenderer renderer)
        {
            if (!m_CheckedRenderingMode)
            {
                if (renderer is UniversalRenderer universalRenderer)
                {
                    //必须使用反射才能拿到
                    //RenderingMode m_RenderingMode;
                    Type rendererType = typeof(UniversalRenderer);
                    FieldInfo renderingModeField = rendererType.GetField("m_RenderingMode",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (renderingModeField != null)
                    {
                        object renderingModeValue = renderingModeField.GetValue(universalRenderer);
                        
                        if (renderingModeValue != null)
                        {
                            m_RenderingMode = (RenderingMode)((int)renderingModeValue);
                            Debug.Log("Current RenderingMode" + m_RenderingMode);
                        }
                    }
                }

                m_CheckedRenderingMode = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_BeforeRenderingOpaques.Dispose(disposing);
            m_AfterRenderingOpaques.Dispose(disposing);
            m_BeforeRenderingGBuffer.Dispose(disposing);
            m_BeforeRenderingDeferredLights.Dispose(disposing);
            m_AfterRenderingSkybox.Dispose(disposing);
            m_BeforeRenderingPostProcessing.Dispose(disposing);
            m_AfterRenderingPostProcessing.Dispose(disposing);
            m_UberPostProcessing.Dispose();
            PyramidBlur.Release();

            m_CheckedRenderingMode = false;
#if UNITY_EDITOR
            m_DebugHandler.Dispose();
#endif
        }


        // 根据Attribute定义 收集子类
        private List<PostProcessRenderer> InstantiateRenderers(List<String> names, Dictionary<string, PostProcessRenderer> shared)
        {
            var renderers = new List<PostProcessRenderer>(names.Count);
            foreach (var name in names)
            {
                if (shared.TryGetValue(name, out var renderer))
                {
                    renderers.Add(renderer);
                }
                else
                {
                    var type = Type.GetType(name);
                    if (type == null || !type.IsSubclassOf(typeof(PostProcessRenderer))) continue;
                    var attribute = PostProcessAttribute.GetAttribute(type);
                    if (attribute == null) continue;

                    renderer = Activator.CreateInstance(type) as PostProcessRenderer;
                    renderers.Add(renderer);

                    if (attribute.ShareInstance)
                        shared.Add(name, renderer);
                }
            }
            return renderers;
        }
    }
}
