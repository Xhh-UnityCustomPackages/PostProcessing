using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;
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
        
        private static RTHandle m_HiZDepthRT;
        
        private readonly PostProcessData m_Data;
        public static RTHandle HiZDepthRT => m_HiZDepthRT;

        public DepthPyramidPass(PostProcessData data)
        {
            profilingSampler = new ProfilingSampler(nameof(DepthPyramidPass));
            renderPassEvent = PostProcessingRenderPassEvent.DepthPyramidPass;
            m_Data = data;
        }
        
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            var mipChainSize = m_Data.DepthMipChainInfo.textureSize;
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
                    m_Data.GPUCopy.SampleCopyChannel_xyzw2x(cmd, cameraDepth, m_HiZDepthRT,
                        new RectInt(0, 0, cameraTargetDescriptor.width, cameraTargetDescriptor.height));
                }
                // Depth Pyramid
                using (new ProfilingScope(cmd, DepthPyramidSampler))
                {
                    m_Data.MipGenerator.RenderMinDepthPyramid(cmd, m_HiZDepthRT, m_Data.DepthMipChainInfo);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            m_HiZDepthRT?.Release();
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
            public TextureHandle depthTexture;
            public PackedMipChainInfo mipInfo;
            public MipGenerator mipGenerator;
        }

        void CopyDepthBufferIfNeeded(RenderGraph renderGraph, Camera hdCamera, TextureHandle depthTexture, TextureHandle outDepthTexture)
        {
            using (var builder = renderGraph.AddUnsafePass<CopyDepthPassData>("Copy depth buffer", out var passData, CopyDepthSampler))
            {
                passData.inputDepth = depthTexture;
                passData.outputDepth = outDepthTexture;
                
                passData.GPUCopy = m_Data.GPUCopy;
                passData.width = hdCamera.pixelWidth;
                passData.height = hdCamera.pixelHeight;
                
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
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_HiZDepthRT, depthDescriptor, name: "CameraDepthBufferMipChain");
            var depthPyramidTexture = renderGraph.ImportTexture(m_HiZDepthRT);

            var camerDepthTexture = resourceData.cameraDepthTexture;
            
            // If the depth buffer hasn't been already copied by the decal or low res depth buffer pass, then we do the copy here.
            CopyDepthBufferIfNeeded(renderGraph, cameraData.camera, camerDepthTexture, depthPyramidTexture);
            
            using (var builder = renderGraph.AddUnsafePass<GenerateDepthPyramidPassData>("Generate Depth Buffer MIP Chain", out var passData, DepthPyramidSampler))
            {
                passData.depthTexture = depthPyramidTexture;
                passData.mipInfo = m_Data.DepthMipChainInfo;
                passData.mipGenerator = m_Data.MipGenerator;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(
                    (GenerateDepthPyramidPassData data, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.mipGenerator.RenderMinDepthPyramid(cmd, data.depthTexture, data.mipInfo);
                    });

                // m_HiZDepthRT = passData.depthTexture;
            }
        }
    }
}
