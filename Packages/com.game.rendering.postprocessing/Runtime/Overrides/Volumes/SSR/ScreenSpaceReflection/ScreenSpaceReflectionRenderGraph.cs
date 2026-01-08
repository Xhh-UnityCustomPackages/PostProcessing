using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceReflection>
    {
        private class TracingPassData
        {
            public Material Material;
            public TextureHandle HitPointTexture;
            public TextureHandle DepthStencilTexture;
            public TextureHandle NormalTexture;
            // public TextureHandle DepthPyramidTexture;
        }

        private class ReprojectionPassData
        {
            public Material Material;
            
            public TextureHandle HitPointTexture;
            public TextureHandle ColorPyramidTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle NormalTexture;
            public TextureHandle SsrLightingTexture;
            public TextureHandle SsrAccumTexture;
        }

        private class CombinePassData
        {
            public Material Material;
            public TextureHandle SsrLightingTextureRW;
            public TextureHandle sourceTexture;
            public TextureHandle targetTexture;
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
            m_Variables.RoughnessFadeEnd = roughnessFadeEnd;
            m_Variables.RoughnessFadeRcpLength = roughnessFadeRcpLength;
            m_Variables.RoughnessFadeEndTimesRcpLength = roughnessFadeEndTimesRcpLength;
            m_Variables.EdgeFadeRcpLength = edgeFadeRcpLength;//照搬的HDRP 但是这个实际效果过度太硬了
            m_Variables.ProjectionMatrix = SSR_ProjectToPixelMatrix;
        }

        private void SetupMaterials(Camera camera)
        {
            PrepareVariables(camera);
            
            var projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.ViewMatrix, camera.worldToCameraMatrix);
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.InverseViewMatrix, camera.worldToCameraMatrix.inverse);
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.InverseProjectionMatrix, projectionMatrix.inverse);
            m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params1,
                new Vector4(settings.vignette.value, settings.distanceFade.value, settings.maximumMarchDistance.value, 0));
            m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params2, new Vector4(0, 0, settings.thickness.value, settings.maximumIterationCount.value));
            
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrIntensity, m_Variables.Intensity);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeEnd, m_Variables.RoughnessFadeEnd);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeEndTimesRcpLength, m_Variables.RoughnessFadeEndTimesRcpLength);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrRoughnessFadeRcpLength, m_Variables.RoughnessFadeRcpLength);
            m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SsrEdgeFadeRcpLength, m_Variables.EdgeFadeRcpLength);
            
            Vector4 testTex_texelSize = new Vector4(
                1.0f / m_ScreenSpaceReflectionDescriptor.width,
                1.0f / m_ScreenSpaceReflectionDescriptor.height,
                m_ScreenSpaceReflectionDescriptor.width,
                m_ScreenSpaceReflectionDescriptor.height
            );
            m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants._SSR_TestTex_TexelSize, testTex_texelSize);
            
            // -------------------------------------------------------------------------------------------------
            // local shader keywords
            m_ShaderKeywords[0] = ShaderConstants.GetDebugKeyword(settings.debugMode.value);
            m_ScreenSpaceReflectionMaterial.shaderKeywords = m_ShaderKeywords;
            if (settings.debugMode.value == ScreenSpaceReflection.DebugMode.Split)
            {
                m_ScreenSpaceReflectionMaterial.SetFloat(ShaderConstants.SEPARATION_POS, settings.split.value);
            }
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
            else if (settings.resolution.value == ScreenSpaceReflection.Resolution.Quarter)
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
            
            SetupMaterials(cameraData.camera);
            
            GetSSRDesc(cameraData.cameraTargetDescriptor);

            var gBuffer = resourceData.gBuffer;
            TextureHandle cameraNormalsTexture = resourceData.cameraNormalsTexture;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;
            TextureHandle motionVectorTexture = resourceData.motionVectorColor;
            TextureHandle colorPyramidTexture = resourceData.cameraColor;
            
            var hitPointTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_ScreenSpaceReflectionDescriptor, "SSR_Hit_Point_Texture", false);
            var ssrLightingTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_ScreenSpaceReflectionDescriptor, "SSR_Lighting_Texture", false);

            // Execute tracing pass
            TextureHandle tracedHitPoint;
            tracedHitPoint = RenderTracingRasterPass(renderGraph, hitPointTexture, cameraDepthTexture, cameraNormalsTexture);
            
            // Execute reprojection pass
            TextureHandle reprojectedResult;
            reprojectedResult = RenderReprojectionRasterPass(renderGraph, tracedHitPoint, colorPyramidTexture, motionVectorTexture, cameraNormalsTexture, ssrLightingTexture);

            // Execute accumulation pass for PBR mode
            TextureHandle finalResult;
            if (true)
                finalResult = RenderAccumulationPass(renderGraph, colorPyramidTexture, motionVectorTexture, reprojectedResult, source, destination);
            else
                finalResult = reprojectedResult;
            
            // Set global texture for SSR result
            RenderGraphUtils.SetGlobalTexture(renderGraph, ShaderConstants.SSR_Lighting_Texture, finalResult);
        }

        private TextureHandle RenderTracingRasterPass(RenderGraph renderGraph, in TextureHandle hitPointTexture, TextureHandle depthStencilTexture, TextureHandle normalTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<TracingPassData>("SSR Tracing (Raster)", out var passData, m_TracingSampler))
            {
                passData.Material = m_ScreenSpaceReflectionMaterial;
                
                passData.DepthStencilTexture = depthStencilTexture;
                builder.UseTexture(depthStencilTexture, AccessFlags.Read);
                
                passData.NormalTexture = normalTexture;
                builder.UseTexture(normalTexture, AccessFlags.Read);
                passData.HitPointTexture = hitPointTexture;
                // builder.UseTexture(hitPointTexture, AccessFlags.Write);
                builder.SetRenderAttachment(hitPointTexture, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((TracingPassData data, RasterGraphContext ctx) =>
                {
                    var propertyBlock = new MaterialPropertyBlock();
                    propertyBlock.SetTexture(ShaderConstants._CameraDepthTexture, data.DepthStencilTexture);
                    propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                    
                    // Blitter.BlitCameraTexture(ctx.cmd, sourceTextureHdl,  passData.HitPointTexture, data.Material, (int)ShaderPasses.Test);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.Material, (int)ShaderPasses.Test, MeshTopology.Triangles, 3, 1, propertyBlock);
                });
                
                return passData.HitPointTexture;
            }
        }

        private TextureHandle RenderReprojectionRasterPass(RenderGraph renderGraph, TextureHandle hitPointTexture,
            TextureHandle colorPyramidTexture, TextureHandle motionVectorTexture, TextureHandle normalTexture,
            TextureHandle ssrLightingTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ReprojectionPassData>("SSR Reprojection (Raster)", out var passData, m_ReprojectionSampler))
            {
                passData.Material = m_ScreenSpaceReflectionMaterial;
                
                passData.NormalTexture = normalTexture;
                builder.UseTexture(normalTexture, AccessFlags.Read);
                
                // passData.MotionVectorTexture = motionVectorTexture;
                // builder.UseTexture(motionVectorTexture, AccessFlags.Read);
                
                passData.HitPointTexture = hitPointTexture;
                builder.UseTexture(hitPointTexture, AccessFlags.Read);

                passData.ColorPyramidTexture = colorPyramidTexture;
                builder.UseTexture(colorPyramidTexture, AccessFlags.Read);

                passData.SsrLightingTexture = ssrLightingTexture;
                
                builder.SetRenderAttachment(ssrLightingTexture, 0, AccessFlags.WriteAll);
                
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((ReprojectionPassData data, RasterGraphContext ctx) =>
                {
                    var propertyBlock = new MaterialPropertyBlock();
                    propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                    propertyBlock.SetTexture(ShaderConstants.SsrHitPointTexture, data.HitPointTexture);
                    propertyBlock.SetTexture(ShaderConstants._BlitTexture, data.ColorPyramidTexture);
                    propertyBlock.SetTexture(ShaderConstants._GBuffer2, data.NormalTexture);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.Material, (int)ShaderPasses.Reproject, MeshTopology.Triangles, 3, 1, propertyBlock);
                });
                
                return passData.SsrLightingTexture;
            }
        }


        private TextureHandle RenderAccumulationPass(RenderGraph renderGraph, 
            TextureHandle colorPyramidTexture, TextureHandle motionVectorTexture,
            TextureHandle ssrLightingTextureRW, 
            TextureHandle source, TextureHandle destination)
        {
            using (var builder = renderGraph.AddUnsafePass<CombinePassData>("SSR Accumulation", out var passData, m_AccumulationSampler))
            {
                passData.Material = m_ScreenSpaceReflectionMaterial;
                
                passData.SsrLightingTextureRW = ssrLightingTextureRW;
                builder.UseTexture(ssrLightingTextureRW, AccessFlags.Read);
                
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                passData.targetTexture = destination;
                builder.UseTexture(destination, AccessFlags.ReadWrite);
                
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((CombinePassData data, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    data.Material.SetTexture(ShaderConstants.SsrLightingTexture, data.SsrLightingTextureRW);
                    Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.targetTexture, data.Material, (int)ShaderPasses.Composite);
                });
                return passData.SsrLightingTextureRW;
            }
        }

    }
}