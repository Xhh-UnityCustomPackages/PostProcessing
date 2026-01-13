using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
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

    //效果需求的管线特性
    [Flags]
    public enum PostProcessPassInput
    {
        None = 0,
        DepthPyramid = 1 << 0, // 层次深度
        ScreenSpaceShadow = 1 << 1, // 走屏幕空间阴影 
        ColorPyramid = 1 << 2, // 颜色金字塔
        PreviousFrameColor = 1 << 3, //上一帧颜色
        // UberPost = 1 << 4,//自定义UberPost
    }

    [DisallowMultipleRendererFeature]
    public partial class PostProcessFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PostProcessSettings
        {
            [SerializeField]
            public PostProcessFeatureData m_PostProcessFeatureData;

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

        private PostProcessFeatureRuntimeResources m_RuntimeResources;
        private PostProcessRenderPass m_BeforeRenderingGBuffer, m_BeforeRenderingDeferredLights;
        private PostProcessRenderPass m_BeforeRenderingOpaques, m_AfterRenderingOpaques;
        private PostProcessRenderPass m_AfterRenderingSkybox, m_BeforeRenderingPostProcessing, m_AfterRenderingPostProcessing;
        UberPostProcess m_UberPostProcessing;
        private static PostProcessFeatureContext m_Context;


        private SetGlobalVariablesPass m_SetGlobalVariablesPass;
        private ColorPyramidPass m_ColorPyramidPass;
        private DepthPyramidPass m_HizDepthGenerator;
        
        private ScreenSpaceShadowsPass m_SSShadowsPass = null;
        private ScreenSpaceShadowsPostPass m_SSShadowsPostPass = null;

        private RenderingMode m_RenderingMode;
        public RenderingMode RenderingMode => m_RenderingMode;
        
#if UNITY_EDITOR
        DebugHandler m_DebugHandler;
#endif

        public override void Create()
        {
            m_RuntimeResources = GraphicsSettings.GetRenderPipelineSettings<PostProcessFeatureRuntimeResources>();
            
            var postProcessFeatureData = m_Settings.m_PostProcessFeatureData;
            m_Context = new PostProcessFeatureContext();
            
            PyramidBlur.Initialize(postProcessFeatureData.materials.DualBlur);
            
            Dictionary<string, PostProcessRenderer> shared = new Dictionary<string, PostProcessRenderer>();
            m_BeforeRenderingGBuffer = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingGBuffer,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingGBuffer, shared),
                postProcessFeatureData, m_Context);
            m_BeforeRenderingDeferredLights = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingDeferredLights,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingDeferredLights, shared),
                postProcessFeatureData, m_Context);

            m_BeforeRenderingOpaques = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingOpaques,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingOpaques, shared),
                postProcessFeatureData, m_Context);
            m_AfterRenderingOpaques = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingOpaques,
                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingOpaques, shared),
                postProcessFeatureData, m_Context);

            m_AfterRenderingSkybox = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingSkybox,
                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingSkybox, shared),
                postProcessFeatureData, m_Context);
            // 外挂后处理目前只放在这个位置
            m_BeforeRenderingPostProcessing = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingPostProcessing,
                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingPostProcessing, shared),
                postProcessFeatureData, m_Context);
            m_AfterRenderingPostProcessing = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingPostProcessing,
                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingPostProcessing, shared),
                postProcessFeatureData, m_Context);

            m_UberPostProcessing = new UberPostProcess(postProcessFeatureData);
            m_SetGlobalVariablesPass = new SetGlobalVariablesPass(m_Context);
            
#if UNITY_EDITOR
            m_DebugHandler = new DebugHandler();
            m_DebugHandler.Init();
