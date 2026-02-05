using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class DiffuseShadowDenoisePass : ScriptableRenderPass, IDisposable
    {
        static class ShaderIDs
        {
            public static readonly int RaytracingLightAngle = Shader.PropertyToID("_RaytracingLightAngle");
            public static readonly int CameraFOV = Shader.PropertyToID("_CameraFOV");
            public static readonly int DenoiseOutputTextureRW = Shader.PropertyToID("_DenoiseOutputTextureRW");
            public static readonly int DepthTexture = Shader.PropertyToID("_DepthTexture");
            public static readonly int DenoiserFilterRadius = Shader.PropertyToID("_DenoiserFilterRadius");
            public static readonly int DenoiseInputTexture = Shader.PropertyToID("_DenoiseInputTexture");
            public static readonly int NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");
        }
        
   
        
        private readonly ProfilingSampler m_ProfilingSampler;

        private ComputeShader m_DiffuseShadowDenoiserCS;
        private int bilateralFilterHSingleDirectionalKernel;
        private int bilateralFilterVSingleDirectionalKernel;
        
        private RTHandle m_IntermediateBuffer;
        private RTHandle m_ContactShadowsDenoisedRT;
        private ContactShadowsRenderer m_ContactShadowsRenderer;
        
        public DiffuseShadowDenoisePass(ContactShadowsRenderer renderer)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer + 5;
            m_ProfilingSampler = new ProfilingSampler("Diffuse Shadow Denoise");
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ContactShadowResources>();
            m_DiffuseShadowDenoiserCS = runtimeShaders.diffuseShadowDenoiserCS;
            bilateralFilterHSingleDirectionalKernel = m_DiffuseShadowDenoiserCS.FindKernel("BilateralFilterHSingleDirectional");
            bilateralFilterVSingleDirectionalKernel = m_DiffuseShadowDenoiserCS.FindKernel("BilateralFilterVSingleDirectional");
            m_ContactShadowsRenderer = renderer;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            
            // Temporary buffers
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_IntermediateBuffer, desc, name: "Intermediate buffer");
            // Output buffer
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_ContactShadowsDenoisedRT, desc, name: "Denoised Buffer");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Prepare data
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;
            var renderer = cameraData.renderer;
            var contactShadows = VolumeManager.instance.stack.GetComponent<ContactShadows>();

            var _depthStencilBuffer = UniversalRenderingUtility.GetDepthTexture(renderer);
            if (_depthStencilBuffer == null) return;

            var cameraFov = camera.fieldOfView * Mathf.PI / 180.0f;
            // Convert the angular diameter of the directional light to radians (from degrees)
            const float angularDiameter = 2.5f;
            var lightAngle = angularDiameter * Mathf.PI / 180.0f;
            var kernelSize = contactShadows.filterSizeTraced.value;

            int actualWidth = cameraData.cameraTargetDescriptor.width;
            int actualHeight = cameraData.cameraTargetDescriptor.height;
            // Evaluate the dispatch parameters
            int numTilesX = PostProcessingUtils.DivRoundUp(actualWidth, 8);
            int numTilesY = PostProcessingUtils.DivRoundUp(actualHeight, 8);

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // TODO: Add distance based denoise support
                // Raise the distance based denoiser keyword
                // CoreUtils.SetKeyword(cmd, "DISTANCE_BASED_DENOISER", true);

                var computeShader = m_DiffuseShadowDenoiserCS;
                // Bind input uniforms for both dispatches
                cmd.SetComputeFloatParam(computeShader, ShaderIDs.RaytracingLightAngle, lightAngle);
                cmd.SetComputeIntParam(computeShader, ShaderIDs.DenoiserFilterRadius, kernelSize);
                cmd.SetComputeFloatParam(computeShader, ShaderIDs.CameraFOV, cameraFov);
                int kernel;

                kernel = bilateralFilterHSingleDirectionalKernel;
                // Bind Input Textures
                cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DepthTexture, _depthStencilBuffer);
                computeShader.SetTextureFromGlobal(kernel, ShaderIDs.NormalBufferTexture, PipelineShaderIDs._CameraNormalsTexture);
                computeShader.SetTextureFromGlobal(kernel, ShaderIDs.DenoiseInputTexture, PipelineShaderIDs._ContactShadowMap);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, ShaderIDs.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DenoiseOutputTextureRW, m_IntermediateBuffer);

                // Do the Horizontal pass
                cmd.DispatchCompute(computeShader, kernel, numTilesX, numTilesY, 1);

                kernel = bilateralFilterVSingleDirectionalKernel;
                // Bind Input Textures
                cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DepthTexture, _depthStencilBuffer);
                computeShader.SetTextureFromGlobal(kernel, ShaderIDs.NormalBufferTexture, PipelineShaderIDs._CameraNormalsTexture);
                cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DenoiseInputTexture, m_IntermediateBuffer);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, ShaderIDs.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DenoiseOutputTextureRW, m_ContactShadowsDenoisedRT);

                // Do the Vertical pass
                cmd.DispatchCompute(computeShader, kernel, numTilesX, numTilesY, 1);

                Shader.SetGlobalTexture(PipelineShaderIDs._ContactShadowMap, m_ContactShadowsDenoisedRT);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private class DiffuseShadowDenoisePassData
        {
            public ComputeShader ShadowDenoiserCS;
            // Kernels that we are using
            public int bilateralFilterHSingleDirectionalKernel;
            public int bilateralFilterVSingleDirectionalKernel;

            public int numTilesX;
            public int numTilesY;
            
            public float lightAngle;
            public float cameraFov;
            public int kernelSize;

            public TextureHandle depthTexture;
            public TextureHandle intermediateBuffer;
            public TextureHandle contactShadowsDenoisedRT;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var contactShadows = VolumeManager.instance.stack.GetComponent<ContactShadows>();
            
            using (var builder = renderGraph.AddComputePass<DiffuseShadowDenoisePassData>(profilingSampler.name, out var passData, m_ProfilingSampler))
            {
                passData.depthTexture = resourceData.cameraDepthTexture;
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                
                builder.UseGlobalTexture(PipelineShaderIDs._ContactShadowMap);
                
                // Convert the angular diameter of the directional light to radians (from degrees)
                const float angularDiameter = 2.5f;
                passData.lightAngle = angularDiameter * Mathf.PI / 180.0f;
                passData.cameraFov = cameraData.camera.fieldOfView * Mathf.PI / 180.0f;
                passData.kernelSize = contactShadows.filterSizeTraced.value;
                
                var cs = m_DiffuseShadowDenoiserCS;
                passData.ShadowDenoiserCS = cs;
                passData.bilateralFilterHSingleDirectionalKernel = bilateralFilterHSingleDirectionalKernel;
                passData.bilateralFilterVSingleDirectionalKernel = bilateralFilterVSingleDirectionalKernel;
                
            
                var desc = cameraData.cameraTargetDescriptor;
                desc.enableRandomWrite = true;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            
                // Temporary buffers
                var intermediateBuffer = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_IntermediateTexture", false);
                // Output buffer
                var contactShadowsDenoisedRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "DenoisedTexture", false);
                
                passData.intermediateBuffer = intermediateBuffer;
                builder.UseTexture(intermediateBuffer, AccessFlags.ReadWrite);
                passData.contactShadowsDenoisedRT = contactShadowsDenoisedRT;
                builder.UseTexture(contactShadowsDenoisedRT, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(contactShadowsDenoisedRT, PipelineShaderIDs._ContactShadowMap);
                
                int actualWidth = cameraData.cameraTargetDescriptor.width;
                int actualHeight = cameraData.cameraTargetDescriptor.height;
                // Evaluate the dispatch parameters
                int numTilesX = PostProcessingUtils.DivRoundUp(actualWidth, 8);
                int numTilesY = PostProcessingUtils.DivRoundUp(actualHeight, 8);
                passData.numTilesX = numTilesX;
                passData.numTilesY = numTilesY;
                
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((DiffuseShadowDenoisePassData data, ComputeGraphContext context) => ExecutePass(data, context));
            }
        }

        void ExecutePass(DiffuseShadowDenoisePassData data, ComputeGraphContext context)
        {
            int numTilesX = data.numTilesX;
            int numTilesY = data.numTilesY;
            
            var cmd = context.cmd;
            var computeShader = data.ShadowDenoiserCS;
            // Bind input uniforms for both dispatches
            cmd.SetComputeFloatParam(computeShader, ShaderIDs.RaytracingLightAngle, data.lightAngle);
            cmd.SetComputeIntParam(computeShader, ShaderIDs.DenoiserFilterRadius, data.kernelSize);
            cmd.SetComputeFloatParam(computeShader, ShaderIDs.CameraFOV, data.cameraFov);
            int kernel;
            
            kernel = data.bilateralFilterHSingleDirectionalKernel;
            // Bind Input Textures
            cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DepthTexture, data.depthTexture);
            computeShader.SetTextureFromGlobal(kernel, ShaderIDs.NormalBufferTexture, PipelineShaderIDs._CameraNormalsTexture);
            computeShader.SetTexture(kernel, ShaderIDs.DenoiseInputTexture, m_ContactShadowsRenderer.ContactShadowsTexture);
            
            // Bind output textures
            cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DenoiseOutputTextureRW, data.intermediateBuffer);
                 
            // Do the Horizontal pass
            cmd.DispatchCompute(computeShader, kernel, numTilesX, numTilesY, 1);
            
            kernel = data.bilateralFilterVSingleDirectionalKernel;
            // Bind Input Textures
            cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DepthTexture, data.depthTexture);
            computeShader.SetTextureFromGlobal(kernel, ShaderIDs.NormalBufferTexture, PipelineShaderIDs._CameraNormalsTexture);
            cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DenoiseInputTexture, data.intermediateBuffer);
            
            // TODO: Add distance based denoise support
            // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, ShaderIDs.DistanceTexture, _distanceBuffer);
            
            // Bind output textures
            cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs.DenoiseOutputTextureRW, data.contactShadowsDenoisedRT);
            
            // Do the Vertical pass
            cmd.DispatchCompute(computeShader, kernel, numTilesX, numTilesY, 1);
                 
            // Shader.SetGlobalTexture(ShaderIDs.ContactShadowsRT, data.contactShadowsDenoisedRT);

        }

        public void Dispose()
        {
            m_IntermediateBuffer?.Release();
            m_ContactShadowsDenoisedRT?.Release();
        }
    }
}