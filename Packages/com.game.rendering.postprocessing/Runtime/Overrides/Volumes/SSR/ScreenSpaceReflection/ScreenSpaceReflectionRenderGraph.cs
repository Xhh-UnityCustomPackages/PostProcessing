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
            public Material Material;
            // Input
            public TextureHandle DepthTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle NormalTexture;
            public TextureHandle GBuffer2;
            // --
            public TextureHandle HitPointTexture;
            public TextureHandle SsrLightingTexture;

            public TextureHandle CameraColorTexture;
        }
        
        private struct ScreenSpaceReflectionVariables
        {
            public Matrix4x4 ProjectionMatrix;
            
            public float Intensity;
            public float Thickness;
            public float ThicknessScale;
            public float ThicknessBias;
            
            public float Steps;
            public float StepSize;
            public float RoughnessFadeEnd;
            public float RoughnessFadeRcpLength;
            
            public float RoughnessFadeEndTimesRcpLength;
            public float EdgeFadeRcpLength;
            public int DepthPyramidMaxMip;
            public float DownsamplingDivider;
            
            public float AccumulationAmount;
            public float PBRSpeedRejection;
            public float PBRSpeedRejectionScalerFactor;
            public float PBRBias;
            
            public int ColorPyramidMaxMip;
        }

        private ScreenSpaceReflectionVariables m_Variables;
        
        private void PrepareVariables(Camera camera)
        {
            var thickness = settings.thickness.value;
            var minSmoothness = settings.minSmoothness.value;
            var smoothnessFadeStart = settings.smoothnessFadeStart.value;
            var screenFadeDistance = settings.vignette.value;
            float n = camera.nearClipPlane;
            float f = camera.farClipPlane;
            
            float roughnessFadeStart = 1 - smoothnessFadeStart;
            float roughnessFadeEnd = 1 - minSmoothness;
            float roughnessFadeLength = roughnessFadeEnd - roughnessFadeStart;
            float roughnessFadeEndTimesRcpLength = (roughnessFadeLength != 0) ? roughnessFadeEnd * (1.0f / roughnessFadeLength) : 1;
            float roughnessFadeRcpLength = (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0;
            float edgeFadeRcpLength = Mathf.Min(1.0f / screenFadeDistance, float.MaxValue);
            
            var SSR_ProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            var HalfCameraSize = new Vector2(
                (int)(camera.pixelWidth * 0.5f),
                (int)(camera.pixelHeight * 0.5f));
            Matrix4x4 warpToScreenSpaceMatrix = Matrix4x4.identity;
            warpToScreenSpaceMatrix.m00 = HalfCameraSize.x;
            warpToScreenSpaceMatrix.m03 = HalfCameraSize.x;
            warpToScreenSpaceMatrix.m11 = HalfCameraSize.y;
            warpToScreenSpaceMatrix.m13 = HalfCameraSize.y;
            Matrix4x4 SSR_ProjectToPixelMatrix = warpToScreenSpaceMatrix * SSR_ProjectionMatrix;
            
            m_Variables.Intensity = settings.intensity.value;
            m_Variables.Thickness = thickness;
            m_Variables.ThicknessScale = 1.0f / (1.0f + thickness);;
            m_Variables.ThicknessBias = -n / (f - n) * (thickness * m_Variables.ThicknessScale);
            // m_Variables.Steps = settings.steps.value;
            m_Variables.StepSize = settings.stepSize.value;
            m_Variables.RoughnessFadeEnd = roughnessFadeEnd;
            m_Variables.RoughnessFadeRcpLength = roughnessFadeRcpLength;
            m_Variables.RoughnessFadeEndTimesRcpLength = roughnessFadeEndTimesRcpLength;
            m_Variables.EdgeFadeRcpLength = edgeFadeRcpLength;//照搬的HDRP 但是这个实际效果过度太硬了
            m_Variables.DepthPyramidMaxMip = context.DepthMipChainInfo.mipLevelCount - 1;
            m_Variables.ColorPyramidMaxMip = context.ColorPyramidHistoryMipCount - 1;
            m_Variables.DownsamplingDivider = GetScaleFactor();
            m_Variables.ProjectionMatrix = SSR_ProjectToPixelMatrix;

            // PBR properties only be used in compute shader mode
            m_Variables.PBRBias = settings.biasFactor.value;
            m_Variables.PBRSpeedRejection = Mathf.Clamp01(settings.speedRejectionParam.value);
            m_Variables.PBRSpeedRejectionScalerFactor = Mathf.Pow(settings.speedRejectionScalerFactor.value * 0.1f, 2.0f);
            if (context.FrameCount <= 3)
            {
                m_Variables.AccumulationAmount = 1.0f;
            }
            else
            {
                m_Variables.AccumulationAmount = Mathf.Pow(2, Mathf.Lerp(0.0f, -7.0f, settings.accumulationFactor.value));
            }
        }

        private void SetupMaterials(Camera camera)
        {
            PrepareVariables(camera);
            m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params1,
                new Vector4(settings.vignette.value, 0, settings.maximumMarchDistance.value, settings.maximumIterationCount.value));
            // m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.SSR_ProjectionMatrix, m_Variables.ProjectionMatrix);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrIntensity, m_Variables.Intensity);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.Thickness, m_Variables.Thickness);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrThicknessScale, m_Variables.ThicknessScale);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrThicknessBias, m_Variables.ThicknessBias);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.StepSize, m_Variables.StepSize);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeEnd, m_Variables.RoughnessFadeEnd);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeEndTimesRcpLength, m_Variables.RoughnessFadeEndTimesRcpLength);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeRcpLength, m_Variables.RoughnessFadeRcpLength);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrEdgeFadeRcpLength, m_Variables.EdgeFadeRcpLength);
            m_ScreenSpaceReflectionMaterial.SetInteger(ShaderConstants.SsrDepthPyramidMaxMip, m_Variables.DepthPyramidMaxMip);
            m_ScreenSpaceReflectionMaterial.SetInteger(ShaderConstants.SsrColorPyramidMaxMip, m_Variables.ColorPyramidMaxMip);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrDownsamplingDivider, m_Variables.DownsamplingDivider);
            
            // -------------------------------------------------------------------------------------------------
            // local shader keywords
            m_ShaderKeywords[0] = ShaderConstants.GetDebugKeyword(settings.debugMode.value);
            m_ShaderKeywords[1] = ShaderConstants.GetApproxKeyword(settings.usedAlgorithm.value);
            m_ShaderKeywords[2] = ShaderConstants.GetMultiBounceKeyword(settings.enableMipmap.value);
            m_ScreenSpaceReflectionMaterial.shaderKeywords = m_ShaderKeywords;
            if (settings.debugMode.value == ScreenSpaceReflection.DebugMode.Split)
            {
                m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SEPARATION_POS, settings.split.value);
            }
        }
        
        private float GetScaleFactor()
        {
            float scaleFactor = 1.0f;
            if (settings.resolution.value == ScreenSpaceReflection.Resolution.Half)
            {
                scaleFactor = 0.5f;
               
            }

            return scaleFactor;
        }

        void GetSSRDesc(RenderTextureDescriptor desc)
        {
            float width = desc.width;
            float height = desc.height;

            if (false)
            {
                int size = Mathf.ClosestPowerOfTwo(Mathf.Min(m_SSRTestDescriptor.width, m_SSRTestDescriptor.height));
                width = height = size;
            }
            
            float scaleFactor = GetScaleFactor(); 
            width *= scaleFactor;
            height *= scaleFactor;

            m_SSRTestDescriptor = desc;
            m_SSRTestDescriptor.width = Mathf.CeilToInt(width);
            m_SSRTestDescriptor.height = Mathf.CeilToInt(height);
            GetCompatibleDescriptor(ref m_SSRTestDescriptor, GraphicsFormat.R16G16B16A16_SFloat);

            m_SSRColorDescriptor = m_SSRTestDescriptor;
            m_SSRColorDescriptor.width = desc.width;
            m_SSRColorDescriptor.height = desc.height;
            m_SSRColorDescriptor.colorFormat = desc.colorFormat;
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            SetupMaterials(cameraData.camera);
            
            GetSSRDesc(cameraData.cameraTargetDescriptor);

            var gBuffer = resourceData.gBuffer;
            TextureHandle cameraNormalsTexture = resourceData.cameraNormalsTexture;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;
            TextureHandle motionVectorTexture = resourceData.motionVectorColor;
            TextureHandle colorPyramidTexture = resourceData.cameraColor;

            var hitPointTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_SSRTestDescriptor, "SSR_Hit_Point_Texture", false);
            var ssrLightingTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_SSRTestDescriptor, "SSR_Lighting_Texture", false);

            using (var builder = renderGraph.AddUnsafePass<ScreenSpaceReflectionPassData>(profilingSampler.name, out var passData))
            {
                passData.Material = m_ScreenSpaceReflectionMaterial;

                passData.DepthTexture = cameraDepthTexture;
                builder.UseTexture(cameraDepthTexture, AccessFlags.Read);

                passData.NormalTexture = cameraNormalsTexture;
                builder.UseTexture(cameraNormalsTexture, AccessFlags.Read);

                passData.MotionVectorTexture = motionVectorTexture;
                builder.UseTexture(cameraNormalsTexture, AccessFlags.Read);

                passData.GBuffer2 = gBuffer[2];
                builder.UseTexture(gBuffer[2], AccessFlags.Read);
                
                passData.HitPointTexture = hitPointTexture;
                builder.UseTexture(hitPointTexture, AccessFlags.ReadWrite);
                
                passData.SsrLightingTexture = ssrLightingTexture;
                builder.UseTexture(ssrLightingTexture, AccessFlags.Write);

                passData.CameraColorTexture = colorPyramidTexture;
                builder.UseTexture(colorPyramidTexture, AccessFlags.ReadWrite);
                
                // builder.UseTexture(destination, AccessFlags.Write);
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((ScreenSpaceReflectionPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    // var cmd = context.cmd;
                    using (new ProfilingScope(cmd, m_TracingSampler))
                    {
                        var propertyBlock = new MaterialPropertyBlock();
                        propertyBlock.SetTexture(ShaderConstants._CameraDepthTexture, data.DepthTexture);
                        propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                        cmd.SetRenderTarget(data.HitPointTexture);
                        cmd.DrawProcedural(Matrix4x4.identity, data.Material, (int)ShaderPasses.Test, MeshTopology.Triangles, 3, 1, propertyBlock);
                    }

                    using (new ProfilingScope(cmd, m_ReprojectionSampler))
                    {
                        var propertyBlock = new MaterialPropertyBlock();
                        propertyBlock.SetTexture(ShaderConstants._BlitTexture, colorPyramidTexture);
                        propertyBlock.SetTexture(ShaderConstants.SsrHitPointTexture, data.HitPointTexture);
                        propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                        propertyBlock.SetTexture(ShaderConstants._GBuffer2, data.GBuffer2);
                        
                        cmd.SetRenderTarget(ssrLightingTexture);
                        cmd.DrawProcedural(Matrix4x4.identity, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Reproject, MeshTopology.Triangles, 3, 1, propertyBlock);
                    }

                    // Apply SSR
                    {
                        // m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.SsrLightingTexture, ssrLightingTexture);
                        // cmd.SetRenderTarget(destination);
                        // Blitter.BlitCameraTexture(cmd, source, destination, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Composite);
                    }
                });
            }
            
            // Set global texture for SSR result
            // RenderGraphUtils.SetGlobalTexture(renderGraph, ShaderConstants.SSR_Lighting_Texture, finalResult);
        }

    }
}