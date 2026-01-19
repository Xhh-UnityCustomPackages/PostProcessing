using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class PostProcessRenderPass : ScriptableRenderPass
    {
        RenderTextureDescriptor m_Descriptor;
       
        public class PassData
        {
            public TextureHandle sourceTexture;
            public TextureHandle destination;
        }
        
        private PassData m_PassData;
        
        static RenderTextureDescriptor GetCompatibleDescriptor(RenderTextureDescriptor desc, GraphicsFormat format, GraphicsFormat depthStencilFormat = GraphicsFormat.None)
        {
            desc.depthStencilFormat = depthStencilFormat;
            desc.msaaSamples = 1;
            desc.graphicsFormat = format;
            return desc;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalPostProcessingData postProcessingData = frameData.Get<UniversalPostProcessingData>();
            
            RenderTextureDescriptor cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
            m_Descriptor = cameraTargetDescriptor;
            
            var desc = GetCompatibleDescriptor(m_Descriptor, m_Descriptor.graphicsFormat);
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TempRT0, desc, name: "_TempRT0");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TempRT1, desc, name: "_TempRT1");
            var tempRT0 = renderGraph.ImportTexture(m_TempRT0);
            var tempRT1 = renderGraph.ImportTexture(m_TempRT1);
            
            var cameraColorTarget = resourceData.activeColorTexture;
            var source = cameraColorTarget;
            var target = tempRT0;

            m_PassData ??= new();

            m_PassData.sourceTexture = source;
            m_PassData.destination = target;

            for (int index = 0; index < m_ActivePostProcessRenderers.Count; ++index)
            {
                var renderer = m_ActivePostProcessRenderers[index];
                
                if (!renderer.renderToCamera)
                {
                    // 不需要渲染到最终摄像机 就无所谓RT切换 (注意: 最终输出完全取决于内部 如果在队列最后一个 可能会导致RT没能切回摄像机)
                    renderer.DoRenderGraph(renderGraph, source, TextureHandle.nullHandle, frameData);
                
                    //如果最后一个是 renderToCamera 的话 
                    if (index == m_ActivePostProcessRenderers.Count - 1)
                    {
                        if (!renderer.dontCareSourceTargetCopy)
                        {
                            using (var builder = renderGraph.AddUnsafePass<PassData>(m_PassName, out var passData))
                            {
                                passData.sourceTexture = source;
                                builder.UseTexture(source);
                                passData.destination = cameraColorTarget;
                                builder.UseTexture(cameraColorTarget, AccessFlags.Write);
                                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                                {
                                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                                    var sourceTextureHdl = data.sourceTexture;
                                    var dst = data.destination;
                                    Blitter.BlitCameraTexture(cmd, sourceTextureHdl, dst);
                                });
                            }
                        }
                    }
                    
                    continue;
                }

                // --------------------------------------------------------------------------
                if (index == m_ActivePostProcessRenderers.Count - 1)
                {
                    // 最后一个 target 正常必须是 m_CameraColorTarget
                    // 如果 source == m_CameraColorTarget 则需要把 m_CameraColorTarget copyto RT
                    if (source.GetDescriptor(renderGraph).name == cameraColorTarget.GetDescriptor(renderGraph).name && 
                        !renderer.dontCareSourceTargetCopy)
                    {
                        // blit source: m_CameraColorTarget target: m_TempRT
                        // copy
                        // swap source: m_TempRT target: m_CameraColorTarget

                        using (var builder = renderGraph.AddUnsafePass<PassData>(m_PassName, out var passData))
                        {
                            passData.sourceTexture = source;
                            builder.UseTexture(source);
                            passData.destination = target;
                            builder.UseTexture(target, AccessFlags.Write);
                            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                            {
                                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                                var sourceTextureHdl = data.sourceTexture;
                                var dst = data.destination;
                                Blitter.BlitCameraTexture(cmd, sourceTextureHdl, dst);
                            });
                        }
                    }
                    CoreUtils.Swap(ref source, ref target);
                    target = cameraColorTarget;
                }
                else
                {
                    // 不是最后一个时 如果 target == m_CameraColorTarget 就改成非souce的那个RT
                    // source: lastRT target: nextRT
                    if (target.GetDescriptor(renderGraph).name == cameraColorTarget.GetDescriptor(renderGraph).name)
                    {
                        target = source.GetDescriptor(renderGraph).name == tempRT0.GetDescriptor(renderGraph).name ? tempRT1 : tempRT0;
                    }
                }
                
                m_PassData.destination = target;
                m_PassData.sourceTexture = source;
                
                //如何包起来
                using (new ProfilingScope(profilingSampler))
                {
                    renderer.DoRenderGraph(renderGraph, m_PassData.sourceTexture, m_PassData.destination, frameData);
                }

                CoreUtils.Swap(ref source, ref target);
                
            }
        }
    }
}
