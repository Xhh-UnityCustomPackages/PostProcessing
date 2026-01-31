using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ScreenSpaceGlobalIlluminationRenderer
    {
        // RenderGraph PassData classes
        private class TracePassData
        {
            public ScreenSpaceGlobalIlluminationVariables Variables;
            public ComputeShader ComputeShader;
            public int TraceKernel;
            public int Width;
            public int Height;
            public int ViewCount;
            public ComputeBuffer OffsetBuffer;

            public TextureHandle HitPointTexture;
            public TextureHandle DepthPyramidTexture;
            public TextureHandle NormalTexture;
        }

        private class ReprojectPassData
        {
            public ScreenSpaceGlobalIlluminationVariables Variables;
            public ComputeShader ComputeShader;
            public int ReprojectKernel;
            public int Width;
            public int Height;
            public int ViewCount;
            public ComputeBuffer OffsetBuffer;
            public bool IsNewFrame;

            public TextureHandle HitPointTexture;
            public TextureHandle DepthPyramidTexture;
            public TextureHandle NormalTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle ColorPyramidTexture;
            public TextureHandle HistoryDepthTexture;
            public TextureHandle ExposureTexture;
            public TextureHandle PrevExposureTexture;
            public TextureHandle OutputTexture;
        }

        private class ValidateHistoryPassData
        {
            public ComputeShader TemporalFilterCS;
            public int ValidateHistoryKernel;
            public float HistoryValidity;
            public float PixelSpreadAngleTangent;
            public Vector4 HistorySizeAndScale;
            public int Width;
            public int Height;
            public int ViewCount;

            public TextureHandle DepthTexture;
            public TextureHandle HistoryDepthTexture;
            public TextureHandle NormalTexture;
            public TextureHandle HistoryNormalTexture;
            public TextureHandle MotionVectorTexture;
            public TextureHandle ValidationBufferTexture;
        }

        private class TemporalDenoisePassData
        {
            public ComputeShader TemporalFilterCS;
            public int TemporalAccumulationKernel;
            public int CopyHistoryKernel;
            public float HistoryValidity;
            public float PixelSpreadAngleTangent;
            public Vector4 ResolutionMultiplier;
            public int Width;
            public int Height;
            public int ViewCount;

            public TextureHandle InputTexture;
            public TextureHandle HistoryBuffer;
            public TextureHandle DepthTexture;
            public TextureHandle ValidationBuffer;
            public TextureHandle MotionVectorTexture;
            public TextureHandle ExposureTexture;
            public TextureHandle PrevExposureTexture;
            public TextureHandle OutputTexture;
        }

        private class SpatialDenoisePassData
        {
            public ComputeShader DiffuseDenoiserCS;
            public int BilateralFilterKernel;
            public int GatherKernel;
            public float DenoiserFilterRadius;
            public float PixelSpreadAngleTangent;
            public int HalfResolutionFilter;
            public int JitterFramePeriod;
            public Vector4 ResolutionMultiplier;
            public int Width;
            public int Height;
            public int ViewCount;
            public GraphicsBuffer PointDistribution;

            public TextureHandle InputTexture;
            public TextureHandle DepthTexture;
            public TextureHandle NormalTexture;
            public TextureHandle IntermediateTexture;
            public TextureHandle OutputTexture;
        }

        private class UpsamplePassData
        {
            public ShaderVariablesBilateralUpsample Variables;
            public ComputeShader BilateralUpsampleCS;
            public int UpsampleKernel;
            public Vector4 HalfScreenSize;
            public int Width;
            public int Height;
            public int ViewCount;

            public TextureHandle LowResolutionTexture;
            public TextureHandle OutputTexture;
        }

        private class InitializeDiffuseDenoiserPassData
        {
            public ComputeShader DiffuseDenoiserCS;
            public int GeneratePointDistributionKernel;
            public GraphicsBuffer PointDistribution;
        }

        private ShaderVariablesBilateralUpsample _upsampleVariables;

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            // Prepare SSGI data
            PrepareSSGIData(cameraData);
            
            // Prepare shader variables
            PrepareVariables(cameraData.camera);

            // Import external textures
            var depthPyramidTexture = renderGraph.ImportTexture(postProcessData.DepthPyramidRT);
            var normalTexture = resource.cameraNormalsTexture;
           

            // Get previous frame color pyramid
            var preFrameColorRT = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);
            if (preFrameColorRT == null)
                return;

            var colorPyramidTexture = renderGraph.ImportTexture(preFrameColorRT);

            // Get history depth texture
            var historyDepthRT = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.Depth);
            if (!historyDepthRT.IsValid())
            {
                historyDepthRT = postProcessData.DepthPyramidRT;
            }

            var historyDepthTexture = renderGraph.ImportTexture(historyDepthRT);

            bool isNewFrame = false;
            // Get motion vector texture
            var motionVectorTexture = resource.motionVectorColor;
            motionVectorTexture = motionVectorTexture.IsValid() && isNewFrame ? motionVectorTexture : renderGraph.ImportTexture(postProcessData.GetBlackTextureRT());

            // Get exposure textures
            var exposureTexture = renderGraph.ImportTexture(postProcessData.GetExposureTexture());
            var prevExposureTexture = renderGraph.ImportTexture(postProcessData.GetPreviousExposureTexture());

            // Use async compute for trace and reproject
            bool useAsyncCompute = false; // Can be enabled if needed

            // Execute trace pass
            var hitPointTexture = RenderTracePass(renderGraph, depthPyramidTexture, normalTexture, useAsyncCompute);
           
            // Execute reproject pass
            var giTexture = RenderReprojectPass(renderGraph, hitPointTexture,
                depthPyramidTexture, normalTexture, motionVectorTexture, colorPyramidTexture,
                historyDepthTexture, exposureTexture, prevExposureTexture, isNewFrame, useAsyncCompute);
            
            // Execute denoising pipeline if enabled
            if (_needDenoise)
            {
                // Initialize denoiser if needed (only once)
                if (!_denoiserInitialized)
                {
                    RenderInitializeDiffuseDenoiserPass(renderGraph);
                    _denoiserInitialized = true;
                }
                
                // Validate history
                var validationTexture = RenderValidateHistoryPass(renderGraph, cameraData,
                    depthPyramidTexture, normalTexture, historyDepthTexture,
                    motionVectorTexture, 1.0f);
                
                // Allocate first history buffer
                float scaleFactor = _halfResolution ? 0.5f : 1.0f;
                var historyBuffer1 = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.ScreenSpaceGlobalIllumination);
                if (scaleFactor != _historyResolutionScale || historyBuffer1 == null)
                {
                    postProcessData.ReleaseHistoryFrameRT((int)FrameHistoryType.ScreenSpaceGlobalIllumination);
                    var historyAllocator = new PostProcessData.CustomHistoryAllocator(
                        new Vector2(scaleFactor, scaleFactor),
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "IndirectDiffuseHistoryBuffer");
                    historyBuffer1 = postProcessData.AllocHistoryFrameRT((int)FrameHistoryType.ScreenSpaceGlobalIllumination,
                        historyAllocator.Allocator, 1);
                }
                var historyTexture1 = renderGraph.ImportTexture(historyBuffer1);
                
                float resolutionMultiplier = _halfResolution ? 0.5f : 1.0f;
                var temporalOutput = RenderTemporalDenoisePass(renderGraph, cameraData,
                    giTexture, historyTexture1, depthPyramidTexture, validationTexture,
                    motionVectorTexture, exposureTexture, prevExposureTexture, resolutionMultiplier);
                
                // First spatial denoise pass
                bool halfResFilter = settings.halfResolutionDenoiser.value;
                var spatialOutput = RenderSpatialDenoisePass(renderGraph, cameraData,
                    temporalOutput, depthPyramidTexture, normalTexture,
                    settings.denoiserRadius.value, halfResFilter, settings.secondDenoiserPass.value, resolutionMultiplier);
                
                giTexture = spatialOutput;
                
                // Second denoise pass if enabled
                if (settings.secondDenoiserPass.value)
                {
                    var historyBuffer2 = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.ScreenSpaceGlobalIllumination2);
                    if (scaleFactor != _historyResolutionScale || historyBuffer2 == null)
                    {
                        postProcessData.ReleaseHistoryFrameRT((int)FrameHistoryType.ScreenSpaceGlobalIllumination2);
                        var historyAllocator2 = new PostProcessData.CustomHistoryAllocator(
                            new Vector2(scaleFactor, scaleFactor),
                            GraphicsFormat.R16G16B16A16_SFloat,
                            "IndirectDiffuseHistoryBuffer2");
                        historyBuffer2 = postProcessData.AllocHistoryFrameRT((int)FrameHistoryType.ScreenSpaceGlobalIllumination2,
                            historyAllocator2.Allocator, 1);
                    }
                    var historyTexture2 = renderGraph.ImportTexture(historyBuffer2);
                    
                    temporalOutput = RenderTemporalDenoisePass(renderGraph, cameraData,
                        giTexture, historyTexture2, depthPyramidTexture, validationTexture,
                        motionVectorTexture, exposureTexture, prevExposureTexture, resolutionMultiplier);
                    
                    spatialOutput = RenderSpatialDenoisePass(renderGraph, cameraData,
                        temporalOutput, depthPyramidTexture, normalTexture,
                        settings.denoiserRadius.value * 0.5f, halfResFilter, false, resolutionMultiplier);
                    
                    giTexture = spatialOutput;
                }
                _historyResolutionScale = scaleFactor;
            }
            
            // Upsample if half resolution
            if (_halfResolution)
            {
                giTexture = RenderUpsamplePass(renderGraph, giTexture);
            }
            
            // Set global texture
            RenderGraphUtils.SetGlobalTexture(renderGraph, PipelineShaderIDs._IndirectDiffuseTexture, giTexture);
            
        }

        private void PrepareSSGIData(UniversalCameraData cameraData)
        {
            // Get SSGI volume settings
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();
            if (!volume || !volume.enable.value)
                return;

            _needDenoise = volume.denoise.value;
            _screenWidth = cameraData.cameraTargetDescriptor.width;
            _screenHeight = cameraData.cameraTargetDescriptor.height;
            _halfResolution = volume.halfResolution.value;

            int resolutionDivider = _halfResolution ? 2 : 1;
            _rtWidth = (int)_screenWidth / resolutionDivider;
            _rtHeight = (int)_screenHeight / resolutionDivider;

            // Configure probe volumes keyword
            if (volume.enableProbeVolumes.value /*&& context.SampleProbeVolumes*/)
            {
                _ssgiComputeShader.EnableKeyword("_PROBE_VOLUME_ENABLE");
            }
            else
            {
                _ssgiComputeShader.DisableKeyword("_PROBE_VOLUME_ENABLE");
            }
        }

        private TextureHandle RenderTracePass(RenderGraph renderGraph, TextureHandle depthPyramidTexture, TextureHandle normalTexture, bool useAsyncCompute)
        {
            using (var builder = renderGraph.AddComputePass<TracePassData>("SSGI Trace", out var passData, TracingSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);

                passData.Variables = _giVariables;
                passData.ComputeShader = _ssgiComputeShader;
                passData.TraceKernel = _halfResolution ? _traceHalfKernel : _traceKernel;
                passData.Width = _rtWidth;
                passData.Height = _rtHeight;
                passData.ViewCount = 1;
                passData.OffsetBuffer = postProcessData.DepthMipChainInfo.GetOffsetBufferData(
                    postProcessData.DepthPyramidMipLevelOffsetsBuffer);
                
                // Create output texture
                var hitPointDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                {
                    colorFormat = GraphicsFormat.R16G16_SFloat,
                    enableRandomWrite = true,
                    name = "SSGI Hit Point"
                };
                var hitPoint = renderGraph.CreateTexture(hitPointDesc);
                builder.UseTexture(hitPoint, AccessFlags.Write);
                passData.HitPointTexture = hitPoint;
                
                builder.UseTexture(depthPyramidTexture);
                passData.DepthPyramidTexture = depthPyramidTexture;
                builder.UseTexture(normalTexture);
                passData.NormalTexture = normalTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((TracePassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd.GetNativeCommandBuffer();
                    postProcessData.BindDitheredRNGData8SPP(cmd);
                    
                    ConstantBuffer.Push(cmd, data.Variables, data.ComputeShader, Properties.ShaderVariablesSSGI);
                    
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.TraceKernel,
                        PipelineShaderIDs._DepthPyramid, data.DepthPyramidTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.TraceKernel,
                        PipelineShaderIDs._GBuffer2, data.NormalTexture);
                    context.cmd.SetComputeBufferParam(data.ComputeShader, data.TraceKernel,
                        PipelineShaderIDs._DepthPyramidMipLevelOffsets, data.OffsetBuffer);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.TraceKernel,
                        Properties.IndirectDiffuseHitPointTextureRW, data.HitPointTexture);
                    
                    int tilesX = PostProcessingUtils.DivRoundUp(data.Width, 8);
                    int tilesY = PostProcessingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.ComputeShader, data.TraceKernel, tilesX, tilesY, data.ViewCount);
                });

                return passData.HitPointTexture;
            }
        }

        private TextureHandle RenderReprojectPass(RenderGraph renderGraph,
            TextureHandle hitPointTexture, TextureHandle depthPyramidTexture, TextureHandle normalTexture,
            TextureHandle motionVectorTexture, TextureHandle colorPyramidTexture, TextureHandle historyDepthTexture,
            TextureHandle exposureTexture, TextureHandle prevExposureTexture, bool isNewFrame, bool useAsyncCompute)
        {
            using (var builder = renderGraph.AddComputePass<ReprojectPassData>("SSGI Reproject", out var passData, ReprojectSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);

                passData.Variables = _giVariables;
                passData.ComputeShader = _ssgiComputeShader;
                passData.ReprojectKernel = _halfResolution ? _reprojectHalfKernel : _reprojectKernel;
                passData.Width = _rtWidth;
                passData.Height = _rtHeight;
                passData.ViewCount = 1;
                passData.OffsetBuffer = postProcessData.DepthMipChainInfo.GetOffsetBufferData(
                    postProcessData.DepthPyramidMipLevelOffsetsBuffer);
                passData.IsNewFrame = isNewFrame;
                
                // Create output texture
                var outputDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                {
                    colorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                    enableRandomWrite = true,
                    name = "SSGI Output"
                };
                var output = renderGraph.CreateTexture(outputDesc);
                builder.UseTexture(output, AccessFlags.Write);
                passData.OutputTexture = output;
                
                builder.UseTexture(hitPointTexture);
                passData.HitPointTexture = hitPointTexture;
                builder.UseTexture(depthPyramidTexture);
                passData.DepthPyramidTexture = depthPyramidTexture;
                builder.UseTexture(normalTexture);
                passData.NormalTexture = normalTexture;
                builder.UseTexture(motionVectorTexture);
                passData.MotionVectorTexture = motionVectorTexture;
                builder.UseTexture(colorPyramidTexture);
                passData.ColorPyramidTexture = colorPyramidTexture;
                builder.UseTexture(historyDepthTexture);
                passData.HistoryDepthTexture = historyDepthTexture;
                builder.UseTexture(exposureTexture);
                passData.ExposureTexture = exposureTexture;
                builder.UseTexture(prevExposureTexture);
                passData.PrevExposureTexture = prevExposureTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((ReprojectPassData data, ComputeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpersExtensions.GetNativeCommandBuffer(context.cmd);
                    ConstantBuffer.Push(cmd, data.Variables, data.ComputeShader, Properties.ShaderVariablesSSGI); 
                    
                     context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        PipelineShaderIDs._DepthPyramid, data.DepthPyramidTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        PipelineShaderIDs._GBuffer2, data.NormalTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        PipelineShaderIDs._MotionVectorTexture, data.MotionVectorTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        PipelineShaderIDs._ColorPyramidTexture, data.ColorPyramidTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        Properties.HistoryDepthTexture, data.HistoryDepthTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        Properties.IndirectDiffuseHitPointTexture, data.HitPointTexture);
                    context.cmd.SetComputeBufferParam(data.ComputeShader, data.ReprojectKernel,
                        PipelineShaderIDs._DepthPyramidMipLevelOffsets, data.OffsetBuffer);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        PipelineShaderIDs._ExposureTexture, data.ExposureTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        PipelineShaderIDs._PrevExposureTexture, data.PrevExposureTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.ReprojectKernel,
                        Properties.IndirectDiffuseTextureRW, data.OutputTexture);
                    
                    int tilesX = PostProcessingUtils.DivRoundUp(data.Width, 8);
                    int tilesY = PostProcessingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.ComputeShader, data.ReprojectKernel, tilesX, tilesY, data.ViewCount);
                    
                });

                return passData.OutputTexture;
            }
        }
        
        private void RenderInitializeDiffuseDenoiserPass(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddComputePass<InitializeDiffuseDenoiserPassData>("SSGI Initialize Denoiser", out var passData))
            {
                passData.DiffuseDenoiserCS = _diffuseDenoiserCS;
                passData.GeneratePointDistributionKernel = _generatePointDistributionKernel;
                passData.PointDistribution = _pointDistribution;
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((InitializeDiffuseDenoiserPassData data, ComputeGraphContext context) =>
                {
                    context.cmd.SetComputeBufferParam(data.DiffuseDenoiserCS, data.GeneratePointDistributionKernel,
                        Properties.PointDistributionRW, data.PointDistribution);
                    context.cmd.DispatchCompute(data.DiffuseDenoiserCS, data.GeneratePointDistributionKernel, 1, 1, 1);
                });
            }
        }
        
         private TextureHandle RenderValidateHistoryPass(RenderGraph renderGraph, UniversalCameraData cameraData,
            TextureHandle depthTexture, TextureHandle normalTexture, TextureHandle historyDepthTexture,
            TextureHandle motionVectorTexture, float historyValidity)
        {
            using (var builder = renderGraph.AddComputePass<ValidateHistoryPassData>("SSGI Validate History", out var passData))
            {
                // Get history buffers
                Vector4 sizeAndScale = Vector4.one;

                var historyNormalRT = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.Normal);
                if (historyNormalRT.IsValid())
                {
                    TextureHandle historyNormalTexture = renderGraph.ImportTexture(historyNormalRT);
                    builder.UseTexture(historyNormalTexture);
                    passData.HistoryNormalTexture = historyNormalTexture;
                    sizeAndScale = postProcessData.EvaluateRayTracingHistorySizeAndScale(historyNormalRT);
                }
                else
                {
                    passData.HistoryNormalTexture = normalTexture;
                }
                
                passData.TemporalFilterCS = _temporalFilterCS;
                passData.ValidateHistoryKernel = _validateHistoryKernel;
                passData.HistoryValidity = historyValidity;
                passData.PixelSpreadAngleTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, (int)_screenWidth, (int)_screenHeight);
                passData.HistorySizeAndScale = sizeAndScale;
                passData.Width = (int)_screenWidth;
                passData.Height = (int)_screenHeight;
                passData.ViewCount = 1;
                
                // Create validation buffer
                var validationDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                {
                    colorFormat = GraphicsFormat.R8_UInt,
                    enableRandomWrite = true,
                    name = "SSGI Validation Buffer",
                    clearBuffer = true,
                    clearColor = Color.black
                };
                var validation = renderGraph.CreateTexture(validationDesc);
                builder.UseTexture(validation, AccessFlags.Write);
                passData.ValidationBufferTexture = validation;
                
                builder.UseTexture(depthTexture);
                passData.DepthTexture = depthTexture;
                builder.UseTexture(historyDepthTexture);
                passData.HistoryDepthTexture = historyDepthTexture;
                builder.UseTexture(normalTexture);
                passData.NormalTexture = normalTexture;
                builder.UseTexture(motionVectorTexture);
                passData.MotionVectorTexture = motionVectorTexture;
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((ValidateHistoryPassData data, ComputeGraphContext context) =>
                {
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel,
                        Properties.DepthTexture, data.DepthTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel,
                        Properties.HistoryDepthTexture, data.HistoryDepthTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel,
                        Properties.NormalBufferTexture, data.NormalTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel,
                        Properties.HistoryNormalTexture, data.HistoryNormalTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel,
                        PipelineShaderIDs._MotionVectorTexture, data.MotionVectorTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel,
                        PipelineShaderIDs._GBuffer2, data.NormalTexture);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, Properties.HistoryValidity, data.HistoryValidity);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, Properties.PixelSpreadAngleTangent, data.PixelSpreadAngleTangent);
                    context.cmd.SetComputeVectorParam(data.TemporalFilterCS, Properties.HistorySizeAndScale, data.HistorySizeAndScale);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.ValidateHistoryKernel,
                        Properties.ValidationBufferRW, data.ValidationBufferTexture);
                    
                    int tilesX = PostProcessingUtils.DivRoundUp(data.Width, 8);
                    int tilesY = PostProcessingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.TemporalFilterCS, data.ValidateHistoryKernel, tilesX, tilesY, data.ViewCount);
                });
                
                return passData.ValidationBufferTexture;
            }
        }

        private TextureHandle RenderTemporalDenoisePass(RenderGraph renderGraph, UniversalCameraData cameraData,
            TextureHandle inputTexture, TextureHandle historyBuffer, TextureHandle depthTexture,
            TextureHandle validationBuffer, TextureHandle motionVectorTexture, TextureHandle exposureTexture,
            TextureHandle prevExposureTexture, float resolutionMultiplier)
        {
            using (var builder = renderGraph.AddComputePass<TemporalDenoisePassData>("SSGI Temporal Denoise", out var passData, DenoiseSampler))
            {
                passData.TemporalFilterCS = _temporalFilterCS;
                passData.TemporalAccumulationKernel = _temporalAccumulationColorKernel;
                passData.CopyHistoryKernel = _temporalFilterCopyHistoryKernel;
                passData.HistoryValidity = 1.0f;
                passData.PixelSpreadAngleTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, _rtWidth, _rtHeight);
                passData.ResolutionMultiplier = new Vector4(resolutionMultiplier, 1.0f / resolutionMultiplier, 1, 1);
                passData.Width = _rtWidth;
                passData.Height = _rtHeight;
                passData.ViewCount = 1;
                
                // Create output texture
                var outputDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "SSGI Temporal Output"
                };
                var output = renderGraph.CreateTexture(outputDesc);
                builder.UseTexture(output, AccessFlags.Write);
                passData.OutputTexture = output;
                
                builder.UseTexture(inputTexture);
                passData.InputTexture = inputTexture;
                builder.UseTexture(historyBuffer, AccessFlags.ReadWrite);
                passData.HistoryBuffer = historyBuffer;
                builder.UseTexture(depthTexture);
                passData.DepthTexture = depthTexture;
                builder.UseTexture(validationBuffer);
                passData.ValidationBuffer = validationBuffer;
                builder.UseTexture(motionVectorTexture);
                passData.MotionVectorTexture = motionVectorTexture;
                builder.UseTexture(exposureTexture);
                passData.ExposureTexture = exposureTexture;
                builder.UseTexture(prevExposureTexture);
                passData.PrevExposureTexture = prevExposureTexture;
                
                 builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((TemporalDenoisePassData data, ComputeGraphContext context) =>
                {
                    // Temporal accumulation
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        Properties.DenoiseInputTexture, data.InputTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        Properties.HistoryBuffer, data.HistoryBuffer);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        Properties.DepthTexture, data.DepthTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        Properties.ValidationBuffer, data.ValidationBuffer);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        PipelineShaderIDs._MotionVectorTexture, data.MotionVectorTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        PipelineShaderIDs._ExposureTexture, data.ExposureTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        PipelineShaderIDs._PrevExposureTexture, data.PrevExposureTexture);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, Properties.HistoryValidity, data.HistoryValidity);
                    context.cmd.SetComputeIntParam(data.TemporalFilterCS, Properties.ReceiverMotionRejection, 0);
                    context.cmd.SetComputeIntParam(data.TemporalFilterCS, Properties.OccluderMotionRejection, 0);
                    context.cmd.SetComputeFloatParam(data.TemporalFilterCS, Properties.PixelSpreadAngleTangent, data.PixelSpreadAngleTangent);
                    context.cmd.SetComputeVectorParam(data.TemporalFilterCS, Properties.DenoiserResolutionMultiplierVals, data.ResolutionMultiplier);
                    context.cmd.SetComputeIntParam(data.TemporalFilterCS, Properties.EnableExposureControl, 1);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.TemporalAccumulationKernel,
                        Properties.AccumulationOutputTextureRW, data.OutputTexture);
                    
                    int tilesX = PostProcessingUtils.DivRoundUp(data.Width, 8);
                    int tilesY = PostProcessingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.TemporalFilterCS, data.TemporalAccumulationKernel, tilesX, tilesY, data.ViewCount);
                    
                    // Copy to history
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.CopyHistoryKernel,
                        Properties.DenoiseInputTexture, data.OutputTexture);
                    context.cmd.SetComputeTextureParam(data.TemporalFilterCS, data.CopyHistoryKernel,
                        Properties.DenoiseOutputTextureRW, data.HistoryBuffer);
                    context.cmd.SetComputeVectorParam(data.TemporalFilterCS, Properties.DenoiserResolutionMultiplierVals, data.ResolutionMultiplier);
                    context.cmd.DispatchCompute(data.TemporalFilterCS, data.CopyHistoryKernel, tilesX, tilesY, data.ViewCount);
                });
                
                return passData.OutputTexture;
            }
        }

        private TextureHandle RenderSpatialDenoisePass(RenderGraph renderGraph, UniversalCameraData cameraData,
            TextureHandle inputTexture, TextureHandle depthTexture, TextureHandle normalTexture,
            float kernelSize, bool halfResolutionFilter, bool jitterFilter, float resolutionMultiplier)
        {
            using (var builder = renderGraph.AddComputePass<SpatialDenoisePassData>("SSGI Spatial Denoise", out var passData, DenoiseSampler))
            {
                passData.DiffuseDenoiserCS = _diffuseDenoiserCS;
                passData.BilateralFilterKernel = _bilateralFilterColorKernel;
                passData.GatherKernel = _gatherColorKernel;
                passData.DenoiserFilterRadius = kernelSize;
                passData.PixelSpreadAngleTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, _rtWidth, _rtHeight);
                passData.HalfResolutionFilter = halfResolutionFilter ? 1 : 0;
                int frameIndex = (int)(postProcessData.FrameCount % 16);
                passData.JitterFramePeriod = jitterFilter ? (frameIndex % 4) : -1;
                passData.ResolutionMultiplier = new Vector4(resolutionMultiplier, 1.0f / resolutionMultiplier, 0, 0);
                passData.Width = _rtWidth;
                passData.Height = _rtHeight;
                passData.ViewCount = 1;
                passData.PointDistribution = _pointDistribution;
                
                // Create output texture
                var outputDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                {
                    colorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                    enableRandomWrite = true,
                    name = "SSGI Spatial Output"
                };
                var output = renderGraph.CreateTexture(outputDesc);
                builder.UseTexture(output, AccessFlags.Write);
                passData.OutputTexture = output;
                
                builder.UseTexture(inputTexture);
                passData.InputTexture = inputTexture;
                builder.UseTexture(depthTexture);
                passData.DepthTexture = depthTexture;
                builder.UseTexture(normalTexture);
                passData.NormalTexture = normalTexture;
                
                // Create intermediate texture if half resolution filter
                if (halfResolutionFilter)
                {
                    var intermediate = renderGraph.CreateTexture(outputDesc);
                    builder.UseTexture(intermediate, AccessFlags.ReadWrite);
                    passData.IntermediateTexture = intermediate;
                }
                
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((SpatialDenoisePassData data, ComputeGraphContext context) =>
                {
                    // Setup parameters
                    context.cmd.SetComputeFloatParam(data.DiffuseDenoiserCS, Properties.DenoiserFilterRadius, data.DenoiserFilterRadius);
                    context.cmd.SetComputeFloatParam(data.DiffuseDenoiserCS, Properties.PixelSpreadAngleTangent, data.PixelSpreadAngleTangent);
                    context.cmd.SetComputeIntParam(data.DiffuseDenoiserCS, Properties.HalfResolutionFilter, data.HalfResolutionFilter);
                    context.cmd.SetComputeVectorParam(data.DiffuseDenoiserCS, Properties.DenoiserResolutionMultiplierVals, data.ResolutionMultiplier);
                    context.cmd.SetComputeIntParam(data.DiffuseDenoiserCS, Properties.JitterFramePeriod, data.JitterFramePeriod);
                    
                    // Bilateral filter
                    context.cmd.SetComputeBufferParam(data.DiffuseDenoiserCS, data.BilateralFilterKernel,
                        Properties.PointDistribution, data.PointDistribution);
                    context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.BilateralFilterKernel,
                        Properties.DenoiseInputTexture, data.InputTexture);
                    context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.BilateralFilterKernel,
                        Properties.DepthTexture, data.DepthTexture);
                    context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.BilateralFilterKernel,
                        Properties.NormalBufferTexture, data.NormalTexture);
                    
                    if (data.HalfResolutionFilter == 1)
                    {
                        context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.BilateralFilterKernel,
                            Properties.DenoiseOutputTextureRW, data.IntermediateTexture);
                    }
                    else
                    {
                        context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.BilateralFilterKernel,
                            Properties.DenoiseOutputTextureRW, data.OutputTexture);
                    }
                    
                    int tilesX = PostProcessingUtils.DivRoundUp(data.Width, 8);
                    int tilesY = PostProcessingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.DiffuseDenoiserCS, data.BilateralFilterKernel, tilesX, tilesY, data.ViewCount);
                    
                    // Gather pass if half resolution filter
                    if (data.HalfResolutionFilter == 1)
                    {
                        context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.GatherKernel,
                            Properties.DenoiseInputTexture, data.IntermediateTexture);
                        context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.GatherKernel,
                            Properties.DepthTexture, data.DepthTexture);
                        context.cmd.SetComputeTextureParam(data.DiffuseDenoiserCS, data.GatherKernel,
                            Properties.DenoiseOutputTextureRW, data.OutputTexture);
                        context.cmd.DispatchCompute(data.DiffuseDenoiserCS, data.GatherKernel, tilesX, tilesY, data.ViewCount);
                    }
                });
                
                return passData.OutputTexture;
            }
        }
        
         private TextureHandle RenderUpsamplePass(RenderGraph renderGraph, TextureHandle lowResInput)
        {
            using (var builder = renderGraph.AddComputePass<UpsamplePassData>("SSGI Upsample", out var passData))
            {
                // Setup constant buffer
                unsafe
                {
                    _upsampleVariables._HalfScreenSize = new Vector4(
                        _rtWidth,
                        _rtHeight,
                        1.0f / _rtWidth,
                        1.0f / _rtHeight);

                    // Fill distance-based weights (2x2 pattern for half resolution)
                    for (int i = 0; i < 16; ++i)
                        _upsampleVariables._DistanceBasedWeights[i] = BilateralUpsample.distanceBasedWeights_2x2[i];

                    // Fill tap offsets (2x2 pattern for half resolution)
                    for (int i = 0; i < 32; ++i)
                        _upsampleVariables._TapOffsets[i] = BilateralUpsample.tapOffsets_2x2[i];
                }
                
                passData.Variables = _upsampleVariables;
                passData.BilateralUpsampleCS = _bilateralUpsampleCS;
                passData.UpsampleKernel = _bilateralUpsampleKernel;
                passData.HalfScreenSize = new Vector4(_rtWidth, _rtHeight, 1.0f / _rtWidth, 1.0f / _rtHeight);
                passData.Width = (int)_screenWidth;
                passData.Height = (int)_screenHeight;
                passData.ViewCount = 1;
                
                // Create full resolution output texture
                var outputDesc = new TextureDesc(passData.Width, passData.Height)
                {
                    colorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                    enableRandomWrite = true,
                    name = "SSGI Upsampled"
                };
                var output = renderGraph.CreateTexture(outputDesc);
                builder.UseTexture(output, AccessFlags.Write);
                passData.OutputTexture = output;
                
                builder.UseTexture(lowResInput);
                passData.LowResolutionTexture = lowResInput;
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((UpsamplePassData data, ComputeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpersExtensions.GetNativeCommandBuffer(context.cmd);
                    ConstantBuffer.Push(cmd, data.Variables, data.BilateralUpsampleCS, Properties.ShaderVariablesBilateralUpsample);
                    
                    context.cmd.SetComputeTextureParam(data.BilateralUpsampleCS, data.UpsampleKernel,
                        Properties.LowResolutionTexture, data.LowResolutionTexture);
                    context.cmd.SetComputeVectorParam(data.BilateralUpsampleCS, Properties.HalfScreenSize, data.HalfScreenSize);
                    context.cmd.SetComputeTextureParam(data.BilateralUpsampleCS, data.UpsampleKernel,
                        Properties.OutputUpscaledTexture, data.OutputTexture);
                    
                    int tilesX = PostProcessingUtils.DivRoundUp(data.Width, 8);
                    int tilesY = PostProcessingUtils.DivRoundUp(data.Height, 8);
                    context.cmd.DispatchCompute(data.BilateralUpsampleCS, data.UpsampleKernel, tilesX, tilesY, data.ViewCount);
                });
                
                return passData.OutputTexture;
            }
        }

    }
}