using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class StencilDebugPass : ScriptableRenderPass, IDisposable
    {
        static class ShaderConstants
        {
            public static readonly int Scale = Shader.PropertyToID("_Scale");
            public static readonly int Margin = Shader.PropertyToID("_Margin");
            
            public static readonly string StencilDebug = "_StencilDebugTexture";
            public static readonly string Stencil = "_StencilTexture";
            public static readonly string CameraColor = "_CameraColorTexture";
        }

        private ComputeShader debug;
        private int debugKernel;
        private float scale, margin;
        private readonly ProfilingSampler debugSampler = new(nameof(StencilDebugPass));
        private RTHandle cameraDepthRTHandle;
        private RTHandle debugRTHandle;
        
        private static int DivRoundUp(int x, int y) => (x + y - 1) / y;
        
        public StencilDebugPass(ComputeShader debugShader)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            debug = debugShader;
            debugKernel = debug.FindKernel("StencilDebug");
        }

        public void Setup(float debugScale, float debugMargin)
        {
            scale = debugScale;
            margin = debugMargin;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.enableRandomWrite = true;

            RenderingUtils.ReAllocateHandleIfNeeded(ref debugRTHandle, desc);

            cameraDepthRTHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, debugSampler))
            {
                var colorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
                var stencilHandle = cameraDepthRTHandle;
                var debugHandle = debugRTHandle;

                cmd.SetComputeFloatParam(debug, ShaderConstants.Scale, scale);
                cmd.SetComputeFloatParam(debug, ShaderConstants.Margin, margin);

                cmd.SetComputeTextureParam(debug, debugKernel, ShaderConstants.CameraColor, colorHandle, 0);
                cmd.SetComputeTextureParam(debug, debugKernel, ShaderConstants.Stencil, stencilHandle, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(debug, debugKernel, ShaderConstants.StencilDebug, debugHandle);

                cmd.DispatchCompute(debug, debugKernel, DivRoundUp(renderingData.cameraData.cameraTargetDescriptor.width, 8), DivRoundUp(renderingData.cameraData.cameraTargetDescriptor.height, 8), 1);

                Blit(cmd,debugHandle,colorHandle);
                // Blitter.BlitTexture(cmd, debugHandle, new Vector4(1, 1, 0, 0), 0, false);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public void Dispose()
        {
            debugRTHandle?.Release();
        }
    }
}