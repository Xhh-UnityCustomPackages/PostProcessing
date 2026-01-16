using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ColorPyramidPass : ScriptableRenderPass, IDisposable
    {
        private readonly PostProcessData m_Data;
        
        public ColorPyramidPass(PostProcessData data)
        {
            profilingSampler = new ProfilingSampler(nameof(ColorPyramidPass));
            renderPassEvent = PostProcessingRenderPassEvent.ColorPyramidPass;
            m_Data = data;
        }

        public void Dispose()
        {
        }

        // BufferedRTHandleSystem API expects an allocator function. We define it here.
        private static RTHandle HistoryBufferAllocatorFunction(string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;
            return rtHandleSystem.Alloc(Vector2.one,
                // TextureXR.slices, 
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                // dimension: TextureXR.dimension,
                enableRandomWrite: true, useMipMap: true, autoGenerateMips: false, 
                // useDynamicScale: true,
                name: $"{viewName}_CameraColorBufferMipChain{frameIndex}");
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Data.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain) == null)
            {
                m_Data.AllocHistoryFrameRT((int)FrameHistoryType.ColorBufferMipChain, HistoryBufferAllocatorFunction, 1);
            }
            
            var camera = renderingData.cameraData.camera;
            var cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Color Pyramid
                var colorPyramidRT = m_Data.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);

                cmd.SetGlobalTexture(PipelineShaderIDs._ColorPyramidTexture, colorPyramidRT);
                Vector2Int pyramidSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                m_Data.ColorPyramidHistoryMipCount =
                    m_Data.MipGenerator.RenderColorGaussianPyramid(cmd, pyramidSize, cameraColor, colorPyramidRT.rt);
                
                // Copy History if needed
                // if (_rendererData.RequireHistoryDepthNormal)
                // {
                //     using (new ProfilingScope(cmd, _copyHistorySampler))
                //     {
                //         _rendererData.CopyHistoryGraphicsBuffers(cmd, ref renderingData);
                //     }
                // }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        class GenerateColorPyramidData
        {
            public TextureHandle colorPyramid;
            public TextureHandle inputColor;
            public MipGenerator mipGenerator;
            public PostProcessData HdData;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            
            if (m_Data.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain) == null)
            {
                m_Data.AllocHistoryFrameRT((int)FrameHistoryType.ColorBufferMipChain, HistoryBufferAllocatorFunction, 1);
            }
            
            var colorPyramidRT = m_Data.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);
            var colorPyramidHandle = renderGraph.ImportTexture(colorPyramidRT);
            var inputColor = resourceData.cameraColor;
            var camera = cameraData.camera;
            
            using (var builder = renderGraph.AddUnsafePass<GenerateColorPyramidData>("Color Gaussian MIP Chain", out var passData))
            {
                passData.mipGenerator = m_Data.MipGenerator;
                passData.colorPyramid = colorPyramidHandle;
                passData.inputColor = inputColor;
                passData.HdData = m_Data;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(
                    (GenerateColorPyramidData data, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        Vector2Int pyramidSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                        data.HdData.ColorPyramidHistoryMipCount = data.mipGenerator.RenderColorGaussianPyramid(cmd, pyramidSize, data.inputColor, data.colorPyramid);
                        // TODO RENDERGRAPH: We'd like to avoid SetGlobals like this but it's required by custom passes currently.
                        // We will probably be able to remove those once we push custom passes fully to render graph.
                        cmd.SetGlobalTexture(PipelineShaderIDs._ColorPyramidTexture, data.colorPyramid);
                    });
            }
        }
    }
}