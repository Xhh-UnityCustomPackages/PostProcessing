using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ColorPyramidPass : ScriptableRenderPass
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
            if (m_Context.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain) == null)
            {
                m_Context.AllocHistoryFrameRT((int)FrameHistoryType.ColorBufferMipChain, HistoryBufferAllocatorFunction, 1);
            }
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
            var camera = renderingData.cameraData.camera;
            var cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Color Pyramid
                var colorPyramidRT = m_Context.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);
                if (colorPyramidRT == null)
                {
                    return;
                }

                cmd.SetGlobalTexture(PipelineShaderIDs._ColorPyramidTexture, colorPyramidRT);
                Vector2Int pyramidSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                m_Context.ColorPyramidHistoryMipCount =
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
    }
}