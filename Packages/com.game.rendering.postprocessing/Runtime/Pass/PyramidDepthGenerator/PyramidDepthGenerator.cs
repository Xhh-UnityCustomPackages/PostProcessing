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

        public static class PyramidDepthShaderIDs
        {
            public static int PrevMipDepth = Shader.PropertyToID("_PrevMipDepth");
            public static int HierarchicalDepth = Shader.PropertyToID("_HierarchicalDepth");
            public static int PrevCurr_InvSize = Shader.PropertyToID("_PrevCurr_Inverse_Size");
        }

        const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
        const int k_DepthBufferBits = 32;

        private ComputeShader m_Shader;
        private RTHandle m_HizRT;
        private RTHandle m_HizRT1;
        private int m_MipCount;
        private RTHandle[] m_PyramidMipIDs;

        public PyramidDepthGenerator(ComputeShader shader, in int maxMipCount = 10)
        {
            base.profilingSampler = new ProfilingSampler(nameof(PyramidDepthGenerator));
            renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights;
            m_Shader = shader;
            m_MipCount = maxMipCount;

            m_PyramidMipIDs = new RTHandle[m_MipCount];

        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var depthDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            depthDescriptor.useMipMap = true;
            depthDescriptor.mipCount = m_MipCount;
            depthDescriptor.msaaSamples = 1;

            // if (this.renderingModeActual != RenderingMode.Deferred)
            // {
            //     depthDescriptor.graphicsFormat = GraphicsFormat.None;
            //     depthDescriptor.depthStencilFormat = k_DepthStencilFormat;
            //     depthDescriptor.depthBufferBits = k_DepthBufferBits;
            // }
            // else
            {
                depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
                depthDescriptor.depthStencilFormat = GraphicsFormat.None;
                depthDescriptor.depthBufferBits = 0;
            }

            RenderingUtils.ReAllocateIfNeeded(ref m_HizRT, depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_HizDepthTexture");
            RenderingUtils.ReAllocateIfNeeded(ref m_HizRT1, depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_HizDepthTexture1");

            //---------------------------------
            depthDescriptor.enableRandomWrite = true;
            // depthDescriptor.useMipMap = false;
            for (int i = 0; i < m_MipCount; ++i)
            {
                depthDescriptor.width /= 2;
                depthDescriptor.height /= 2;
                if (depthDescriptor.width < 1 || depthDescriptor.height < 1) break;
                RenderingUtils.ReAllocateIfNeeded(ref m_PyramidMipIDs[i], depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_SSSRDepthMip" + i);
            }
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
            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;

            int2 pyramidSize = new int2(width, height);
            int2 lastPyramidSize = pyramidSize;

#if UNITY_2023_1_18
            RTHandle lastPyramidDepthTexture = renderingData.cameraData.renderer.cameraDepthTargetHandle;

#else
            RenderTargetIdentifier lastPyramidDepthTexture = Shader.GetGlobalTexture("_CameraDepthTexture");
            cmd.CopyTexture(lastPyramidDepthTexture, 0, 0, m_HizRT, 0, 0);
#endif

            // Debug.LogError($"lastPyramidDepthTexture:{lastPyramidDepthTexture}");


            for (int i = 0; i < m_MipCount; ++i)
            {
                pyramidSize /= 2;
                int dispatchSizeX = Mathf.CeilToInt(pyramidSize.x / 8);
                int dispatchSizeY = Mathf.CeilToInt(pyramidSize.y / 8);

                if (dispatchSizeX < 1 || dispatchSizeY < 1) break;

                cmd.SetComputeVectorParam(m_Shader, PyramidDepthShaderIDs.PrevCurr_InvSize, new float4(1.0f / pyramidSize.x, 1.0f / pyramidSize.y, 1.0f / lastPyramidSize.x, 1.0f / lastPyramidSize.y));
                cmd.SetComputeTextureParam(m_Shader, 0, PyramidDepthShaderIDs.PrevMipDepth, lastPyramidDepthTexture);
                cmd.SetComputeTextureParam(m_Shader, 0, PyramidDepthShaderIDs.HierarchicalDepth, m_PyramidMipIDs[i]);
                cmd.DispatchCompute(m_Shader, 0, Mathf.CeilToInt(pyramidSize.x / 8), Mathf.CeilToInt(pyramidSize.y / 8), 1);
                cmd.CopyTexture(m_PyramidMipIDs[i], 0, 0, m_HizRT, 0, i + 1);

                lastPyramidSize = pyramidSize;
                lastPyramidDepthTexture = m_PyramidMipIDs[i];
            }

            // cmd.Blit(m_HizRT, m_HizRT1);
        }
    }
}
