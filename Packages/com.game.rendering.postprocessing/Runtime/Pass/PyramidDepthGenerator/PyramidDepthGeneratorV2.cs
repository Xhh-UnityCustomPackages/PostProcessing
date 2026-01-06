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
    /// 非Mip形式的HiZ 算法
    /// </summary>
    public class PyramidDepthGeneratorV2 : ScriptableRenderPass
    {

        static class CopyTextureKernelProperties
        {
            
        }

        private ComputeShader m_ComputeShader;
        private static RTHandle m_HiZDepthRT;


        public static RTHandle HiZDepthRT => m_HiZDepthRT;

        public PyramidDepthGeneratorV2()
        {
            base.profilingSampler = new ProfilingSampler(nameof(PyramidDepthGenerator));
            renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights - 1;
            
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<PyramidDepthGeneratorResources>();
            m_ComputeShader = runtimeShaders.hiZCS;
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, this.profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

              
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        
    }
}
