using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using ShaderIDs = Game.Core.PostProcessing.VolumetricFogShaderIDs;

namespace Game.Core.PostProcessing
{
    public class ComputeMaxDepthPass : ScriptableRenderPass, IDisposable
    {
        private PostProcessData postProcessData;

        public ComputeMaxDepthPass(PostProcessData postProcessData)
        {
            this.postProcessData = postProcessData;
        }
        
        private RTHandle maxZ8xBuffer;
        private RTHandle maxZBuffer;
        private RTHandle dilatedMaxZBuffer;

        public void Dispose()
        {
            maxZ8xBuffer?.Release();
            maxZ8xBuffer = null;
            maxZBuffer?.Release();
            maxZBuffer = null;
            dilatedMaxZBuffer?.Release();
            dilatedMaxZBuffer = null;
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            int width = desc.width;
            int height = desc.height;
            desc.enableRandomWrite = true;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.dimension = TextureDimension.Tex2DArray;
            desc.volumeDepth = 1;
            
        
            desc.graphicsFormat = GraphicsFormat.R32_SFloat;

            desc.width = (int)(width * 0.125f);
            desc.height = (int)(height * 0.125f);
            RenderingUtils.ReAllocateHandleIfNeeded(ref maxZ8xBuffer, desc, FilterMode.Point, name: "MaxZ mask 8x");
            RenderingUtils.ReAllocateHandleIfNeeded(ref maxZBuffer, desc, FilterMode.Point, name: "MaxZ mask");
            desc.width = (int)(width / 16.0f);
            desc.height = (int)(height / 16.0f);
            RenderingUtils.ReAllocateHandleIfNeeded(ref dilatedMaxZBuffer, desc, FilterMode.Point, name: "Dilated MaxZ mask");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
            var generateMaxZCS = runtimeShaders.computeMaxDepth;
            generateMaxZCS.shaderKeywords = null;

            CoreUtils.SetKeyword(generateMaxZCS, "PLANAR_OBLIQUE_DEPTH", false); //是否考虑平面反射

            var maxZKernel = generateMaxZCS.FindKernel("ComputeMaxZ");
            var maxZDownsampleKernel = generateMaxZCS.FindKernel("ComputeFinalMask");
            var dilateMaxZKernel = generateMaxZCS.FindKernel("DilateMask");

            Vector2Int intermediateMaskSize = new Vector2Int();
            intermediateMaskSize.x = PostProcessingUtils.DivRoundUp(postProcessData.actualWidth, 8);
            intermediateMaskSize.y = PostProcessingUtils.DivRoundUp(postProcessData.actualHeight, 8);

            Vector2Int finalMaskSize = new Vector2Int();
            finalMaskSize.x = intermediateMaskSize.x / 2;
            finalMaskSize.y = intermediateMaskSize.y / 2;
            //TODO:mip offset
            Vector2Int minDepthMipOffset = new Vector2Int();
            minDepthMipOffset.x = 0;
            minDepthMipOffset.y = 0;

            int frameIndex = (int)VolumetricFogHDRPRenderer.VolumetricFrameIndex(postProcessData);
            var currIdx = frameIndex & 1;

            float dilationWidth;
            if (postProcessData.vBufferParams != null)
            {
                var currentParams = postProcessData.vBufferParams[currIdx];
                float ratio = (float)currentParams.viewportSize.x / (float)postProcessData.actualWidth;
                dilationWidth = ratio < 0.1f ? 2 :
                    ratio < 0.5f ? 1 : 0;
            }
            else
            {
                dilationWidth = 1;
            }
            
            var cmd = CommandBufferPool.Get("VolumetricFog");

            using (new ProfilingScope(cmd, profilingSampler))
            {
                var cs = generateMaxZCS;
                var kernel = maxZKernel;

                int maskW = intermediateMaskSize.x;
                int maskH = intermediateMaskSize.y;

                int dispatchX = maskW;
                int dispatchY = maskH;

                // --------------------------------------------------------------
                // Compute Max Z (1/8 resolution)

                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, maxZ8xBuffer);
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._CameraDepthTexture, new RenderTargetIdentifier("_CameraDepthTexture"));

                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);

