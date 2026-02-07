using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class SetKeywordPass : ScriptableRenderPass, IDisposable
    {
        private PostProcessFeature m_Feature;
        public SetKeywordPass(PostProcessFeature feature)
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
            profilingSampler = new ProfilingSampler($"Set Global Keyword");
            m_Feature = feature;
        }
        
        public void Dispose()
        {
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                CoreUtils.SetKeyword(cmd, PipelineKeywords._DEFERRED_RENDERING_PATH, m_Feature.RenderingMode == RenderingMode.Deferred);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        private class SetKeywordPassData
        {
            internal PostProcessFeature feature;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<SetKeywordPassData>("Set Global Keyword", out var passData, profilingSampler))
            {
                passData.feature = m_Feature;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SetKeywordPassData data, UnsafeGraphContext context) =>
                {
                    CoreUtils.SetKeyword(context.cmd, PipelineKeywords._DEFERRED_RENDERING_PATH, data.feature.RenderingMode == RenderingMode.Deferred);
                });
            }
        }
    }
}