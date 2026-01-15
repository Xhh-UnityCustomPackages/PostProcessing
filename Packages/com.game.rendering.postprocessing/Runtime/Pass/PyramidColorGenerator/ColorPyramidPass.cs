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
        private readonly PostProcessFeatureContext m_Context;
        
        public ColorPyramidPass(PostProcessFeatureContext context)
        {
            profilingSampler = new ProfilingSampler(nameof(ColorPyramidPass));
            renderPassEvent = PostProcessingRenderPassEvent.ColorPyramidPass;
            m_Context = context;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var postProcessCamera = m_Context.GetPostProcessCamera(renderingData.cameraData.camera);
            if (postProcessCamera == null) return;
            if (postProcessCamera.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain) == null)
            {
                postProcessCamera.AllocHistoryFrameRT((int)FrameHistoryType.ColorBufferMipChain, HistoryBufferAllocatorFunction, 1);
            }
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
            var postProcessCamera = m_Context.GetPostProcessCamera(renderingData.cameraData.camera);
            if (postProcessCamera == null) return;
            var camera = renderingData.cameraData.camera;
            var cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Color Pyramid
                var colorPyramidRT = postProcessCamera.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);

                cmd.SetGlobalTexture(PipelineShaderIDs._ColorPyramidTexture, colorPyramidRT);
                Vector2Int pyramidSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                postProcessCamera.ColorPyramidHistoryMipCount =
                    m_Context.MipGenerator.RenderColorGaussianPyramid(cmd, pyramidSize, cameraColor, colorPyramidRT.rt);
                
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
            public PostProcessCamera hdCamera;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            
            var postProcessCamera = m_Context.GetPostProcessCamera(cameraData.camera);
            if (postProcessCamera == null) return;
            if (postProcessCamera.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain) == null)
            {
                postProcessCamera.AllocHistoryFrameRT((int)FrameHistoryType.ColorBufferMipChain, HistoryBufferAllocatorFunction, 1);
            }
            
            var colorPyramidRT = postProcessCamera.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);
            var colorPyramidHandle = renderGraph.ImportTexture(colorPyramidRT);
            var inputColor = resourceData.cameraColor;
            var camera = cameraData.camera;
            
            using (var builder = renderGraph.AddUnsafePass<GenerateColorPyramidData>("Color Gaussian MIP Chain", out var passData))
            {
                passData.mipGenerator = m_Context.MipGenerator;
                passData.colorPyramid = colorPyramidHandle;
                passData.inputColor = inputColor;
                passData.hdCamera = postProcessCamera;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(
                    (GenerateColorPyramidData data, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        Vector2Int pyramidSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                        data.hdCamera.ColorPyramidHistoryMipCount = data.mipGenerator.RenderColorGaussianPyramid(cmd, pyramidSize, data.inputColor, data.colorPyramid);
                        // TODO RENDERGRAPH: We'd like to avoid SetGlobals like this but it's required by custom passes currently.
                        // We will probably be able to remove those once we push custom passes fully to render graph.
                        cmd.SetGlobalTexture(PipelineShaderIDs._ColorPyramidTexture, data.colorPyramid);
                    });
            }
        }
    }
}