using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class GroundTruthAmbientOcclusionRenderer
    {
        bool m_AOHistoryReady = false;
        
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            // RenderAmbientOcclusion(renderGraph);
        }

        TextureHandle CreateAmbientOcclusionTexture(RenderGraph renderGraph, bool fullResolution)
        {
            if (fullResolution)
                return renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true) { enableRandomWrite = true, format = GraphicsFormat.R8_UNorm, name = "Ambient Occlusion" });
            else
                return renderGraph.CreateTexture(new TextureDesc(Vector2.one * 0.5f, true, true) { enableRandomWrite = true, format = GraphicsFormat.R32_SFloat, name = "Final Half Res AO Packed" });
        }

        TextureHandle RenderAmbientOcclusion(RenderGraph renderGraph, in TextureHandle depthBuffer, in TextureHandle depthPyramid, in TextureHandle normalBuffer,
            in TextureHandle motionVectors, in PackedMipChainInfo depthMipInfo/*,
            in TextureHandle historyValidityBuffer, ShaderVariablesRaytracing shaderVariablesRaytracing, in TextureHandle rayCountTexture*/)
        {
            TextureHandle result;

            using (new RenderGraphProfilingScope(renderGraph, profilingSampler))
            {
                // if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.RayTracing) && settings.rayTracing.value && GetRayTracingState())
                //     result = RenderRTAO(renderGraph, hdCamera, depthBuffer, normalBuffer, motionVectors, historyValidityBuffer, rayCountTexture, shaderVariablesRaytracing);
                // else
                {
                    // m_AOHistoryReady = !hdCamera.AllocateAmbientOcclusionHistoryBuffer(settings.fullResolution ? 1.0f : 0.5f);
                    m_AOHistoryReady = false;
                    
                    var historyRT = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.AmbientOcclusion);
                    var currentHistory = renderGraph.ImportTexture(historyRT);
                    var outputHistory = renderGraph.ImportTexture(postProcessData.GetPreviousFrameRT((int)FrameHistoryType.AmbientOcclusion));

                    Vector2 historySize = historyRT.GetScaledSize();
                    var rtScaleForHistory = postProcessData.historyRTHandleProperties.rtHandleScale;

                    var aoParameters = PrepareRenderAOParameters(postProcessData, historySize * rtScaleForHistory, depthMipInfo);

                    result = RenderAO(renderGraph, aoParameters, depthPyramid, normalBuffer);
                    if (aoParameters.temporalAccumulation || aoParameters.fullResolution)
                        result = SpatialDenoiseAO(renderGraph, aoParameters, result);
                    if (aoParameters.temporalAccumulation)
                        result = TemporalDenoiseAO(renderGraph, aoParameters, depthPyramid, motionVectors, result, currentHistory, outputHistory);
                    if (!aoParameters.fullResolution)
                        result = UpsampleAO(renderGraph, aoParameters, result, depthPyramid);
                }
            }
           

            // PushFullScreenDebugTexture(m_RenderGraph, result, FullScreenDebugMode.ScreenSpaceAmbientOcclusion);

            return result;
        }
        
        class RenderAOPassData
        {
            public RenderAOParameters parameters;

            public ComputeShader gtaoCS;
            public int gtaoKernel;

            public TextureHandle packedData;
            public TextureHandle depthPyramid;
            public TextureHandle normalBuffer;
        }
        
         TextureHandle RenderAO(RenderGraph renderGraph, in RenderAOParameters parameters, in TextureHandle depthPyramid, in TextureHandle normalBuffer)
        {
            using (var builder = renderGraph.AddComputePass<RenderAOPassData>("GTAO Horizon search and integration", out var passData, m_SamplerHorizonSSAO))
            {
                builder.EnableAsyncCompute(parameters.runAsync);

                passData.parameters = parameters;
                passData.gtaoCS = m_GTAOCS;
                passData.gtaoCS.shaderKeywords = null;

                if (parameters.temporalAccumulation)
                    passData.gtaoCS.EnableKeyword("TEMPORAL");
                if (parameters.fullResolution)
                    passData.gtaoCS.EnableKeyword("FULL_RES");
                else
                    passData.gtaoCS.EnableKeyword("HALF_RES");

                passData.gtaoKernel = passData.gtaoCS.FindKernel("GTAOMain");

                float scaleFactor = parameters.fullResolution ? 1.0f : 0.5f;

                passData.packedData = renderGraph.CreateTexture(new TextureDesc(Vector2.one * scaleFactor, true, true)
                { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "AO Packed data" });
                builder.UseTexture(passData.packedData, AccessFlags.Write);
                passData.depthPyramid = depthPyramid;
                builder.UseTexture(passData.depthPyramid, AccessFlags.Read);
                passData.normalBuffer = normalBuffer;
                builder.UseTexture(passData.normalBuffer, AccessFlags.Read);

                builder.SetRenderFunc(
                    static (RenderAOPassData data, ComputeGraphContext ctx) =>
                    {
                        var cmd = CommandBufferHelpersExtensions.GetNativeCommandBuffer(ctx.cmd);
                        ConstantBuffer.Push(cmd, data.parameters.cb, data.gtaoCS, ShaderIDs._ShaderVariablesAmbientOcclusion);
                        ctx.cmd.SetComputeTextureParam(data.gtaoCS, data.gtaoKernel, ShaderIDs._AOPackedData, data.packedData);
                        ctx.cmd.SetComputeTextureParam(data.gtaoCS, data.gtaoKernel, PipelineShaderIDs._CameraNormalsTexture, data.normalBuffer);
                        ctx.cmd.SetComputeTextureParam(data.gtaoCS, data.gtaoKernel, PipelineShaderIDs._DepthPyramid, data.depthPyramid);

                        const int groupSizeX = 8;
                        const int groupSizeY = 8;
                        int threadGroupX = ((int)data.parameters.runningRes.x + (groupSizeX - 1)) / groupSizeX;
                        int threadGroupY = ((int)data.parameters.runningRes.y + (groupSizeY - 1)) / groupSizeY;

                        ctx.cmd.DispatchCompute(data.gtaoCS, data.gtaoKernel, threadGroupX, threadGroupY, data.parameters.viewCount);
                    });

                return passData.packedData;
            }
        }
         
          class SpatialDenoiseAOPassData
        {
            public RenderAOParameters parameters;
            public ComputeShader spatialDenoiseAOCS;
            public int denoiseKernelSpatial;

            public TextureHandle packedData;
            public TextureHandle denoiseOutput;
        }

        TextureHandle SpatialDenoiseAO(RenderGraph renderGraph, in RenderAOParameters parameters, in TextureHandle aoPackedData)
        {
            using (var builder = renderGraph.AddComputePass<SpatialDenoiseAOPassData>("Spatial Denoise GTAO", out var passData))
            {
                builder.EnableAsyncCompute(parameters.runAsync);

                float scaleFactor = parameters.fullResolution ? 1.0f : 0.5f;

                passData.parameters = parameters;

                passData.spatialDenoiseAOCS = m_GTAOSpatialDenoiseCS;
                passData.spatialDenoiseAOCS.shaderKeywords = null;
                if (parameters.temporalAccumulation)
                    passData.spatialDenoiseAOCS.EnableKeyword("TO_TEMPORAL");
                passData.denoiseKernelSpatial = passData.spatialDenoiseAOCS.FindKernel("SpatialDenoise");

                passData.packedData = aoPackedData;
                builder.UseTexture(passData.packedData, AccessFlags.Read);
                if (parameters.temporalAccumulation)
                {
                    passData.denoiseOutput = renderGraph.CreateTexture(
                        new TextureDesc(Vector2.one * (parameters.fullResolution ? 1.0f : 0.5f), true, true) { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "AO Packed blurred data" });
                }
                else
                {
                    passData.denoiseOutput = CreateAmbientOcclusionTexture(renderGraph, parameters.fullResolution);
                }
                builder.UseTexture(passData.denoiseOutput, AccessFlags.Write);

                builder.SetRenderFunc(
                    static (SpatialDenoiseAOPassData data, ComputeGraphContext ctx) =>
                    {
                        const int groupSizeX = 8;
                        const int groupSizeY = 8;
                        int threadGroupX = PostProcessingUtils.DivRoundUp((int)data.parameters.runningRes.x, groupSizeX);
                        int threadGroupY = PostProcessingUtils.DivRoundUp((int)data.parameters.runningRes.y, groupSizeY);

                        var blurCS = data.spatialDenoiseAOCS;
                        var cmd = CommandBufferHelpersExtensions.GetNativeCommandBuffer(ctx.cmd);
                        ConstantBuffer.Set<ShaderVariablesAmbientOcclusion>(cmd, blurCS, ShaderIDs._ShaderVariablesAmbientOcclusion);

                        // Spatial
                        ctx.cmd.SetComputeTextureParam(blurCS, data.denoiseKernelSpatial, ShaderIDs._AOPackedData, data.packedData);
                        ctx.cmd.SetComputeTextureParam(blurCS, data.denoiseKernelSpatial, ShaderIDs._OcclusionTexture, data.denoiseOutput);
                        ctx.cmd.DispatchCompute(blurCS, data.denoiseKernelSpatial, threadGroupX, threadGroupY, data.parameters.viewCount);
                    });

                return passData.denoiseOutput;
            }
        }
        
        class TemporalDenoiseAOPassData
        {
            public RenderAOParameters parameters;

            public ComputeShader temporalDenoiseAOCS;
            public int denoiseKernelTemporal;
            public ComputeShader copyHistoryAOCS;
            public int denoiseKernelCopyHistory;
            public bool historyReady;

            public TextureHandle packedDataBlurred;
            public TextureHandle currentHistory;
            public TextureHandle outputHistory;
            public TextureHandle denoiseOutput;
            public TextureHandle motionVectors;
        }

        TextureHandle TemporalDenoiseAO(RenderGraph renderGraph,
            in RenderAOParameters parameters,
            TextureHandle depthTexture,
            TextureHandle motionVectors,
            TextureHandle aoPackedDataBlurred,
            TextureHandle currentHistory,
            TextureHandle outputHistory)
        {
            using (var builder = renderGraph.AddComputePass<TemporalDenoiseAOPassData>("Temporal Denoise GTAO", out var passData))
            {
                builder.EnableAsyncCompute(parameters.runAsync);

                float scaleFactor = parameters.fullResolution ? 1.0f : 0.5f;

                passData.parameters = parameters;
                passData.temporalDenoiseAOCS = m_GTAOTemporalDenoiseCS;
                passData.temporalDenoiseAOCS.shaderKeywords = null;
                if (parameters.fullResolution)
                    passData.temporalDenoiseAOCS.EnableKeyword("FULL_RES");
                else
                    passData.temporalDenoiseAOCS.EnableKeyword("HALF_RES");
                passData.denoiseKernelTemporal = passData.temporalDenoiseAOCS.FindKernel("TemporalDenoise");
                passData.copyHistoryAOCS = m_GTAOCopyHistoryCS;
                passData.denoiseKernelCopyHistory = passData.copyHistoryAOCS.FindKernel("GTAODenoise_CopyHistory");
                passData.historyReady = m_AOHistoryReady;

                passData.motionVectors = motionVectors;
                builder.UseTexture(passData.motionVectors, AccessFlags.Read);
                passData.currentHistory = currentHistory;
                builder.UseTexture(passData.currentHistory, AccessFlags.Read); // can also be written on first frame, but since it's an imported resource, it doesn't matter in term of lifetime.
                passData.outputHistory = outputHistory;
                builder.UseTexture(passData.outputHistory, AccessFlags.Write);
                passData.packedDataBlurred = aoPackedDataBlurred;
                builder.UseTexture(passData.packedDataBlurred, AccessFlags.Read);
                passData.denoiseOutput = CreateAmbientOcclusionTexture(renderGraph, parameters.fullResolution);
                builder.UseTexture(passData.denoiseOutput, AccessFlags.Write);

                builder.SetRenderFunc(
                    static (TemporalDenoiseAOPassData data, ComputeGraphContext ctx) =>
                    {
                        const int groupSizeX = 8;
                        const int groupSizeY = 8;
                        int threadGroupX = ((int)data.parameters.runningRes.x + (groupSizeX - 1)) / groupSizeX;
                        int threadGroupY = ((int)data.parameters.runningRes.y + (groupSizeY - 1)) / groupSizeY;

                        if (!data.historyReady)
                        {
                            ctx.cmd.SetComputeTextureParam(data.copyHistoryAOCS, data.denoiseKernelCopyHistory, ShaderIDs._InputTexture, data.packedDataBlurred);
                            ctx.cmd.SetComputeTextureParam(data.copyHistoryAOCS, data.denoiseKernelCopyHistory, ShaderIDs._OutputTexture, data.currentHistory);
                            ctx.cmd.DispatchCompute(data.copyHistoryAOCS, data.denoiseKernelCopyHistory, threadGroupX, threadGroupY, data.parameters.viewCount);
                        }

                        var blurCS = data.temporalDenoiseAOCS;
                        var cmd = CommandBufferHelpersExtensions.GetNativeCommandBuffer(ctx.cmd);
                        ConstantBuffer.Set<ShaderVariablesAmbientOcclusion>(cmd, blurCS, ShaderIDs._ShaderVariablesAmbientOcclusion);

                        // Temporal
                        ctx.cmd.SetComputeTextureParam(blurCS, data.denoiseKernelTemporal, ShaderIDs._AOPackedBlurred, data.packedDataBlurred);
                        ctx.cmd.SetComputeTextureParam(blurCS, data.denoiseKernelTemporal, ShaderIDs._AOPackedHistory, data.currentHistory);
                        ctx.cmd.SetComputeTextureParam(blurCS, data.denoiseKernelTemporal, ShaderIDs._AOOutputHistory, data.outputHistory);
                        ctx.cmd.SetComputeTextureParam(blurCS, data.denoiseKernelTemporal, PipelineShaderIDs._MotionVectorTexture, data.motionVectors);
                        ctx.cmd.SetComputeTextureParam(blurCS, data.denoiseKernelTemporal, ShaderIDs._OcclusionTexture, data.denoiseOutput);
                        ctx.cmd.DispatchCompute(blurCS, data.denoiseKernelTemporal, threadGroupX, threadGroupY, data.parameters.viewCount);
                    });

                return passData.denoiseOutput;
            }
        }

        class UpsampleAOPassData
        {
            public RenderAOParameters parameters;

            public ComputeShader upsampleAndBlurAOCS;
            public int upsampleAOKernel;

            public TextureHandle depthTexture;
            public TextureHandle input;
            public TextureHandle output;
        }

        TextureHandle UpsampleAO(RenderGraph renderGraph, in RenderAOParameters parameters, in TextureHandle input, in TextureHandle depthTexture)
        {
            if (parameters.fullResolution)
                return input;

            using (var builder = renderGraph.AddComputePass<UpsampleAOPassData>("Upsample GTAO", out var passData, m_SamplerUpSampleSSAO))
            {
                builder.EnableAsyncCompute(parameters.runAsync);

                passData.parameters = parameters;
                passData.upsampleAndBlurAOCS = m_GTAOBlurAndUpsample;
                if (parameters.temporalAccumulation)
                    passData.upsampleAOKernel = passData.upsampleAndBlurAOCS.FindKernel(parameters.bilateralUpsample ? "BilateralUpsampling" : "BoxUpsampling");
                else
                    passData.upsampleAOKernel = passData.upsampleAndBlurAOCS.FindKernel("BlurUpsample");
                passData.input = input;
                builder.UseTexture(passData.input, AccessFlags.Read);
                passData.depthTexture = depthTexture;
                builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                passData.output = CreateAmbientOcclusionTexture(renderGraph, true);
                builder.UseTexture(passData.output, AccessFlags.Write);

                builder.SetRenderFunc(
                    static (UpsampleAOPassData data, ComputeGraphContext ctx) =>
                    {
                        var cmd = CommandBufferHelpersExtensions.GetNativeCommandBuffer(ctx.cmd);
                        ConstantBuffer.Set<ShaderVariablesAmbientOcclusion>(cmd, data.upsampleAndBlurAOCS, ShaderIDs._ShaderVariablesAmbientOcclusion);

                        ctx.cmd.SetComputeTextureParam(data.upsampleAndBlurAOCS, data.upsampleAOKernel, ShaderIDs._AOPackedData, data.input);
                        ctx.cmd.SetComputeTextureParam(data.upsampleAndBlurAOCS, data.upsampleAOKernel, ShaderIDs._OcclusionTexture, data.output);
                        ctx.cmd.SetComputeTextureParam(data.upsampleAndBlurAOCS, data.upsampleAOKernel, PipelineShaderIDs._DepthPyramid, data.depthTexture);

                        const int groupSizeX = 8;
                        const int groupSizeY = 8;
                        int threadGroupX = ((int)data.parameters.runningRes.x + (groupSizeX - 1)) / groupSizeX;
                        int threadGroupY = ((int)data.parameters.runningRes.y + (groupSizeY - 1)) / groupSizeY;
                        ctx.cmd.DispatchCompute(data.upsampleAndBlurAOCS, data.upsampleAOKernel, threadGroupX, threadGroupY, data.parameters.viewCount);
                    });

                return passData.output;
            }
        }
         
    }
}