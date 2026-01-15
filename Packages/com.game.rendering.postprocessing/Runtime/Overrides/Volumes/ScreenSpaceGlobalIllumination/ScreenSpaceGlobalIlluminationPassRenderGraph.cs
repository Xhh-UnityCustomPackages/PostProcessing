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
            // public ShaderVariablesBilateralUpsample Variables;
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



        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            // Prepare SSGI data
            PrepareSSGIData(cameraData);

            // Prepare shader variables
            PrepareVariables(cameraData.camera);


            // Import external textures
            var depthPyramidTexture = renderGraph.ImportTexture(postProcessCamera.DepthPyramidRT);
            var normalTexture = resource.cameraNormalsTexture;

            // Get previous frame color pyramid
            var preFrameColorRT = postProcessCamera.CameraPreviousColorTextureRT;
            if (preFrameColorRT == null)
                return;

            var colorPyramidTexture = renderGraph.ImportTexture(preFrameColorRT);

            // Get history depth texture
            var historyDepthRT = postProcessCamera.GetCurrentFrameRT((int)FrameHistoryType.Depth);
            if (!historyDepthRT.IsValid())
            {
                historyDepthRT = postProcessCamera.DepthPyramidRT;
            }

            var historyDepthTexture = renderGraph.ImportTexture(historyDepthRT);

            bool isNewFrame = false;
            // Get motion vector texture
            var motionVectorTexture = resource.motionVectorColor;
            // motionVectorTexture = motionVectorTexture.IsValid() && isNewFrame ? motionVectorTexture : renderGraph.ImportTexture(_rendererData.GetBlackTextureRT());

            // Get exposure textures
            var exposureTexture = renderGraph.ImportTexture(postProcessCamera.GetExposureTexture());
            var prevExposureTexture = renderGraph.ImportTexture(postProcessCamera.GetPreviousExposureTexture());

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
            }
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
            // if (volume.enableProbeVolumes.value && context.SampleProbeVolumes)
            // {
            //     _ssgiComputeShader.EnableKeyword("_PROBE_VOLUME_ENABLE");
            // }
            // else
            // {
            //     _ssgiComputeShader.DisableKeyword("_PROBE_VOLUME_ENABLE");
            // }
        }

        private TextureHandle RenderTracePass(RenderGraph renderGraph, TextureHandle depthPyramidTexture, TextureHandle normalTexture, bool useAsyncCompute)
        {
            using (var builder = renderGraph.AddComputePass<TracePassData>("SSGI Trace", out var passData, TracingSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);

                // passData.Variables = _giVariables;
                // passData.ComputeShader = _ssgiComputeShader;
                // passData.TraceKernel = _halfResolution ? _traceHalfKernel : _traceKernel;
                // passData.Width = _rtWidth;
                // passData.Height = _rtHeight;
                // passData.ViewCount = 1;
                // passData.OffsetBuffer = context.DepthMipChainInfo.GetOffsetBufferData(
                //     _rendererData.DepthPyramidMipLevelOffsetsBuffer);

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

                // var historyNormalRT = _rendererData.GetCurrentFrameRT((int)FrameHistoryType.Normal);
                // if (historyNormalRT.IsValid())
                // {
                //     TextureHandle historyNormalTexture = renderGraph.ImportTexture(historyNormalRT);
                //     builder.UseTexture(historyNormalTexture);
                //     passData.HistoryNormalTexture = historyNormalTexture;
                //     sizeAndScale = _rendererData.EvaluateRayTracingHistorySizeAndScale(historyNormalRT);
                // }
                // else
                // {
                //     passData.HistoryNormalTexture = normalTexture;
                // }
                //
                // passData.TemporalFilterCS = _temporalFilterCS;
                // passData.ValidateHistoryKernel = _validateHistoryKernel;
                // passData.HistoryValidity = historyValidity;
                // passData.PixelSpreadAngleTangent = GetPixelSpreadTangent(cameraData.camera.fieldOfView, (int)_screenWidth, (int)_screenHeight);
                // passData.HistorySizeAndScale = sizeAndScale;
                // passData.Width = (int)_screenWidth;
                // passData.Height = (int)_screenHeight;
                // passData.ViewCount = 1;
                //
                // // Create validation buffer
                // var validationDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                // {
                //     colorFormat = GraphicsFormat.R8_UInt,
                //     enableRandomWrite = true,
                //     name = "SSGI Validation Buffer",
                //     clearBuffer = true,
                //     clearColor = Color.black
                // };
                // var validation = renderGraph.CreateTexture(validationDesc);
                // builder.UseTexture(validation, AccessFlags.Write);
                // passData.ValidationBufferTexture = validation;
                //
                // builder.UseTexture(depthTexture);
                // passData.DepthTexture = depthTexture;
                // builder.UseTexture(historyDepthTexture);
                // passData.HistoryDepthTexture = historyDepthTexture;
                // builder.UseTexture(normalTexture);
                // passData.NormalTexture = normalTexture;
                // builder.UseTexture(motionVectorTexture);
                // passData.MotionVectorTexture = motionVectorTexture;
                
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
         
    }
}