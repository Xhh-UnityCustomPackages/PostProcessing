using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class DiffuseShadowDenoisePass : ScriptableRenderPass, IDisposable
    {
        public static class RayTracingShaderProperties
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


        private readonly ComputeShader _shadowDenoiser;
        // Kernels that we are using
        private readonly int _bilateralFilterHSingleDirectionalKernel;
        private readonly int _bilateralFilterVSingleDirectionalKernel;
        
        private readonly ProfilingSampler _profilingSampler;
        
        private RTHandle _intermediateBuffer;
        private RTHandle _ContactShadowsDenoisedRT;
        
        // Camera parameters
        private int _texWidth;
        private int _texHeight;
        // Evaluation parameters
        private float _lightAngle;
        private float _cameraFov;
        private int _kernelSize;
        
        private RTHandle _depthStencilBuffer;
        // private RTHandle _normalBuffer;
        
        public DiffuseShadowDenoisePass()
        {
            _profilingSampler = new ProfilingSampler("Diffuse Shadow Denoise");
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ContactShadowResources>();
            _shadowDenoiser = runtimeShaders.diffuseShadowDenoiserCS;
            _bilateralFilterHSingleDirectionalKernel = _shadowDenoiser.FindKernel("BilateralFilterHSingleDirectional");
            _bilateralFilterVSingleDirectionalKernel = _shadowDenoiser.FindKernel("BilateralFilterVSingleDirectional");
        }
        
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            
            // Temporary buffers
            RenderingUtils.ReAllocateHandleIfNeeded(ref _intermediateBuffer, desc, name: "Intermediate buffer");
            // Output buffer
            RenderingUtils.ReAllocateHandleIfNeeded(ref _ContactShadowsDenoisedRT, desc, name: "Denoised Buffer");
            
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Prepare data
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;
            var renderer = cameraData.renderer;
            var contactShadows = VolumeManager.instance.stack.GetComponent<ContactShadows>();
            
            _depthStencilBuffer = UniversalRenderingUtility.GetDepthTexture(renderer);
            if (_depthStencilBuffer == null) return;
            
            // _normalBuffer = UniversalRenderingUtility.GetNormalTexture(renderer);
            // Debug.LogError(_normalBuffer);
            // if (_normalBuffer == null) return;
            
            _cameraFov = camera.fieldOfView * Mathf.PI / 180.0f;
            // Convert the angular diameter of the directional light to radians (from degrees)
            const float angularDiameter = 2.5f;
            _lightAngle = angularDiameter * Mathf.PI / 180.0f;
            _kernelSize = contactShadows.filterSizeTraced.value;
            
            int actualWidth = cameraData.cameraTargetDescriptor.width;
            int actualHeight = cameraData.cameraTargetDescriptor.height;
            _texWidth = actualWidth;
            _texHeight = actualHeight;
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                // TODO: Add distance based denoise support
                // Raise the distance based denoiser keyword
                // CoreUtils.SetKeyword(cmd, "DISTANCE_BASED_DENOISER", true);

                // Evaluate the dispatch parameters
                int numTilesX = GraphicsUtility.DivRoundUp(_texWidth, 8);
                int numTilesY = GraphicsUtility.DivRoundUp(_texHeight, 8);

                // Bind input uniforms for both dispatches
                cmd.SetComputeFloatParam(_shadowDenoiser, RayTracingShaderProperties.RaytracingLightAngle, _lightAngle);
                cmd.SetComputeIntParam(_shadowDenoiser, RayTracingShaderProperties.DenoiserFilterRadius, _kernelSize);
                cmd.SetComputeFloatParam(_shadowDenoiser, RayTracingShaderProperties.CameraFOV, _cameraFov);
                int kernel;

                kernel = _bilateralFilterHSingleDirectionalKernel;
                // Bind Input Textures
                cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.DepthTexture, _depthStencilBuffer);
                _shadowDenoiser.SetTextureFromGlobal(kernel, RayTracingShaderProperties.NormalBufferTexture, RayTracingShaderProperties.CameraNormalsTexture);
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.NormalBufferTexture, _normalBuffer);
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.DenoiseInputTexture, _rendererData.ContactShadowsRT);
                _shadowDenoiser.SetTextureFromGlobal(kernel, RayTracingShaderProperties.DenoiseInputTexture, RayTracingShaderProperties.ContactShadowsRT);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderIds.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.DenoiseOutputTextureRW, _intermediateBuffer);
                
                // Do the Horizontal pass
                cmd.DispatchCompute(_shadowDenoiser, kernel, numTilesX, numTilesY, 1);

                kernel = _bilateralFilterVSingleDirectionalKernel;
                // Bind Input Textures
                cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.DepthTexture, _depthStencilBuffer);
                _shadowDenoiser.SetTextureFromGlobal(kernel, RayTracingShaderProperties.NormalBufferTexture, RayTracingShaderProperties.CameraNormalsTexture);
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.NormalBufferTexture, _normalBuffer);
                cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.DenoiseInputTexture, _intermediateBuffer);

                // TODO: Add distance based denoise support
                // cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderIds.DistanceTexture, _distanceBuffer);

                // Bind output textures
                cmd.SetComputeTextureParam(_shadowDenoiser, kernel, RayTracingShaderProperties.DenoiseOutputTextureRW, _ContactShadowsDenoisedRT);

                // Do the Vertical pass
                cmd.DispatchCompute(_shadowDenoiser, kernel, numTilesX, numTilesY, 1);
                
                
                Shader.SetGlobalTexture(RayTracingShaderProperties.ContactShadowsRT, _ContactShadowsDenoisedRT);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _intermediateBuffer?.Release();
            _ContactShadowsDenoisedRT?.Release();
        }
    }
}