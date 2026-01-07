using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceReflection>
    {
        private class ScreenSpaceReflectionPassData
        {
            // Setup
            public Material material;
            // Inputs
            internal TextureHandle sourceTexture;
            // Pass textures
            internal TextureHandle testTexture;
            internal TextureHandle resolveTexture;
            internal TextureHandle resolveBlurTexture;
            internal TextureHandle gBuffer0;
            internal TextureHandle gBuffer1;
            internal TextureHandle gBuffer2;
            internal TextureHandle motionVectorColorTexture;
            // Output texture
            internal TextureHandle targetTexture;
            
        
            internal ProfilingSampler profilingSampler_Reproject;
            internal ProfilingSampler profilingSampler_Blur;
            internal ProfilingSampler profilingSampler_Compose;
        }

        
        private struct ScreenSpaceReflectionVariables
        {
            public Matrix4x4 ProjectionMatrix;
            
            public float Intensity;
            public float Thickness;
            public float ThicknessScale;
            public float ThicknessBias;
            
            // public float Steps;
            // public float StepSize;
            public float RoughnessFadeEnd;
            public float RoughnessFadeRcpLength;
            
            public float RoughnessFadeEndTimesRcpLength;
            public float EdgeFadeRcpLength;
            // public int DepthPyramidMaxMip;
            // public float DownsamplingDivider;
            //
            // public float AccumulationAmount;
            // public float PBRSpeedRejection;
            // public float PBRSpeedRejectionScalerFactor;
            // public float PBRBias;
            //
            // public int ColorPyramidMaxMip;
        }

        private ScreenSpaceReflectionVariables m_Variables;
        
        private void PrepareVariables(Camera camera)
        {
            var minSmoothness = settings.minSmoothness.value;
            var smoothnessFadeStart = settings.smoothnessFadeStart.value;
            var screenFadeDistance = settings.vignette.value;
            
            float roughnessFadeStart = 1 - smoothnessFadeStart;
            float roughnessFadeEnd = 1 - minSmoothness;
            float roughnessFadeLength = roughnessFadeEnd - roughnessFadeStart;
            float roughnessFadeEndTimesRcpLength = (roughnessFadeLength != 0) ? roughnessFadeEnd * (1.0f / roughnessFadeLength) : 1;
            float roughnessFadeRcpLength = (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0;
            float edgeFadeRcpLength = Mathf.Min(1.0f / screenFadeDistance, float.MaxValue);
            
            var projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            
            m_Variables.Intensity = settings.intensity.value;
            m_Variables.RoughnessFadeEnd = roughnessFadeEnd;
            m_Variables.RoughnessFadeRcpLength = roughnessFadeRcpLength;
            m_Variables.RoughnessFadeEndTimesRcpLength = roughnessFadeEndTimesRcpLength;
            m_Variables.EdgeFadeRcpLength = edgeFadeRcpLength;
            
        }

        private void SetupMaterials(Camera camera, int width, int height)
        {
            if (m_ScreenSpaceReflectionMaterial == null)
            {
                var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceReflectionResources>();
                m_ScreenSpaceReflectionMaterial = GetMaterial(runtimeResources.screenSpaceReflectionPS);
            }

            PrepareVariables(camera);
      
            var size = m_ScreenSpaceReflectionDescriptor.width;
            
            var projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
           

            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.ViewMatrix, camera.worldToCameraMatrix);
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.InverseViewMatrix, camera.worldToCameraMatrix.inverse);
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.InverseProjectionMatrix, projectionMatrix.inverse);
            m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params1,
                new Vector4(settings.vignette.value, settings.distanceFade.value, settings.maximumMarchDistance.value,  m_Variables.Intensity));

            if (settings.jitterMode.value == ScreenSpaceReflection.JitterMode.BlueNoise)
            {
                var noiseTex = postProcessFeatureData.textures.blueNoiseTex;
                m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.NoiseTex, noiseTex);
                m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params2,
                    new Vector4((float)width / height, size / (float)noiseTex.width, settings.thickness.value, settings.maximumIterationCount.value));
            }
            else
            {
                m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params2,
                    new Vector4(0, 0, settings.thickness.value, settings.maximumIterationCount.value));
            }
            
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrIntensity, m_Variables.Intensity);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeEnd, m_Variables.RoughnessFadeEnd);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeEndTimesRcpLength, m_Variables.RoughnessFadeEndTimesRcpLength);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeRcpLength, m_Variables.RoughnessFadeRcpLength);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrEdgeFadeRcpLength, m_Variables.EdgeFadeRcpLength);
            
            // -------------------------------------------------------------------------------------------------
            // local shader keywords
            m_ShaderKeywords[0] = ShaderConstants.GetDebugKeyword(settings.debugMode.value);
            m_ShaderKeywords[1] = ShaderConstants.GetJitterKeyword(settings.jitterMode.value);
            m_ScreenSpaceReflectionMaterial.shaderKeywords = m_ShaderKeywords;
            
        }

        void GetSSRDesc(RenderTextureDescriptor desc)
        {
            int width = desc.width;
            int height = desc.height;

            if(false)
            {
                int size = Mathf.ClosestPowerOfTwo(Mathf.Min(m_ScreenSpaceReflectionDescriptor.width, m_ScreenSpaceReflectionDescriptor.height));
                width = height = size;
            }

            if (settings.resolution.value == ScreenSpaceReflection.Resolution.Half)
            {
                width >>= 1;
                height >>= 1;
            }
            else if (settings.resolution.value == ScreenSpaceReflection.Resolution.Quater)
            {
                width >>= 2;
                height >>= 2;
            }
            else if (settings.resolution.value == ScreenSpaceReflection.Resolution.Double)
            {
                width <<= 1;
                height <<= 1;
            }

            m_ScreenSpaceReflectionDescriptor = desc;
            m_ScreenSpaceReflectionDescriptor.width = width;
            m_ScreenSpaceReflectionDescriptor.height = height;
            GetCompatibleDescriptor(ref m_ScreenSpaceReflectionDescriptor, GraphicsFormat.R16G16B16A16_SFloat);
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            SetupMaterials(cameraData.camera, cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
            
            GetSSRDesc(cameraData.cameraTargetDescriptor);

            var gBuffer = resourceData.gBuffer;
            TextureHandle cameraNormalsTexture = resourceData.cameraNormalsTexture;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;
            TextureHandle motionVectorColorTexture = resourceData.motionVectorColor;
            
            var testRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_ScreenSpaceReflectionDescriptor, "_SSR_TestTex", false);
            var resolveTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_ScreenSpaceReflectionDescriptor, "_SSR_ResolveTex", false);
            var resolveBlurTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_ScreenSpaceReflectionDescriptor, "_SSR_ResolveBlurTex", false);

            using (var builder = renderGraph.AddUnsafePass<ScreenSpaceReflectionPassData>(profilingSampler.name, out var passData))
            {
                passData.material = m_ScreenSpaceReflectionMaterial;
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);

                passData.targetTexture = destination;
                builder.UseTexture(destination, AccessFlags.Write);

                passData.testTexture = testRT;
                builder.UseTexture(testRT, AccessFlags.ReadWrite);

                Vector4 testTex_texelSize = new Vector4(
                    1.0f / m_ScreenSpaceReflectionDescriptor.width,
                    1.0f / m_ScreenSpaceReflectionDescriptor.height,
                    m_ScreenSpaceReflectionDescriptor.width,
                    m_ScreenSpaceReflectionDescriptor.height
                );
                passData.material.SetVector(ShaderConstants._SSR_TestTex_TexelSize, testTex_texelSize);

                passData.resolveTexture = resolveTex;
                builder.UseTexture(resolveTex, AccessFlags.ReadWrite);

                passData.resolveBlurTexture = resolveBlurTex;
                builder.UseTexture(resolveBlurTex, AccessFlags.Write);

                //global
                builder.UseTexture(motionVectorColorTexture, AccessFlags.Read);
                builder.UseTexture(cameraNormalsTexture, AccessFlags.Read);
                builder.UseTexture(cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(gBuffer[0], AccessFlags.Read);
                builder.UseTexture(gBuffer[1], AccessFlags.Read);
                builder.UseTexture(gBuffer[2], AccessFlags.Read);
                passData.gBuffer0 = gBuffer[0];
                passData.gBuffer1 = gBuffer[1];
                passData.gBuffer2 = gBuffer[2];

                passData.profilingSampler_Reproject = m_ProfilingSampler_Reproject;
                passData.profilingSampler_Blur = m_ProfilingSampler_Blur;
                passData.profilingSampler_Compose = m_ProfilingSampler_Compose;

                // if (cameraData.historyManager != null)
                // {
                //     cameraData.historyManager.
                // }

                builder.SetRenderFunc(static (ScreenSpaceReflectionPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    RTHandle sourceTextureHdl = data.sourceTexture;
                    RTHandle targetTextureHdl = data.targetTexture;
                    RTHandle testTextureHdl = data.testTexture;
                    Blitter.BlitCameraTexture(cmd, sourceTextureHdl, testTextureHdl, data.material, (int)ShaderPasses.Test);
                    
                    RTHandle resolveTextureHdl = data.resolveTexture;
                    data.material.SetTexture(ShaderConstants.TestTex, testTextureHdl);
                    Blitter.BlitCameraTexture(cmd, sourceTextureHdl, resolveTextureHdl, data.material, (int)ShaderPasses.Resolve);

                    using (new ProfilingScope(cmd, data.profilingSampler_Reproject))
                    {
                        ExecuteReprojection(cmd, data);
                    }
                    
                    using (new ProfilingScope(cmd, data.profilingSampler_Blur))
                    {
                        ExecuteBlur(cmd, data);
                    }

                    using (new ProfilingScope(cmd, data.profilingSampler_Compose))
                    {
                        var finalRT = data.resolveTexture;
                        data.material.SetTexture(ShaderConstants.ResolveTex, finalRT);
                        data.material.SetTexture("_GBuffer0", data.gBuffer0);
                        data.material.SetTexture("_GBuffer1", data.gBuffer1);
                        data.material.SetTexture("_GBuffer2", data.gBuffer2);
                        Blitter.BlitCameraTexture(cmd, sourceTextureHdl, targetTextureHdl, data.material, (int)ShaderPasses.Composite);
                    }
                });
            }
        }

        static void ExecuteReprojection(CommandBuffer cmd, ScreenSpaceReflectionPassData data)
        {
        }

        static void ExecuteBlur(CommandBuffer cmd, ScreenSpaceReflectionPassData data)
        {
        }
    }
}