using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Game.Core.PostProcessing
{
    /// <summary>
    /// 非Mip形式的HiZ 算法 照搬HDRP版本
    /// </summary>
    public class DepthPyramidPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler CopyDepthSampler = new("Copy Depth Buffer");
        private static readonly ProfilingSampler DepthPyramidSampler = new("Depth Pyramid");
        
        private static RTHandle m_HiZDepthRT;
        
        private readonly PostProcessFeatureContext m_Context;
        public static RTHandle HiZDepthRT => m_HiZDepthRT;

        public DepthPyramidPass(PostProcessFeatureContext context)
        {
            profilingSampler = new ProfilingSampler(nameof(DepthPyramidPass));
            renderPassEvent = PostProcessingRenderPassEvent.DepthPyramidPass;
            m_Context = context;
        }
        
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            var mipChainSize = m_Context.MipChainInfo.textureSize;
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.enableRandomWrite = true;
            depthDescriptor.width = mipChainSize.x;
            depthDescriptor.height = mipChainSize.y;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_HiZDepthRT, depthDescriptor, name: "CameraDepthBufferMipChain");
            cmd.SetGlobalTexture(PipelineShaderIDs._DepthPyramid, m_HiZDepthRT);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, this.profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                
                // Copy Depth
                using (new ProfilingScope(cmd, CopyDepthSampler))
                {
                    var cameraDepth = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
                    m_Context.GPUCopy.SampleCopyChannel_xyzw2x(cmd, cameraDepth, m_HiZDepthRT,
                        new RectInt(0, 0, cameraTargetDescriptor.width, cameraTargetDescriptor.height));
                }
                // Depth Pyramid
                using (new ProfilingScope(cmd, DepthPyramidSampler))
                {
                    m_Context.MipGenerator.RenderMinDepthPyramid(cmd, m_HiZDepthRT, m_Context.MipChainInfo);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            m_HiZDepthRT?.Release();
        }
        
    }
}
