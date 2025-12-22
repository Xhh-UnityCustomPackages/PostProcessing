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
            var tempRT0 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_TempRT0", true, FilterMode.Bilinear);
            var tempRT1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_TempRT1", true, FilterMode.Bilinear);

            ref ScriptableRenderer renderer = ref cameraData.renderer;
            var cameraColorTarget = resourceData.activeColorTexture;
            var source = cameraColorTarget;
            var target = tempRT0;

            if (m_PassData == null)
            {
                m_PassData = new();
            }

            m_PassData.sourceTexture = source;
            m_PassData.destination = target;

            for (int index = 0; index < m_ActivePostProcessRenderers.Count; ++index)
            {
                var postProcessRenderer = m_ActivePostProcessRenderers[index];
                
                if (!postProcessRenderer.renderToCamera)
                {
                    // 不需要渲染到最终摄像机 就无所谓RT切换 (注意: 最终输出完全取决于内部 如果在队列最后一个 可能会导致RT没能切回摄像机)
                    postProcessRenderer.DoRenderGraph(renderGraph, source, TextureHandle.nullHandle, frameData);
                
                    continue;
                }

                // --------------------------------------------------------------------------
                if (index == m_ActivePostProcessRenderers.Count - 1)
                {
                    // 最后一个 target 正常必须是 m_CameraColorTarget
                    // 如果 source == m_CameraColorTarget 则需要把 m_CameraColorTarget copyto RT
                    if (source.Equals(cameraColorTarget) && !postProcessRenderer.dontCareSourceTargetCopy)
                    {
                        // blit source: m_CameraColorTarget target: m_TempRT
                        // copy
                        // swap source: m_TempRT target: m_CameraColorTarget

                        using (var builder = renderGraph.AddUnsafePass<PassData>("PostProcess Swap", out var passData, profilingSampler))
                        {
                            passData.sourceTexture = source;
                            builder.UseTexture(source);
                            passData.destination = target;
                            builder.UseTexture(target);
                            builder.AllowPassCulling(false);
                            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                            {
                                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                                RTHandle sourceTextureHdl = data.sourceTexture;
                                RTHandle dst = data.destination;
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
                    if (target.Equals(cameraColorTarget))
                    {
                        target = source.Equals(tempRT0) ? tempRT1 : tempRT0;
                    }
                }
                
                m_PassData.destination = target;
                m_PassData.sourceTexture = source;
                
                postProcessRenderer.DoRenderGraph(renderGraph, m_PassData.sourceTexture, m_PassData.destination, frameData);
                CoreUtils.Swap(ref source, ref target);
                
            }
        }
    }
}
