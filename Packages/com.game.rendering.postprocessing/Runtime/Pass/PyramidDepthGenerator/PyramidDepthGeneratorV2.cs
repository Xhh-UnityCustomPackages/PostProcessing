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
    public class PyramidDepthGeneratorV2 : ScriptableRenderPass
    {

        static class ShaderProperties
        {
            public static readonly int _DepthMipChain = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _DepthPyramid = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _DepthPyramidMipLevelOffsets = MemberNameHelpers.ShaderPropertyID();
        }

        private static readonly ProfilingSampler CopyDepthSampler = new("Copy Depth Buffer");
        private static readonly ProfilingSampler DepthPyramidSampler = new("Depth Pyramid");
        
        private static RTHandle m_HiZDepthRT;
        private PackedMipChainInfo m_MipChainInfo;
        private GPUCopy gpuCopy;
        private MipGenerator mipGenerator;
        public static RTHandle HiZDepthRT => m_HiZDepthRT;

        public PyramidDepthGeneratorV2(GPUCopy gpuCopy, MipGenerator mipGenerator)
        {
            profilingSampler = new ProfilingSampler(nameof(PyramidDepthGenerator));
            renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights - 1;
            this.gpuCopy = gpuCopy;
            this.mipGenerator = mipGenerator;
            m_MipChainInfo.Allocate();
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            Vector2Int nonScaledViewport = new Vector2Int(cameraTargetDescriptor.width, cameraTargetDescriptor.height);
            m_MipChainInfo.ComputePackedMipChainInfo(nonScaledViewport, 0);
            var mipChainSize = m_MipChainInfo.textureSize;
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.enableRandomWrite = true;
            depthDescriptor.width = mipChainSize.x;
            depthDescriptor.height = mipChainSize.y;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_HiZDepthRT, depthDescriptor, name: "CameraDepthBufferMipChain");
            cmd.SetGlobalTexture(ShaderProperties._DepthPyramid, m_HiZDepthRT);
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
                    var _depthStencilBuffer = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
                    gpuCopy.SampleCopyChannel_xyzw2x(cmd, _depthStencilBuffer, m_HiZDepthRT,
                        new RectInt(0, 0, cameraTargetDescriptor.width, cameraTargetDescriptor.height));
                }
                
                using (new ProfilingScope(cmd, DepthPyramidSampler))
                {
                    mipGenerator.RenderMinDepthPyramid(cmd, m_HiZDepthRT, m_MipChainInfo);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        
    }
}
