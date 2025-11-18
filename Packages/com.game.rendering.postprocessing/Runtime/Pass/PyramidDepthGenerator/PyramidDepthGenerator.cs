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
    public class PyramidDepthGenerator : ScriptableRenderPass
    {

        public static class CopyTextureKernelProperties
        {
            public static readonly int SOURCE_TEXTURE = Shader.PropertyToID("source");
            public static readonly int DESTINATION_TEXTURE = Shader.PropertyToID("destination");
            public static readonly int SOURCE_SIZE_X = Shader.PropertyToID("sourceSizeX");
            public static readonly int SOURCE_SIZE_Y = Shader.PropertyToID("sourceSizeY");
            public static readonly int DESTINATION_SIZE_X = Shader.PropertyToID("destinationSizeX");
            public static readonly int DESTINATION_SIZE_Y = Shader.PropertyToID("destinationSizeY");
            public static readonly int REVERSE_Z = Shader.PropertyToID("reverseZ");
        }

        private RenderTextureDescriptor m_HiZDepthDesc;
        private RenderTextureDescriptor m_HiZMipDesc;
        private ComputeShader m_ComputeShader;
        private static RTHandle m_HiZDepthRT;
        private int m_HiZMipLevels;
        private RTHandle[] m_HiZMipsLevelRT;


        public static RTHandle HiZDepthRT => m_HiZDepthRT;

        public PyramidDepthGenerator(ComputeShader shader)
        {
            base.profilingSampler = new ProfilingSampler(nameof(PyramidDepthGenerator));
            renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights - 1;
            m_ComputeShader = shader;

        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;
            width = 1 << Math.Max((int)Math.Ceiling(Mathf.Log(width, 2) - 1.0f), 1);
            height = 1 << Math.Max((int)Math.Ceiling(Mathf.Log(height, 2) - 1.0f), 1);
            
            m_HiZMipLevels = (int)Mathf.Floor(Mathf.Log(width, 2f));
            
            if (m_HiZDepthDesc.width != width || m_HiZDepthDesc.height != height)
            {
                m_HiZDepthDesc = new RenderTextureDescriptor(width, height);
                m_HiZDepthDesc.useMipMap = true;
                m_HiZDepthDesc.autoGenerateMips = false;
                m_HiZDepthDesc.enableRandomWrite = true;
                m_HiZDepthDesc.colorFormat = RenderTextureFormat.RFloat;
                m_HiZDepthDesc.volumeDepth = 1;
                m_HiZDepthDesc.msaaSamples = 1;
                m_HiZDepthDesc.bindMS = false;
                m_HiZDepthDesc.dimension = TextureDimension.Tex2D;
            
                m_HiZMipDesc = m_HiZDepthDesc;
                m_HiZMipDesc.useMipMap = false;
            }
            
            
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_HiZDepthRT, m_HiZDepthDesc, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "HiZDepthRT");
            
          
            if (m_HiZMipsLevelRT == null || m_HiZMipsLevelRT.Length != m_HiZMipLevels)
            {
                if (m_HiZMipsLevelRT != null)
                {
                    for (int i = 0; i < m_HiZMipsLevelRT.Length; i++)
                    {
                        m_HiZMipsLevelRT[i].Release();
                    }
                }
            
                m_HiZMipsLevelRT = new RTHandle[m_HiZMipLevels];
            }
            
            for (int i = 0; i < m_HiZMipLevels; ++i)
            {
                width = width >> 1;
                height = height >> 1;
            
                if (width == 0) width = 1;
                if (height == 0) height = 1;
            
                m_HiZMipDesc.width = width;
                m_HiZMipDesc.height = height;
            
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_HiZMipsLevelRT[i], m_HiZMipDesc, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "HiZDepthRT_Mip_" + i);
            }
            
            ConfigureTarget(m_HiZDepthRT);

            Shader.SetGlobalTexture("_HizDepthTexture", m_HiZDepthRT);
            Shader.SetGlobalInt("_HizDepthTextureMipLevel", m_HiZMipLevels - 1);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.isPreviewCamera)
                return;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, this.profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                PyramidDepthUpdate(cmd, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        public void PyramidDepthUpdate(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var unityDepthTexture = renderingData.cameraData.renderer.cameraDepthTargetHandle;

            CopyTextureWithComputeShader(cmd, m_ComputeShader, unityDepthTexture, m_HiZDepthRT);

            for (var i = 0; i < m_HiZMipLevels - 1; ++i)
            {
                var tempRT = m_HiZMipsLevelRT[i];

                if (i == 0)
                    ReduceTextureWithComputeShader(cmd, m_ComputeShader, m_HiZDepthRT, tempRT);
                else
                    ReduceTextureWithComputeShader(cmd, m_ComputeShader, m_HiZMipsLevelRT[i - 1], tempRT);

                // CopyTextureWithComputeShader(cmd, m_ComputeShader, tempRT, m_HiZDepthRT, 0, i + 1, false);
                cmd.CopyTexture(tempRT, 0, 0, m_HiZDepthRT, 0, i + 1);
            }
        }



        public static void CopyTextureWithComputeShader(CommandBuffer cmd, ComputeShader computeShader, Texture source, Texture destination, int sourceMip = 0, int destinationMip = 0, bool reverseZ = true)
        {
            int kernelID = 0;
            cmd.SetComputeTextureParam(computeShader, kernelID, CopyTextureKernelProperties.SOURCE_TEXTURE, source, sourceMip);
            cmd.SetComputeTextureParam(computeShader, kernelID, CopyTextureKernelProperties.DESTINATION_TEXTURE, destination, destinationMip);

            cmd.SetComputeIntParam(computeShader, CopyTextureKernelProperties.SOURCE_SIZE_X, source.width);
            cmd.SetComputeIntParam(computeShader, CopyTextureKernelProperties.SOURCE_SIZE_Y, source.height);
            cmd.SetComputeIntParam(computeShader, CopyTextureKernelProperties.REVERSE_Z, reverseZ ? 1 : 0);

            float COMPUTE_SHADER_THREAD_COUNT_2D = 16.0f;
            int threadGroupX = Mathf.CeilToInt(source.width / COMPUTE_SHADER_THREAD_COUNT_2D);
            int threadGroupY = Mathf.CeilToInt(source.height / COMPUTE_SHADER_THREAD_COUNT_2D);
            cmd.DispatchCompute(computeShader, kernelID, threadGroupX, threadGroupY, 1);
        }

        public static void ReduceTextureWithComputeShader(CommandBuffer cmd, ComputeShader computeShader, Texture source, Texture destination, int sourceMip = 0, int destinationMip = 0)
        {
            int kernelID = 1;
            int sourceW = source.width;
            int sourceH = source.height;
            int destinationW = destination.width;
            int destinationH = destination.height;
           
            sourceW >>= sourceMip;
            sourceH >>= sourceMip;
            
            destinationW >>= destinationMip;
            destinationH >>= destinationMip;
            
            cmd.SetComputeTextureParam(computeShader, kernelID, CopyTextureKernelProperties.SOURCE_TEXTURE, source, sourceMip);
            cmd.SetComputeTextureParam(computeShader, kernelID, CopyTextureKernelProperties.DESTINATION_TEXTURE, destination, destinationMip);

            cmd.SetComputeIntParam(computeShader, CopyTextureKernelProperties.SOURCE_SIZE_X, sourceW);
            cmd.SetComputeIntParam(computeShader, CopyTextureKernelProperties.SOURCE_SIZE_Y, sourceH);
            cmd.SetComputeIntParam(computeShader, CopyTextureKernelProperties.DESTINATION_SIZE_X, destinationW);
            cmd.SetComputeIntParam(computeShader, CopyTextureKernelProperties.DESTINATION_SIZE_Y, destinationH);

            float COMPUTE_SHADER_THREAD_COUNT_2D = 16.0f;
            int threadGroupX = Mathf.CeilToInt(destinationW / COMPUTE_SHADER_THREAD_COUNT_2D);
            int threadGroupY = Mathf.CeilToInt(destinationH / COMPUTE_SHADER_THREAD_COUNT_2D);
            cmd.DispatchCompute(computeShader, kernelID, threadGroupX, threadGroupY, 1);
        }
    }
}
