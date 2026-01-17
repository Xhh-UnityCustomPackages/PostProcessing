using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ExposureRenderer : PostProcessVolumeRenderer<Exposure>
    {
        private static readonly ProfilingSampler m_FixedExposureSampler = new ("Fixed Exposure");
        private static readonly ProfilingSampler m_DynamicExposureSampler = new("Dynamic Exposure");

        class DynamicExposureDataRenderGraph
        {
            internal ComputeShader HistogramExposureCS;
            internal ComputeShader ExposureCS;
            internal int ExposurePreparationKernel;
            internal int ExposureReductionKernel;
            
            public Vector2Int viewportSize;

            public ComputeBuffer HistogramBuffer;
            internal TextureHandle TextureMeteringMask;
            internal TextureHandle ExposureCurve;

            public Exposure.ExposureMode exposureMode;
            public bool HistogramUsesCurve;
            public bool HistogramOutputDebugData;
            
            internal int[] ExposureVariants;
            internal Vector4 ExposureParams;
            internal Vector4 ExposureParams2;
            internal Vector4 ProceduralMaskParams;
            internal Vector4 ProceduralMaskParams2;
            internal Vector4 HistogramExposureParams;
            internal Vector4 AdaptationParams;
            
            internal TextureHandle Source;
            internal TextureHandle PrevExposure;
            internal TextureHandle NextExposure;
            internal TextureHandle ExposureDebugData;
        }
        
        private RTHandle m_TextureMeteringMaskRTHandle;
        private RTHandle m_ExposureCurveRTHandle;
        
        void PreparePassDataForRenderGraph(DynamicExposureDataRenderGraph passData, ContextContainer frameData, Camera camera, IComputeRenderGraphBuilder builder, RenderGraph renderGraph)
        {
            var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<ExposureResources>();
            passData.ExposureCS = runtimeResources.exposureCS;
            passData.HistogramExposureCS = runtimeResources.HistogramExposureCS;
            passData.HistogramExposureCS.shaderKeywords = null;
            
            passData.viewportSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);

            // Setup variants
            var adaptationMode = settings.adaptationMode.value;
            
            if (IsResetHistoryEnabled())
            {
                adaptationMode = Exposure.AdaptationMode.Fixed;
            }
            
            passData.ExposureVariants = m_ExposureVariants;
            passData.ExposureVariants[0] = 1; // (int)exposureSettings.luminanceSource.value;
            passData.ExposureVariants[1] = (int)settings.meteringMode.value;
            passData.ExposureVariants[2] = (int)adaptationMode;
            passData.ExposureVariants[3] = 0;

            // Import Texture2D resources as TextureHandle with cached RTHandle wrappers
            if (m_TextureMeteringMaskRTHandle == null || settings.weightTextureMask.value == null)
            {
                // Release old RTHandle if texture changed
                if (m_TextureMeteringMaskRTHandle != null)
                {
                    RTHandles.Release(m_TextureMeteringMaskRTHandle);
                    m_TextureMeteringMaskRTHandle = null;
                }
                    
                // Create new RTHandle wrapper
                if (settings.weightTextureMask.value != null)
                {
                    m_TextureMeteringMaskRTHandle = RTHandles.Alloc(settings.weightTextureMask.value);
                }
            }

            bool useTextureMask = settings.meteringMode.value == Exposure.MeteringMode.MaskWeighted && settings.weightTextureMask.value != null;
            RTHandle meteringMaskHandle = m_TextureMeteringMaskRTHandle ?? postProcessData.GetWhiteTextureRT();
            if (!useTextureMask)
                meteringMaskHandle = postProcessData.GetWhiteTextureRT();
            var mask = renderGraph.ImportTexture(meteringMaskHandle);
            builder.UseTexture(mask);
            passData.TextureMeteringMask = mask;

            settings.ComputeProceduralMeteringParams(camera, out passData.ProceduralMaskParams, out passData.ProceduralMaskParams2);

            bool isHistogramBased = settings.mode.value == Exposure.ExposureMode.AutomaticHistogram;
            // bool needsCurve = (isHistogramBased && settings.histogramUseCurveRemapping.value) || settings.mode.value == Exposure.ExposureMode.CurveMapping;
            bool needsCurve = (isHistogramBased && settings.histogramUseCurveRemapping.value);

            passData.HistogramUsesCurve = settings.histogramUseCurveRemapping.value;

            // When recording with accumulation, unity_DeltaTime is adjusted to account for the subframes.
            // To match the ganeview's exposure adaptation when recording, we adjust similarly the speed.
            // float speedMultiplier = m_SubFrameManager.isRecording ? (float) m_SubFrameManager.subFrameCount : 1.0f;
            float speedMultiplier = 1.0f;
            passData.AdaptationParams = new Vector4(settings.adaptationSpeedLightToDark.value * speedMultiplier, settings.adaptationSpeedDarkToLight.value * speedMultiplier, 0.0f, 0.0f);

            passData.exposureMode = settings.mode.value;
            
            float limitMax = settings.limitMax.value;
            float limitMin = settings.limitMin.value;

            float curveMin = 0.0f;
            float curveMax = 0.0f;
            if (needsCurve)
            {
                PrepareExposureCurveData(out curveMin, out curveMax);
                limitMin = curveMin;
                limitMax = curveMax;
            }
            
            float m_DebugExposureCompensation = 0;
            passData.ExposureParams = new Vector4(settings.compensation.value + m_DebugExposureCompensation, limitMin, limitMax, 0f);
            passData.ExposureParams2 = new Vector4(curveMin, curveMax, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);

            // Import exposure curve texture if available with cached RTHandle wrapper
            if (m_ExposureCurveRTHandle == null || m_ExposureCurveTexture == null)
            {
                // Release old RTHandle if texture changed
                if (m_ExposureCurveRTHandle != null)
                {
                    RTHandles.Release(m_ExposureCurveRTHandle);
                    m_ExposureCurveRTHandle = null;
                }
                    
                // Create new RTHandle wrapper
                if (m_ExposureCurveTexture != null)
                {
                    m_ExposureCurveRTHandle = RTHandles.Alloc(m_ExposureCurveTexture);
                }
            }
            var curve = renderGraph.ImportTexture(m_ExposureCurveRTHandle);
            builder.UseTexture(curve);
            passData.ExposureCurve = curve;
            
            if (isHistogramBased)
            {
                PostProcessingUtils.ValidateComputeBuffer(ref postProcessData.HistogramBuffer, k_HistogramBins, sizeof(uint));
                postProcessData.HistogramBuffer.SetData(m_EmptyHistogram);    // Clear the histogram
                
                Vector2 histogramFraction = settings.histogramPercentages.value / 100.0f;
                float evRange = limitMax - limitMin;
                float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
                float histBias = -limitMin * histScale;
                passData.HistogramExposureParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
                
                passData.HistogramBuffer = postProcessData.HistogramBuffer;
                passData.HistogramOutputDebugData = false;
#if UNITY_EDITOR
                if (m_DebugSettings != null)
                {
                    passData.HistogramOutputDebugData = m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.HistogramView;
                }
#endif
                if (passData.HistogramOutputDebugData)
                {
                    passData.HistogramExposureCS.EnableKeyword("OUTPUT_DEBUG_DATA");
                }
                
                passData.ExposurePreparationKernel = passData.HistogramExposureCS.FindKernel("KHistogramGen");
                passData.ExposureReductionKernel = passData.HistogramExposureCS.FindKernel("KHistogramReduce");
            } 
            else
            {
                passData.ExposurePreparationKernel = passData.ExposureCS.FindKernel("KPrePass");
                passData.ExposureReductionKernel = passData.ExposureCS.FindKernel("KReduction");
            }
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resource = frameData.Get<UniversalResourceData>();
            
            using (var builder = renderGraph.AddComputePass<DynamicExposureDataRenderGraph>(profilingSampler.name, out var passData))
            {
                PreparePassDataForRenderGraph(passData, frameData, cameraData.camera, builder, renderGraph);
                postProcessData.GrabExposureRequiredTextures(out var prevExposure, out var nextExposure);
                var preExposure = renderGraph.ImportTexture(prevExposure);
                builder.UseTexture(preExposure);
                passData.PrevExposure = preExposure;
                var nextExposureHandle = renderGraph.ImportTexture(nextExposure);
                builder.UseTexture(nextExposureHandle, AccessFlags.Write);
                passData.NextExposure = nextExposureHandle;


                builder.UseTexture(resource.activeColorTexture);
                passData.Source = resource.activeColorTexture;
                
                builder.AllowGlobalStateModification(true);
                // builder.SetGlobalTextureAfterPass(exposureHandleRG, "_AutoExposureLUT");
                bool isFixedExposure = postProcessData.CanRunFixedExposurePass();
                if (!isFixedExposure && passData.HistogramOutputDebugData)
                {
                    var exposureDebugData = postProcessData.GetExposureDebugData();
                    var debugDataHandle = renderGraph.ImportTexture(exposureDebugData);
                    builder.UseTexture(debugDataHandle, AccessFlags.Write);
                    passData.ExposureDebugData = debugDataHandle;
                }
                
                builder.SetRenderFunc(static (DynamicExposureDataRenderGraph data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    
                    if (data.exposureMode == Exposure.ExposureMode.Fixed)
                    {
                        using (new ProfilingScope(cmd, m_FixedExposureSampler))
                        {
                            DoFixedExposureRenderGraph(cmd, data);
                        }
                    }
                    else
                    {
                        
                        using (new ProfilingScope(cmd, m_DynamicExposureSampler))
                        {
                            DoHistogramBasedExposure(cmd, data);
                        }
                    }
                    
                    cmd.SetGlobalTexture("_AutoExposureLUT", data.NextExposure);
                });
            }
        }

        static void DoFixedExposureRenderGraph(ComputeCommandBuffer cmd, DynamicExposureDataRenderGraph exposureData)
        {
            ComputeShader cs = exposureData.ExposureCS;
            int kernel = 0;
            float m_DebugExposureCompensation = 0;
            Vector4 exposureParams;
            Vector4 exposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);
            
            VolumeStack stack = VolumeManager.instance.stack;
            Exposure settings = stack.GetComponent<Exposure>();
            
            // if (settings.mode.value == Exposure.ExposureMode.Fixed)
            {
                kernel = cs.FindKernel("KFixedExposure");
                exposureParams = new Vector4(settings.compensation.value + m_DebugExposureCompensation, settings.fixedExposure.value, 0f, 0f);
            }
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, exposureParams2);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, exposureData.NextExposure);
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }
        
         static void DoHistogramBasedExposure(ComputeCommandBuffer cmd, DynamicExposureDataRenderGraph data)
        {
            var cs = data.HistogramExposureCS;
            int kernel;
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams, data.ProceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams2, data.ProceduralMaskParams2);

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._HistogramExposureParams, data.HistogramExposureParams);

            // Generate histogram.
            kernel = data.ExposurePreparationKernel;
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, data.PrevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, data.Source);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureWeightMask, data.TextureMeteringMask);

            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, data.ExposureVariants);

            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);
            
            int threadGroupSizeX = 16;
            int threadGroupSizeY = 8;
            int dispatchSizeX = DivRoundUp(data.viewportSize.x / 2, threadGroupSizeX);
            int dispatchSizeY = DivRoundUp(data.viewportSize.y / 2, threadGroupSizeY);
            
            cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);
            
            // Now read the histogram
            kernel = data.ExposureReductionKernel;
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, data.ExposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, data.ExposureParams2);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._AdaptationParams, data.AdaptationParams);
            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, data.PrevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, data.NextExposure);
            
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureCurveTexture, data.ExposureCurve);
            data.ExposureVariants[3] = 0;
            if (data.HistogramUsesCurve)
            {
                data.ExposureVariants[3] = 2;
            }
            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants,  data.ExposureVariants);

            if (data.HistogramOutputDebugData)
            {
                cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureDebugTexture, data.ExposureDebugData);
            }
            
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }
    }
}