#endif
        }

        RenderPassEvent GetMotionVectorPassEvent(ScriptableRenderer renderer)
        {
            var m_CopyDepthMode = UniversalRenderingUtility.GetCopyDepthMode(renderer);
            bool copyDepthAfterTransparents = m_CopyDepthMode == CopyDepthMode.AfterTransparents;
            RenderPassEvent copyDepthEvent = copyDepthAfterTransparents ? RenderPassEvent.AfterRenderingTransparents : RenderPassEvent.AfterRenderingSkybox;
            return copyDepthEvent + 1;
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

            m_Context.Setup(camera);
            m_Context.UpdateFrame(ref renderingData);
            
            
            // AfterRenderingPrePasses
            renderer.EnqueuePass(m_SetGlobalVariablesPass);
            

            PostProcessPassInput postProcessPassInput = PostProcessPassInput.None; 
            
            if (renderingData.cameraData.postProcessEnabled)
            {
                if (m_RenderingMode == RenderingMode.Deferred)
                {
                    //SupportRenderPath 为 Deferred|Both 才能加入这两个
                    m_BeforeRenderingGBuffer.AddRenderPasses(ref renderingData, ref postProcessPassInput);
                    m_BeforeRenderingDeferredLights.AddRenderPasses(ref renderingData, ref postProcessPassInput);
                }
                else
                {
                    //SupportRenderPath 为 Forward|Both 才能加入这两个
                    m_BeforeRenderingOpaques.AddRenderPasses(ref renderingData, ref postProcessPassInput);
                    m_AfterRenderingOpaques.AddRenderPasses(ref renderingData, ref postProcessPassInput);
                }
                
                m_AfterRenderingSkybox.AddRenderPasses(ref renderingData, ref postProcessPassInput);
                m_AfterRenderingSkybox.renderPassEvent = GetMotionVectorPassEvent(renderer) + 1;//因为MotionVector的顺序会发生改变 这里在强制改变一次
                m_BeforeRenderingPostProcessing.AddRenderPasses(ref renderingData, ref postProcessPassInput);
                // 暂时不考虑 Camera stack 的情况
                m_AfterRenderingPostProcessing.AddRenderPasses(ref renderingData, ref postProcessPassInput);

                renderer.EnqueuePass(m_UberPostProcessing);
            }
            
            DealPostProcessInput(renderer, postProcessPassInput);

#if UNITY_EDITOR
            m_DebugHandler.EnqueuePass(renderer);
#endif
        }

        void DealPostProcessInput(ScriptableRenderer renderer, PostProcessPassInput postProcessPassInput)
        {
            if (postProcessPassInput.HasFlag(PostProcessPassInput.DepthPyramid))
            {
                m_HizDepthGenerator ??= new DepthPyramidPass(m_Context);
                renderer.EnqueuePass(m_HizDepthGenerator);
            }

            if (postProcessPassInput.HasFlag(PostProcessPassInput.ColorPyramid))
            {
                if (m_Context.FrameCount <= 3)
                {
                    return;
                }

                m_ColorPyramidPass ??= new ColorPyramidPass(m_Context);
                renderer.EnqueuePass(m_ColorPyramidPass);
            }

            if (postProcessPassInput.HasFlag(PostProcessPassInput.ScreenSpaceShadow))
            {
                if (m_SSShadowsPass == null)
                    m_SSShadowsPass = new ScreenSpaceShadowsPass();
                if (m_SSShadowsPostPass == null)
                    m_SSShadowsPostPass = new ScreenSpaceShadowsPostPass();

                m_SSShadowsPostPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                
                m_SSShadowsPass.renderPassEvent = m_RenderingMode == RenderingMode.Deferred
                    ? RenderPassEvent.AfterRenderingGbuffer
                    : RenderPassEvent.AfterRenderingPrePasses + 1; // We add 1 to ensure this happens after depth priming depth copy pass that might be scheduled
                
                m_SSShadowsPass.Setup();

                renderer.EnqueuePass(m_SSShadowsPass);
                renderer.EnqueuePass(m_SSShadowsPostPass);
            }
        }

        private void CheckRenderingMode(ScriptableRenderer renderer)
        {
            m_RenderingMode = UniversalRenderingUtility.GetRenderingMode(renderer);
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
            
            m_SSShadowsPass?.Dispose();
            m_HizDepthGenerator?.Dispose();
            
            m_Context.Dispose();
            
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
