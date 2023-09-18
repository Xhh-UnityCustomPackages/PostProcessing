using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //后处理插入点 支持位操作 所以一个效果可以选择配置在不同的位置
    [Flags]
    public enum PostProcessInjectionPoint
    {
        BeforeRenderingGBuffer = 1 << 4, // 由于是后面新增的选项，以防BUG，这里就定义为第四位
        BeforeRenderingDeferredLights = 1 << 0,
        AfterRenderingSkybox = 1 << 1,
        BeforeRenderingPostProcessing = 1 << 2,
        AfterRenderingPostProcessing = 1 << 3,
    }

    public class PostProcessFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PostProcessSettings
        {
            [SerializeField]
            public PostProcessFeatureData m_PostProcessFeatureData;

            //各个阶段
            [SerializeField] public List<string> m_RenderersBeforeRenderingGBuffer;
            [SerializeField] public List<string> m_RenderersBeforeRenderingDeferredLights;
            [SerializeField] public List<string> m_RenderersAfterRenderingSkybox;
            [SerializeField] public List<string> m_RenderersBeforeRenderingPostProcessing;
            [SerializeField] public List<string> m_RenderersAfterRenderingPostProcessing;

            public PostProcessSettings()
            {
                m_RenderersBeforeRenderingGBuffer = new List<string>();
                m_RenderersBeforeRenderingDeferredLights = new List<string>();
                m_RenderersAfterRenderingSkybox = new List<string>();
                m_RenderersBeforeRenderingPostProcessing = new List<string>();
                m_RenderersAfterRenderingPostProcessing = new List<string>();
            }
        }

        [SerializeField] public PostProcessSettings m_Settings = new PostProcessSettings();

        PostProcessRenderPass m_BeforeRenderingGBuffer, m_BeforeRenderingDeferredLights, m_AfterRenderingSkybox, m_BeforeRenderingPostProcessing, m_AfterRenderingPostProcessing;
        UberPostProcess m_UberPostProcessing;
        public override void Create()
        {
            Dictionary<string, PostProcessRenderer> shared = new Dictionary<string, PostProcessRenderer>();
            m_BeforeRenderingGBuffer = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingGBuffer,
                               InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingGBuffer, shared),
                               m_Settings.m_PostProcessFeatureData);
            m_BeforeRenderingDeferredLights = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingDeferredLights,
                                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingDeferredLights, shared),
                                m_Settings.m_PostProcessFeatureData);
            m_AfterRenderingSkybox = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingSkybox,
                                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingSkybox, shared),
                                m_Settings.m_PostProcessFeatureData);
            // 外挂后处理目前只放在这个位置
            m_BeforeRenderingPostProcessing = new PostProcessRenderPass(PostProcessInjectionPoint.BeforeRenderingPostProcessing,
                                InstantiateRenderers(m_Settings.m_RenderersBeforeRenderingPostProcessing, shared),
                                m_Settings.m_PostProcessFeatureData);
            m_AfterRenderingPostProcessing = new PostProcessRenderPass(PostProcessInjectionPoint.AfterRenderingPostProcessing,
                                InstantiateRenderers(m_Settings.m_RenderersAfterRenderingPostProcessing, shared),
                                m_Settings.m_PostProcessFeatureData);


            m_UberPostProcessing = new UberPostProcess(m_Settings.m_PostProcessFeatureData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
            };
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

            var camera = renderingData.cameraData.camera;
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            {
                return;
            }

            if (renderingData.cameraData.postProcessEnabled)
            {
                m_BeforeRenderingGBuffer.AddRenderPasses(ref renderingData);
                m_BeforeRenderingDeferredLights.AddRenderPasses(ref renderingData);
                m_AfterRenderingSkybox.AddRenderPasses(ref renderingData);
                m_BeforeRenderingPostProcessing.AddRenderPasses(ref renderingData);
                // 暂时不考虑 Camera stack 的情况
                m_AfterRenderingPostProcessing.AddRenderPasses(ref renderingData);

                renderer.EnqueuePass(m_UberPostProcessing);
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_BeforeRenderingGBuffer.Dispose(disposing);
            m_BeforeRenderingDeferredLights.Dispose(disposing);
            m_AfterRenderingSkybox.Dispose(disposing);
            m_BeforeRenderingPostProcessing.Dispose(disposing);
            m_AfterRenderingPostProcessing.Dispose(disposing);
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
