using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class PostProcessFeature
    {
        /// <summary>
        /// Setup pass that handles renderer configuration and setup logic.
        /// </summary>
        private class SetupPass : ScriptableRenderPass, IDisposable
        {
            private readonly PostProcessFeature m_RendererFeature;
            private readonly PostProcessData m_Context;

            public SetupPass(PostProcessFeature rendererFeature, PostProcessData context)
            {
                m_RendererFeature = rendererFeature;
                m_Context = context;
                renderPassEvent = RenderPassEvent.BeforeRendering;
                profilingSampler = new ProfilingSampler("Global Setup");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (new ProfilingScope((CommandBuffer)null, profilingSampler))
                {
                    m_Context.renderingMode = m_RendererFeature.RenderingMode;
                    m_RendererFeature.PerformSetup(frameData, m_Context);
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                using (new ProfilingScope((CommandBuffer)null, profilingSampler))
                {
                    m_Context.renderingMode = m_RendererFeature.RenderingMode;
                    m_RendererFeature.PerformSetup(ref renderingData, m_Context);
                }
            }

            public void Dispose()
            {
                // pass
            }
        }
        
        private void PerformSetup(ContextContainer frameData, PostProcessData context)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            context.Update(cameraData);
        }
        
        
        private void PerformSetup(ref RenderingData renderingData, PostProcessData context)
        {
            context.Update(ref renderingData);
        }
    }
}