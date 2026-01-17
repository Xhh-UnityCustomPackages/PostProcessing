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
            public ComputeShader ComputeShader;
            public bool useComputeShader;
            // Input
            public TextureHandle DepthTexture;
            public TextureHandle MotionVectorTexture;
            // public TextureHandle NormalTexture;
            public TextureHandle GBuffer2;
            public TextureHandle DepthPyramidTexture;
            // --
            public TextureHandle HitPointTexture;
            public TextureHandle SsrLightingTexture;

            public TextureHandle CameraColorTexture;
        }
        
        private struct ScreenSpaceReflectionVariables
        {
            // public Matrix4x4 ProjectionMatrix;
            
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
            
            m_Variables.Intensity = settings.intensity.value;
            m_Variables.Thickness = thickness;
            m_Variables.ThicknessScale = 1.0f / (1.0f + thickness);;
            m_Variables.ThicknessBias = -n / (f - n) * (thickness * m_Variables.ThicknessScale);
            m_Variables.Steps = settings.maximumIterationCount.value;
            m_Variables.StepSize = settings.stepSize.value;
            m_Variables.RoughnessFadeEnd = roughnessFadeEnd;
            m_Variables.RoughnessFadeRcpLength = roughnessFadeRcpLength;
            m_Variables.RoughnessFadeEndTimesRcpLength = roughnessFadeEndTimesRcpLength;
            m_Variables.EdgeFadeRcpLength = edgeFadeRcpLength;//照搬的HDRP 但是这个实际效果过度太硬了
            m_Variables.DepthPyramidMaxMip = postProcessData.DepthMipChainInfo.mipLevelCount - 1;
            m_Variables.ColorPyramidMaxMip = postProcessData.ColorPyramidHistoryMipCount - 1;
            m_Variables.DownsamplingDivider = GetScaleFactor();

            // PBR properties only be used in compute shader mode
            m_Variables.PBRBias = settings.biasFactor.value;
            m_Variables.PBRSpeedRejection = Mathf.Clamp01(settings.speedRejectionParam.value);
            m_Variables.PBRSpeedRejectionScalerFactor = Mathf.Pow(settings.speedRejectionScalerFactor.value * 0.1f, 2.0f);
            if (postProcessData.FrameCount <= 3)
            {
                m_Variables.AccumulationAmount = 1.0f;
            }
            else
            {
                m_Variables.AccumulationAmount = Mathf.Pow(2, Mathf.Lerp(0.0f, -7.0f, settings.accumulationFactor.value));
            }
        }

        private void SetupMaterials(MaterialPropertyBlock propertyBlock, Camera camera)
        {
            PrepareVariables(camera);
            propertyBlock.SetVector(ShaderConstants.Params1,
                new Vector4(settings.vignette.value, 0, settings.maximumMarchDistance.value, 0));
            propertyBlock.SetFloat(ShaderConstants.SsrIntensity, m_Variables.Intensity);
            propertyBlock.SetFloat(ShaderConstants.Thickness, m_Variables.Thickness);
            propertyBlock.SetFloat(ShaderConstants.SsrThicknessScale, m_Variables.ThicknessScale);
            propertyBlock.SetFloat(ShaderConstants.SsrThicknessBias, m_Variables.ThicknessBias);
            propertyBlock.SetFloat(ShaderConstants.Steps, m_Variables.Steps);
            propertyBlock.SetFloat(ShaderConstants.StepSize, m_Variables.StepSize);
            propertyBlock.SetFloat(ShaderConstants.SsrRoughnessFadeEnd, m_Variables.RoughnessFadeEnd);
            propertyBlock.SetFloat(ShaderConstants.SsrRoughnessFadeEndTimesRcpLength, m_Variables.RoughnessFadeEndTimesRcpLength);
            propertyBlock.SetFloat(ShaderConstants.SsrRoughnessFadeRcpLength, m_Variables.RoughnessFadeRcpLength);
            propertyBlock.SetFloat(ShaderConstants.SsrEdgeFadeRcpLength, m_Variables.EdgeFadeRcpLength);
            propertyBlock.SetInteger(ShaderConstants.SsrDepthPyramidMaxMip, m_Variables.DepthPyramidMaxMip);
            propertyBlock.SetInteger(ShaderConstants.SsrColorPyramidMaxMip, m_Variables.ColorPyramidMaxMip);
            propertyBlock.SetFloat(ShaderConstants.SsrDownsamplingDivider, m_Variables.DownsamplingDivider);
            propertyBlock.SetFloat(ShaderConstants.SsrPBRBias, m_Variables.PBRBias);
            
            // -------------------------------------------------------------------------------------------------
            // local shader keywords
            m_ShaderKeywords[0] = ShaderConstants.GetDebugKeyword(settings.debugMode.value);
            m_ShaderKeywords[1] = ShaderConstants.GetApproxKeyword(settings.usedAlgorithm.value);
            m_ShaderKeywords[2] = ShaderConstants.GetUseMipmapKeyword(settings.enableMipmap.value);
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
                scaleFactor = 0.5f;

            return scaleFactor;
        }

        void GetSSRDesc(RenderTextureDescriptor desc)
        {
            float width = desc.width;
            float height = desc.height;
            
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
         
            m_UseCS = settings.useComputeShader.value;
            GetSSRDesc(cameraData.cameraTargetDescriptor);
            if (m_UseCS)
            {
                m_SSRTestDescriptor.enableRandomWrite = true;
                m_SSRColorDescriptor.enableRandomWrite = true;
            }

            var gBuffer = resourceData.gBuffer;
            TextureHandle cameraNormalsTexture = resourceData.cameraNormalsTexture;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;
            TextureHandle motionVectorTexture = resourceData.motionVectorColor;
            TextureHandle colorPyramidTexture = resourceData.cameraColor;
            
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrHitPointRT, m_SSRTestDescriptor, FilterMode.Point, name: "SSR_Hit_Point_Texture");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrLightingRT, m_SSRColorDescriptor, FilterMode.Bilinear, name: "SSR_Lighting_Texture");
            var hitPointTexture = renderGraph.ImportTexture(m_SsrHitPointRT);
            var ssrLightingTexture = renderGraph.ImportTexture(m_SsrLightingRT);
            
            var colorBufferMipChainTexture = TextureHandle.nullHandle;
            if (settings.enableMipmap.value)
            {
                var colorBufferMipChain = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);
                if (colorBufferMipChain != null)
                    colorBufferMipChainTexture = renderGraph.ImportTexture(colorBufferMipChain);

                if (colorBufferMipChainTexture.IsValid())
                {
                    colorPyramidTexture = colorBufferMipChainTexture;
                }
            }
            else
            {
                var previousColorTexture = renderGraph.ImportTexture(postProcessData.CameraPreviousColorTextureRT);
                if (colorBufferMipChainTexture.IsValid())
                {
                    colorPyramidTexture = previousColorTexture;
                }
            }
            

            using (var builder = renderGraph.AddUnsafePass<ScreenSpaceReflectionPassData>(profilingSampler.name, out var passData))
            {
                passData.Material = m_ScreenSpaceReflectionMaterial;
                passData.ComputeShader = m_ComputeShader;
                passData.useComputeShader = m_UseCS;

                passData.DepthTexture = cameraDepthTexture;
                builder.UseTexture(cameraDepthTexture, AccessFlags.Read);

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

                if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.HiZTracing)
                {
                    var depthPyramidTexture = renderGraph.ImportTexture(postProcessData.DepthPyramidRT);
                    passData.DepthPyramidTexture = depthPyramidTexture;
                }

                builder.UseTexture(source, AccessFlags.Read);
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((ScreenSpaceReflectionPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    postProcessData.BindDitheredRNGData1SPP(cmd);
                    // var cmd = context.cmd;
                    using (new ProfilingScope(cmd, m_TracingSampler))
                    {
                        if (passData.useComputeShader)
                        {
                            ConstantBuffer.Push(cmd, m_Variables, passData.ComputeShader, ShaderConstants.ShaderVariablesScreenSpaceReflection);
                            var offsetBuffer = postProcessData.DepthMipChainInfo.GetOffsetBufferData(postProcessData.DepthPyramidMipLevelOffsetsBuffer);
                    
                            //只支持HiZ模式
                            cmd.SetComputeBufferParam(passData.ComputeShader, m_TracingKernel, ShaderConstants._DepthPyramidMipLevelOffsets, offsetBuffer);
                            cmd.SetComputeTextureParam(passData.ComputeShader, m_TracingKernel, PipelineShaderIDs._DepthPyramid, data.DepthPyramidTexture);
                            // cmd.SetComputeTextureParam(m_ComputeShader, m_TracingKernel, ShaderConstants._CameraDepthTexture, cameraDepthTexture, 0, RenderTextureSubElement.Stencil);
                            cmd.SetComputeTextureParam(passData.ComputeShader, m_TracingKernel, ShaderConstants._GBuffer2, passData.GBuffer2);
                            cmd.SetComputeTextureParam(passData.ComputeShader, m_TracingKernel, ShaderConstants.SsrHitPointTexture, passData.HitPointTexture);
                    
                            int groupsX = PostProcessingUtils.DivRoundUp(m_SSRTestDescriptor.width, 8);
                            int groupsY = PostProcessingUtils.DivRoundUp(m_SSRTestDescriptor.height, 8);
                            cmd.DispatchCompute(passData.ComputeShader, m_TracingKernel, groupsX, groupsY, 1);
                        }
                        else
                        {
                            var propertyBlock = new MaterialPropertyBlock();
                            propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                            SetupMaterials(propertyBlock, cameraData.camera);
                            cmd.SetRenderTarget(data.HitPointTexture);
                            if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.LinearTracing)
                            {
                                propertyBlock.SetTexture(ShaderConstants._CameraDepthTexture, data.DepthTexture);
                                cmd.DrawProcedural(Matrix4x4.identity, data.Material, (int)ShaderPasses.Test, MeshTopology.Triangles, 3, 1, propertyBlock);
                            }
                            else
                            {
                                propertyBlock.SetTexture(PipelineShaderIDs._DepthPyramid, data.DepthPyramidTexture);
                                propertyBlock.SetTexture(ShaderConstants._GBuffer2, data.GBuffer2);
                                var offsetBuffer = postProcessData.DepthMipChainInfo.GetOffsetBufferData(postProcessData.DepthPyramidMipLevelOffsetsBuffer);
                                propertyBlock.SetBuffer(ShaderConstants._DepthPyramidMipLevelOffsets, offsetBuffer);
                                cmd.DrawProcedural(Matrix4x4.identity, data.Material, (int)ShaderPasses.HizTest, MeshTopology.Triangles, 3, 1, propertyBlock);
                            }
                        }
                    }

                    using (new ProfilingScope(cmd, m_ReprojectionSampler))
                    {
                        // if (passData.useComputeShader)
                        // {
                        //     ConstantBuffer.Push(cmd, m_Variables, m_ComputeShader, ShaderConstants.ShaderVariablesScreenSpaceReflection);
                        //     cmd.SetComputeTextureParam(passData.ComputeShader, m_ReprojectionKernel, PipelineShaderIDs._ColorPyramidTexture, data.CameraColorTexture);
                        //     cmd.SetComputeTextureParam(passData.ComputeShader, m_ReprojectionKernel, ShaderConstants.SsrHitPointTexture, data.HitPointTexture);
                        //     cmd.SetComputeTextureParam(passData.ComputeShader, m_ReprojectionKernel, ShaderConstants.SsrLightingTexture, data.SsrLightingTexture);
                        //     cmd.SetComputeTextureParam(passData.ComputeShader, m_ReprojectionKernel, ShaderConstants._GBuffer2, passData.GBuffer2);
                        //     
                        //     int groupsX = PostProcessingUtils.DivRoundUp(m_SSRColorDescriptor.width, 8);
                        //     int groupsY = PostProcessingUtils.DivRoundUp(m_SSRColorDescriptor.height, 8);
                        //     cmd.DispatchCompute(passData.ComputeShader, m_ReprojectionKernel, groupsX, groupsY, 1);
                        // }
                        // else
                        {
                            var propertyBlock = new MaterialPropertyBlock();
                            propertyBlock.SetTexture(PipelineShaderIDs._ColorPyramidTexture, data.CameraColorTexture);
                            propertyBlock.SetTexture(ShaderConstants.SsrHitPointTexture, data.HitPointTexture);
                            propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                            propertyBlock.SetTexture(ShaderConstants._GBuffer2, data.GBuffer2);
                            SetupMaterials(propertyBlock, cameraData.camera);
                            cmd.SetRenderTarget(ssrLightingTexture);
                            cmd.DrawProcedural(Matrix4x4.identity, data.Material, (int)ShaderPasses.Reproject, MeshTopology.Triangles, 3, 1, propertyBlock);
                        }
                    }

                    using (new ProfilingScope(cmd, m_AccumulationSampler))
                    {
                    }

                    // Apply SSR
                    {
                        m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.SsrLightingTexture, ssrLightingTexture);
                        // cmd.SetRenderTarget(destination);
                        Blitter.BlitCameraTexture(cmd, source, destination, data.Material, (int)ShaderPasses.Composite);
                    }
                });
            }
            
            // Set global texture for SSR result
            // RenderGraphUtils.SetGlobalTexture(renderGraph, ShaderConstants.SSR_Lighting_Texture, finalResult);
        }

    }
}