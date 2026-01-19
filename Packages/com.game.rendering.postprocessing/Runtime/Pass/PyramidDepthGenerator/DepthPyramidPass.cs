using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Game.Core.PostProcessing
{
    /// <summary>
    /// 非Mip形式的HiZ 算法 照搬HDRP版本
    /// </summary>
    public class DepthPyramidPass : ScriptableRenderPass, IDisposable
    {
        private static readonly ProfilingSampler CopyDepthSampler = new("Copy Depth Buffer");
        private static readonly ProfilingSampler DepthPyramidSampler = new("Depth Pyramid");
        
        
        private readonly PostProcessData m_Data;

        public DepthPyramidPass(PostProcessData data)
        {
            profilingSampler = new ProfilingSampler(nameof(DepthPyramidPass));
            renderPassEvent = PostProcessingRenderPassEvent.DepthPyramidPass;
            m_Data = data;
            
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, this.profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                var mipChainSize = m_Data.DepthMipChainInfo.textureSize;
                var depthDescriptor = cameraTargetDescriptor;
                depthDescriptor.enableRandomWrite = true;
                depthDescriptor.width = mipChainSize.x;
                depthDescriptor.height = mipChainSize.y;
                depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
                depthDescriptor.depthBufferBits = 0;
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_Data.DepthPyramidRT, depthDescriptor, name: "CameraDepthBufferMipChain");
                cmd.SetGlobalTexture(PipelineShaderIDs._DepthPyramid, m_Data.DepthPyramidRT);
                
                // Copy Depth
                using (new ProfilingScope(cmd, CopyDepthSampler))
                {
                    var cameraDepth = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
                    m_Data.GPUCopy.SampleCopyChannel_xyzw2x(cmd, cameraDepth, m_Data.DepthPyramidRT,
                        new RectInt(0, 0, cameraTargetDescriptor.width, cameraTargetDescriptor.height));
                }
                // Depth Pyramid
                using (new ProfilingScope(cmd, DepthPyramidSampler))
                {
                    m_Data.MipGenerator.RenderMinDepthPyramid(cmd, m_Data.DepthPyramidRT, m_Data.DepthMipChainInfo);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
        }

        class CopyDepthPassData
        {
            public TextureHandle inputDepth;
            public TextureHandle outputDepth;
            public GPUCopy GPUCopy;
            public int width;
            public int height;
        }
        
        class GenerateDepthPyramidPassData
        {
            public TextureHandle DepthPyramidTexture;
            public PackedMipChainInfo MipInfo;
            public MipGenerator MipGenerator;
        }

        void CopyDepthBufferIfNeeded(RenderGraph renderGraph, RenderTextureDescriptor desc, TextureHandle depthTexture, TextureHandle outDepthTexture)
        {
            using (var builder = renderGraph.AddUnsafePass<CopyDepthPassData>("Copy depth buffer", out var passData, CopyDepthSampler))
            {
                passData.inputDepth = depthTexture;
                passData.outputDepth = outDepthTexture;
                
                passData.GPUCopy = m_Data.GPUCopy;
                passData.width = desc.width;
                passData.height = desc.height;
                
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(
                    (CopyDepthPassData data, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        // TODO: maybe we don't actually need the top MIP level?
                        // That way we could avoid making the copy, and build the MIP hierarchy directly.
                        // The downside is that our SSR tracing accuracy would decrease a little bit.
                        // But since we never render SSR at full resolution, this may be acceptable.

                        // TODO: reading the depth buffer with a compute shader will cause it to decompress in place.
                        // On console, to preserve the depth test performance, we must NOT decompress the 'm_CameraDepthStencilBuffer' in place.
                        // We should call decompressDepthSurfaceToCopy() and decompress it to 'm_CameraDepthBufferMipChain'.
                        data.GPUCopy.SampleCopyChannel_xyzw2x(cmd, data.inputDepth, data.outputDepth, new RectInt(0, 0, data.width, data.height));
                    });
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            var cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
            var mipChainSize = m_Data.DepthMipChainInfo.textureSize;
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.enableRandomWrite = true;
            depthDescriptor.width = mipChainSize.x;
            depthDescriptor.height = mipChainSize.y;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_Data.DepthPyramidRT, depthDescriptor, name: "CameraDepthBufferMipChain");
            var depthPyramidTexture = renderGraph.ImportTexture(m_Data.DepthPyramidRT);

            var camerDepthTexture = resourceData.cameraDepthTexture;
            
            // If the depth buffer hasn't been already copied by the decal or low res depth buffer pass, then we do the copy here.
            CopyDepthBufferIfNeeded(renderGraph, cameraTargetDescriptor, camerDepthTexture, depthPyramidTexture);
            
            using (var builder = renderGraph.AddUnsafePass<GenerateDepthPyramidPassData>("Generate Depth Buffer MIP Chain", out var passData, DepthPyramidSampler))
            {
                passData.DepthPyramidTexture = depthPyramidTexture;
                passData.MipInfo = m_Data.DepthMipChainInfo;
                passData.MipGenerator = m_Data.MipGenerator;
                builder.AllowPassCulling(false);
                builder.SetGlobalTextureAfterPass(passData.DepthPyramidTexture, PipelineShaderIDs._DepthPyramid);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(
                    (GenerateDepthPyramidPassData data, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.MipGenerator.RenderMinDepthPyramid(cmd, data.DepthPyramidTexture, data.MipInfo);
                    });
            }
        }
    }
}