                // --------------------------------------------------------------
                // Downsample to 16x16 and compute gradient if required

                kernel = maxZDownsampleKernel;

                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._InputTexture, maxZ8xBuffer);
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, maxZBuffer);
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._CameraDepthTexture, new RenderTargetIdentifier("_CameraDepthTexture"));

                Vector4 srcLimitAndDepthOffset = new Vector4(
                    maskW,
                    maskH,
                    minDepthMipOffset.x,
                    minDepthMipOffset.y
                );
                cmd.SetComputeVectorParam(cs, ShaderIDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
                cmd.SetComputeFloatParam(cs, ShaderIDs._DilationWidth, dilationWidth);

                int finalMaskW = Mathf.CeilToInt(maskW / 2.0f);
                int finalMaskH = Mathf.CeilToInt(maskH / 2.0f);

                dispatchX = PostProcessingUtils.DivRoundUp(finalMaskW, 8);
                dispatchY = PostProcessingUtils.DivRoundUp(finalMaskH, 8);

                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);

                // --------------------------------------------------------------
                // Dilate max Z and gradient.
                kernel = dilateMaxZKernel;

                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._InputTexture, maxZBuffer);
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, dilatedMaxZBuffer);
                cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._CameraDepthTexture, new RenderTargetIdentifier("_CameraDepthTexture"));

                srcLimitAndDepthOffset.x = finalMaskW;
                srcLimitAndDepthOffset.y = finalMaskH;
                cmd.SetComputeVectorParam(cs, ShaderIDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
                cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private class PassData
        {
            public ComputeShader generateMaxZCS;
            public int maxZKernel;
            public int maxZDownsampleKernel;
            public int dilateMaxZKernel;

            public Vector2Int intermediateMaskSize;
            public Vector2Int finalMaskSize;
            public Vector2Int minDepthMipOffset;

            public float dilationWidth;

            public TextureHandle depthTexture;
            public TextureHandle maxZ8xBuffer;
            public TextureHandle maxZBuffer;
            public TextureHandle dilatedMaxZBuffer;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "ComputeMaxDepth";

            // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
            using (var builder = renderGraph.AddComputePass<PassData>(passName, out var passData))
            {
                builder.AllowPassCulling(false);
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
                passData.generateMaxZCS = runtimeShaders.computeMaxDepth;
                passData.generateMaxZCS.shaderKeywords = null;

                CoreUtils.SetKeyword(passData.generateMaxZCS, "PLANAR_OBLIQUE_DEPTH", false); //是否考虑平面反射

                passData.maxZKernel = passData.generateMaxZCS.FindKernel("ComputeMaxZ");
                passData.maxZDownsampleKernel = passData.generateMaxZCS.FindKernel("ComputeFinalMask");
                passData.dilateMaxZKernel = passData.generateMaxZCS.FindKernel("DilateMask");

                passData.intermediateMaskSize.x = PostProcessingUtils.DivRoundUp(postProcessData.actualWidth, 8);
                passData.intermediateMaskSize.y = PostProcessingUtils.DivRoundUp(postProcessData.actualHeight, 8);

                passData.finalMaskSize.x = passData.intermediateMaskSize.x / 2;
                passData.finalMaskSize.y = passData.intermediateMaskSize.y / 2;
                //TODO:mip offset
                passData.minDepthMipOffset.x = 0;
                passData.minDepthMipOffset.y = 0;

                int frameIndex = (int)VolumetricFogHDRPRenderer.VolumetricFrameIndex(postProcessData);
                var currIdx = frameIndex & 1;

                if (postProcessData.vBufferParams != null)
                {
                    var currentParams = postProcessData.vBufferParams[currIdx];
                    float ratio = (float)currentParams.viewportSize.x / (float)postProcessData.actualWidth;
                    passData.dilationWidth = ratio < 0.1f ? 2 :
                        ratio < 0.5f ? 1 : 0;
                }
                else
                {
                    passData.dilationWidth = 1;
                }

                //Debug.Log(xr.viewCount);
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                passData.maxZ8xBuffer = builder.CreateTransientTexture(new TextureDesc(Vector2.one * 0.125f, true, true)
                    { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "MaxZ mask 8x" });
                builder.UseTexture(passData.maxZ8xBuffer, AccessFlags.ReadWrite);
                passData.maxZBuffer = builder.CreateTransientTexture(new TextureDesc(Vector2.one * 0.125f, true, true)
                    { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "MaxZ mask" });
                builder.UseTexture(passData.maxZBuffer, AccessFlags.ReadWrite);
                passData.dilatedMaxZBuffer = renderGraph.CreateTexture(new TextureDesc(Vector2.one / 16.0f, true, true)
                    { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "Dilated MaxZ mask" });
                builder.UseTexture(passData.dilatedMaxZBuffer, AccessFlags.ReadWrite);
                passData.depthTexture = resourceData.activeDepthTexture;

                builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                //Debug.Log(passData.dilatedMaxZBuffer.GetDescriptor(renderGraph).dimension);
                //Debug.Log(11);

                builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) =>
                    {
                        var cs = data.generateMaxZCS;
                        var kernel = data.maxZKernel;

                        int maskW = data.intermediateMaskSize.x;
                        int maskH = data.intermediateMaskSize.y;

                        int dispatchX = maskW;
                        int dispatchY = maskH;


                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, data.maxZ8xBuffer);
                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._CameraDepthTexture, data.depthTexture);
                        //Debug.Log("计算着色器尺寸X" + dispatchX.ToString() + "计算着色器尺寸Y：" + dispatchY.ToString() + "计算着色器尺寸Z:" + data.viewCount);

                        ctx.cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);

                        // --------------------------------------------------------------
                        // Downsample to 16x16 and compute gradient if required

                        kernel = data.maxZDownsampleKernel;

                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._InputTexture, data.maxZ8xBuffer);
                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, data.maxZBuffer);
                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._CameraDepthTexture, data.depthTexture);

                        Vector4 srcLimitAndDepthOffset = new Vector4(
                            maskW,
                            maskH,
                            data.minDepthMipOffset.x,
                            data.minDepthMipOffset.y
                        );
                        ctx.cmd.SetComputeVectorParam(cs, ShaderIDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
                        ctx.cmd.SetComputeFloatParam(cs, ShaderIDs._DilationWidth, data.dilationWidth);

                        int finalMaskW = Mathf.CeilToInt(maskW / 2.0f);
                        int finalMaskH = Mathf.CeilToInt(maskH / 2.0f);

                        dispatchX = PostProcessingUtils.DivRoundUp(finalMaskW, 8);
                        dispatchY = PostProcessingUtils.DivRoundUp(finalMaskH, 8);

                        ctx.cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);

                        // --------------------------------------------------------------
                        // Dilate max Z and gradient.
                        kernel = data.dilateMaxZKernel;

                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._InputTexture, data.maxZBuffer);
                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._OutputTexture, data.dilatedMaxZBuffer);
                        ctx.cmd.SetComputeTextureParam(cs, kernel, ShaderIDs._CameraDepthTexture, data.depthTexture);

                        srcLimitAndDepthOffset.x = finalMaskW;
                        srcLimitAndDepthOffset.y = finalMaskH;
                        ctx.cmd.SetComputeVectorParam(cs, ShaderIDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
                        ctx.cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);
                    }
                );

                VolumetricFogHDRPRenderer.m_MaxZTexture = passData.dilatedMaxZBuffer;
            }
        }
    }
}
