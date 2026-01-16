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
        static class RayTracingShaderProperties
        {
            public static readonly int RaytracingLightAngle = Shader.PropertyToID("_RaytracingLightAngle");
            public static readonly int CameraFOV = Shader.PropertyToID("_CameraFOV");
            public static readonly int DenoiseOutputTextureRW = Shader.PropertyToID("_DenoiseOutputTextureRW");
            public static readonly int DepthTexture = Shader.PropertyToID("_DepthTexture");
            public static readonly int DenoiserFilterRadius = Shader.PropertyToID("_DenoiserFilterRadius");
            public static readonly int DenoiseInputTexture = Shader.PropertyToID("_DenoiseInputTexture");
            public static readonly int NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");
            public static readonly int ContactShadowsRT = Shader.PropertyToID("_ContactShadowMap");
            public static readonly int CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");
        }
        
   
        
        private readonly ProfilingSampler m_ProfilingSampler;
        
        private RTHandle m_IntermediateBuffer;
        private RTHandle m_ContactShadowsDenoisedRT;
        private DiffuseShadowDenoisePassData m_PassData;
        
        public DiffuseShadowDenoisePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer + 10;
            m_ProfilingSampler = new ProfilingSampler("Diffuse Shadow Denoise");
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ContactShadowResources>();
            m_PassData = new DiffuseShadowDenoisePassData();
            var cs = runtimeShaders.diffuseShadowDenoiserCS;
            m_PassData.ShadowDenoiserCS = cs;
            m_PassData.bilateralFilterHSingleDirectionalKernel = cs.FindKernel("BilateralFilterHSingleDirectional");
            m_PassData.bilateralFilterVSingleDirectionalKernel = cs.FindKernel("BilateralFilterVSingleDirectional");
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
            
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
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

            m_PassData.cameraFov = camera.fieldOfView * Mathf.PI / 180.0f;
            // Convert the angular diameter of the directional light to radians (from degrees)
            const float angularDiameter = 2.5f;
            m_PassData.lightAngle = angularDiameter * Mathf.PI / 180.0f;
            m_PassData.kernelSize = contactShadows.filterSizeTraced.value;

            int actualWidth = cameraData.cameraTargetDescriptor.width;
            int actualHeight = cameraData.cameraTargetDescriptor.height;
            // Evaluate the dispatch parameters
            int numTilesX = PostProcessingUtils.DivRoundUp(actualWidth, 8);
            int numTilesY = PostProcessingUtils.DivRoundUp(actualHeight, 8);
            m_PassData.numTilesX = numTilesX;
            m_PassData.numTilesY = numTilesY;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // TODO: Add distance based denoise support
                // Raise the distance based denoiser keyword
                // CoreUtils.SetKeyword(cmd, "DISTANCE_BASED_DENOISER", true);

                var computeShader = m_PassData.ShadowDenoiserCS;
                // Bind input uniforms for both dispatches
                cmd.SetComputeFloatParam(computeShader, RayTracingShaderProperties.RaytracingLightAngle, m_PassData.lightAngle);
                cmd.SetComputeIntParam(computeShader, RayTracingShaderProperties.DenoiserFilterRadius, m_PassData.kernelSize);
                cmd.SetComputeFloatParam(computeShader, RayTracingShaderProperties.CameraFOV, m_PassData.cameraFov);
                int kernel;

                kernel = m_PassData.bilateralFilterHSingleDirectionalKernel;
                // Bind Input Textures
                cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DepthTexture, _depthStencilBuffer);
                computeShader.SetTextureFromGlobal(kernel, RayTracingShaderProperties.NormalBufferTexture, RayTracingShaderProperties.CameraNormalsTexture);
                computeShader.SetTextureFromGlobal(kernel, RayTracingShaderProperties.DenoiseInputTexture, RayTracingShaderProperties.ContactShadowsRT);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderIds.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DenoiseOutputTextureRW, m_IntermediateBuffer);

                // Do the Horizontal pass
                cmd.DispatchCompute(computeShader, kernel, m_PassData.numTilesX, m_PassData.numTilesY, 1);

                kernel = m_PassData.bilateralFilterVSingleDirectionalKernel;
                // Bind Input Textures
                cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DepthTexture, _depthStencilBuffer);
                computeShader.SetTextureFromGlobal(kernel, RayTracingShaderProperties.NormalBufferTexture, RayTracingShaderProperties.CameraNormalsTexture);
                cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DenoiseInputTexture, m_IntermediateBuffer);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderIds.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DenoiseOutputTextureRW, m_ContactShadowsDenoisedRT);

                // Do the Vertical pass
                cmd.DispatchCompute(computeShader, kernel, m_PassData.numTilesX, m_PassData.numTilesY, 1);

                Shader.SetGlobalTexture(RayTracingShaderProperties.ContactShadowsRT, m_ContactShadowsDenoisedRT);
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
            
            using (var builder = renderGraph.AddComputePass<DiffuseShadowDenoisePassData>(profilingSampler.name, out var passData))
            {
                passData.depthTexture = resourceData.cameraDepthTexture;
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                
                // Convert the angular diameter of the directional light to radians (from degrees)
                const float angularDiameter = 2.5f;
                passData.lightAngle = angularDiameter * Mathf.PI / 180.0f;
                passData.cameraFov = cameraData.camera.fieldOfView * Mathf.PI / 180.0f;
                passData.kernelSize = contactShadows.filterSizeTraced.value;
                
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ContactShadowResources>();
                var cs = runtimeShaders.diffuseShadowDenoiserCS;
                passData.ShadowDenoiserCS = cs;
                passData.bilateralFilterHSingleDirectionalKernel = cs.FindKernel("BilateralFilterHSingleDirectional");
                passData.bilateralFilterVSingleDirectionalKernel = cs.FindKernel("BilateralFilterVSingleDirectional");
                
            
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
                builder.SetGlobalTextureAfterPass(contactShadowsDenoisedRT, RayTracingShaderProperties.ContactShadowsRT);
                
                int actualWidth = cameraData.cameraTargetDescriptor.width;
                int actualHeight = cameraData.cameraTargetDescriptor.height;
                // Evaluate the dispatch parameters
                int numTilesX = PostProcessingUtils.DivRoundUp(actualWidth, 8);
                int numTilesY = PostProcessingUtils.DivRoundUp(actualHeight, 8);
                passData.numTilesX = numTilesX;
                passData.numTilesY = numTilesY;
                
                builder.AllowPassCulling(false);
                builder.SetRenderFunc( static (DiffuseShadowDenoisePassData data, ComputeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(DiffuseShadowDenoisePassData data, ComputeGraphContext context)
        {
            int numTilesX = data.numTilesX;
            int numTilesY = data.numTilesY;
            
            var cmd = context.cmd;
            var computeShader = data.ShadowDenoiserCS;
            // Bind input uniforms for both dispatches
            cmd.SetComputeFloatParam(computeShader, RayTracingShaderProperties.RaytracingLightAngle, data.lightAngle);
            cmd.SetComputeIntParam(computeShader, RayTracingShaderProperties.DenoiserFilterRadius, data.kernelSize);
            cmd.SetComputeFloatParam(computeShader, RayTracingShaderProperties.CameraFOV, data.cameraFov);
            int kernel;
            
            kernel = data.bilateralFilterHSingleDirectionalKernel;
            // Bind Input Textures
            cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DepthTexture, data.depthTexture);
            computeShader.SetTextureFromGlobal(kernel, RayTracingShaderProperties.NormalBufferTexture, RayTracingShaderProperties.CameraNormalsTexture);
            computeShader.SetTextureFromGlobal(kernel, RayTracingShaderProperties.DenoiseInputTexture, RayTracingShaderProperties.ContactShadowsRT);
            
            // Bind output textures
            cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DenoiseOutputTextureRW, data.intermediateBuffer);
                 
            // Do the Horizontal pass
            cmd.DispatchCompute(computeShader, kernel, numTilesX, numTilesY, 1);
            
            kernel = data.bilateralFilterVSingleDirectionalKernel;
            // Bind Input Textures
            cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DepthTexture, data.depthTexture);
            computeShader.SetTextureFromGlobal(kernel, RayTracingShaderProperties.NormalBufferTexture, RayTracingShaderProperties.CameraNormalsTexture);
            cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DenoiseInputTexture, data.intermediateBuffer);
            
            // TODO: Add distance based denoise support
            // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderIds.DistanceTexture, _distanceBuffer);
            
            // Bind output textures
            cmd.SetComputeTextureParam(computeShader, kernel, RayTracingShaderProperties.DenoiseOutputTextureRW, data.contactShadowsDenoisedRT);
            
            // Do the Vertical pass
            cmd.DispatchCompute(computeShader, kernel, numTilesX, numTilesY, 1);
                 
            // Shader.SetGlobalTexture(RayTracingShaderProperties.ContactShadowsRT, data.contactShadowsDenoisedRT);

        }

        public void Dispose()
        {
            m_IntermediateBuffer?.Release();
            m_ContactShadowsDenoisedRT?.Release();
        }
    }
}