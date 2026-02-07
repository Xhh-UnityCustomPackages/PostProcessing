using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public abstract class PostProcessRenderer
    {
        bool m_Initialized = false;
        bool m_ShowHide = false;


        protected PostProcessRenderPass m_RenderPass;
        public ProfilingSampler profilingSampler;

        protected PostProcessFeatureData postProcessFeatureData { get; private set; }
        protected PostProcessData postProcessData { get; private set; }
        
        //是否在SceneView可见
        public virtual bool visibleInSceneView => true;
        //有些插入时机 2种渲染路径都有 但是具体还需要控制支持情况
        public virtual SupportRenderPath supportRenderPath => SupportRenderPath.Forward | SupportRenderPath.Deferred;
        //需要的管线特性
        public virtual ScriptableRenderPassInput input => ScriptableRenderPassInput.None;
        //需要的后效管线特性
        public virtual PostProcessPassInput postProcessPassInput => PostProcessPassInput.None;
       
        // 默认最后都需要渲染到Camera
        public virtual bool renderToCamera => true;
        // 如果明确知道该效果不会出现把source同时作为target的情况
        // dontCareSourceTargetCopy设置为true，则该效果出现在队列第一个时，就不需要拷贝原始RT
        public virtual bool dontCareSourceTargetCopy => false;

        public abstract bool IsActive(ref RenderingData renderingData);

        internal void SetupInternal(PostProcessRenderPass renderPass,  ref RenderingData renderingData, PostProcessFeatureData data, PostProcessData context)
        {
            if (m_Initialized)
                return;
            m_Initialized = true;

            postProcessData = context;
            m_RenderPass = renderPass;
            postProcessFeatureData = data;
            Setup();
        }

        // 只会调用一次
        protected virtual void Setup() { }
        
        [Obsolete]
        public abstract void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData);

        public virtual void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
        }

        [Obsolete]
        public virtual void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) { }
        public virtual void OnCameraCleanup(CommandBuffer cmd) { }
        public virtual void AddRenderPasses(ref RenderingData renderingData) { }
        // 无论Active是否都会进入
        public virtual void Dispose(bool disposing) { }

        internal void ShowHideInternal(ref RenderingData renderingData)
        {
            if (m_ShowHide != IsActive(ref renderingData))
            {
                m_ShowHide = IsActive(ref renderingData);
                ShowHide(m_ShowHide);
            }
        }

        protected virtual void ShowHide(bool showHide) { }


        protected Material GetMaterial(Shader shader)
        {
            if (shader == null)
            {
                Debug.LogError("Missing shader in PostProcessFeatureData");
                return null;
            }

            return CoreUtils.CreateEngineMaterial(shader);
        }

        public virtual void InitProfilingSampler()
        {
            var attribute = PostProcessAttribute.GetAttribute(GetType());
            profilingSampler = new ProfilingSampler(attribute?.Name);
        }
        
        protected void Blit(CommandBuffer cmd, RTHandle source, RTHandle destination, Material material, int passIndex = 0)
        {
            Blitter.BlitCameraTexture(cmd, source, destination, material, passIndex);
        }

        protected void Blit(CommandBuffer cmd, RTHandle source, RTHandle destination, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, Material material, int passIndex)
        {
            Blitter.BlitCameraTexture(cmd, source, destination, loadAction, storeAction, material, passIndex);
        }

        protected void Blit(CommandBuffer cmd, RTHandle source, RTHandle destination)
        {
            Blitter.BlitCameraTexture(cmd, source, destination);
        }

        protected void GetCompatibleDescriptor(ref RenderTextureDescriptor desc, GraphicsFormat format)
        {
            desc.graphicsFormat = format;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
        }

        protected void DescriptorDownSample(ref RenderTextureDescriptor desc, int downSample)
        {
            desc.width = Mathf.Max(Mathf.FloorToInt(desc.width / downSample), 1);
            desc.height = Mathf.Max(Mathf.FloorToInt(desc.height / downSample), 1);
        }
    }
}
