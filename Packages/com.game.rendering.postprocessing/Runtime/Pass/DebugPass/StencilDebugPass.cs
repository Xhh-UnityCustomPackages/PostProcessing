using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class StencilDebugPass : ScriptableRenderPass, IDisposable
    {
        static class ShaderConstants
        {
            public static readonly int Scale = Shader.PropertyToID("_Scale");
            public static readonly int Margin = Shader.PropertyToID("_Margin");
            
            public static readonly string StencilDebug = "_StencilDebugTexture";
            public static readonly string Stencil = "_StencilTexture";
            public static readonly string CameraColor = "_CameraColorTexture";
        }

        private ComputeShader m_DebugCS;
        private int m_DebugKernel;
        private float scale, margin;
        private readonly ProfilingSampler debugSampler = new(nameof(StencilDebugPass));
        private RTHandle m_DebugRTHandle;
        
        
        public StencilDebugPass(ComputeShader mDebugCsShader)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            m_DebugCS = mDebugCsShader;
            m_DebugKernel = m_DebugCS.FindKernel("StencilDebug");
        }

        public void Setup(float debugScale, float debugMargin)
        {
            scale = debugScale;
            margin = debugMargin;
        }
        
        public void Dispose()
        {
            m_DebugRTHandle?.Release();
        }
        

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.enableRandomWrite = true;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_DebugRTHandle, desc);
            
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, debugSampler))
            {
                var colorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
                var stencilHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;
                var debugHandle = m_DebugRTHandle;

                cmd.SetComputeFloatParam(m_DebugCS, ShaderConstants.Scale, scale);
                cmd.SetComputeFloatParam(m_DebugCS, ShaderConstants.Margin, margin);

                cmd.SetComputeTextureParam(m_DebugCS, m_DebugKernel, ShaderConstants.CameraColor, colorHandle, 0);
                cmd.SetComputeTextureParam(m_DebugCS, m_DebugKernel, ShaderConstants.Stencil, stencilHandle, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(m_DebugCS, m_DebugKernel, ShaderConstants.StencilDebug, debugHandle);

                int threadGroupX = PostProcessingUtils.DivRoundUp(renderingData.cameraData.cameraTargetDescriptor.width, 8);
                int threadGroupY = PostProcessingUtils.DivRoundUp(renderingData.cameraData.cameraTargetDescriptor.height, 8);
                cmd.DispatchCompute(m_DebugCS, m_DebugKernel, threadGroupX, threadGroupY, 1);

                Blit(cmd,debugHandle,colorHandle);
                // Blitter.BlitTexture(cmd, debugHandle, new Vector4(1, 1, 0, 0), 0, false);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private class StencilDebugPassData
        {
            public ComputeShader debugCS;
            public int debugKernel;
            public float scale, margin;
            public int width, height;
            public TextureHandle colorTexture;
            public TextureHandle depthTexture;
            public TextureHandle debugTexture;
        }
        
        private class FinalBlitPassData
        {
            internal TextureHandle Source;
            internal TextureHandle Destination;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            
            var desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.enableRandomWrite = true;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_DebugRTHandle, desc);
            
            var debugTexture = renderGraph.ImportTexture(m_DebugRTHandle);
            
            using (var builder = renderGraph.AddComputePass<StencilDebugPassData>("Stencil Debug Pass", out var passData, debugSampler))
            {
                passData.debugCS = m_DebugCS;
                passData.debugKernel = m_DebugKernel;
                
                var colorHandle = resource.cameraColor;
                builder.UseTexture(colorHandle);
                passData.colorTexture = colorHandle;

                var depthHandle = resource.cameraDepth;
                builder.UseTexture(depthHandle);
                passData.depthTexture = depthHandle;

                
                builder.UseTexture(debugTexture);
                passData.debugTexture = debugTexture;

                passData.scale = scale;
                passData.margin = margin;
                passData.width = cameraData.cameraTargetDescriptor.width;
                passData.height = cameraData.cameraTargetDescriptor.height;

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (StencilDebugPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var cs = data.debugCS;
                    var kernel = data.debugKernel;
                    cmd.SetComputeFloatParam(cs, ShaderConstants.Scale, data.scale);
                    cmd.SetComputeFloatParam(cs, ShaderConstants.Margin, data.margin);
                    
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants.CameraColor, data.colorTexture, 0);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants.Stencil, data.depthTexture, 0, RenderTextureSubElement.Stencil);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants.StencilDebug, data.debugTexture);
                    
                    int threadGroupX = PostProcessingUtils.DivRoundUp(data.width, 8);
                    int threadGroupY = PostProcessingUtils.DivRoundUp(data.height, 8);
                    cmd.DispatchCompute(cs, kernel, threadGroupX, threadGroupY, 1);
                });
            }

            // Stage 3: Final blit to camera target
            using (var builder = renderGraph.AddRasterRenderPass<FinalBlitPassData>("Stencil Debug Final Blit", out var blitPassData, new ProfilingSampler("Stencil Debug Final Blit")))
            {
                builder.UseTexture(debugTexture);
                blitPassData.Source = debugTexture;
                builder.SetRenderAttachment(resource.activeColorTexture, 0);
                blitPassData.Destination = resource.activeColorTexture;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (FinalBlitPassData data, RasterGraphContext context) =>
                {
                    // Blit debug output to camera target
                    Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }
        }

       
    }
}