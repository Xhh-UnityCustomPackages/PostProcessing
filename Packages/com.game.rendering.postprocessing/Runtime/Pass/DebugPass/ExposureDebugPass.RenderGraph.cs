using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ExposureDebugPass
    {
        class DebugImageHistogramData
        {
            public ComputeShader DebugImageHistogramCS;
            public ComputeBuffer HistogramBuffer;

            public int DebugImageHistogramKernel;
            public int CameraWidth, CameraHeight;

            public TextureHandle SourceTexture;
        }
        
        private class DebugExposurePassData
        {
            internal Material DebugExposureMaterial;
            internal TextureHandle OutputTexture;
            internal TextureHandle SourceTexture;
            internal TextureHandle CurrentExposure;
            internal TextureHandle PreviousExposure;
            internal TextureHandle ExposureDebugData;
            internal TextureHandle WeightTextureMask;
            internal ComputeBuffer HistogramBuffer;
            internal TextureHandle DebugFontTex;
            
            internal Vector4 ProceduralMaskParams;
            internal Vector4 ProceduralMaskParams2;
            internal Vector4 HistogramParams;
            internal Vector4 ExposureVariants;
            internal Vector4 ExposureParams;
            internal Vector4 ExposureParams2;
            internal Vector4 MousePixelCoord;
            internal Vector4 ExposureDebugParams;
            
            internal int PassIndex;
            internal Exposure.ExposureDebugMode ExposureDebugMode;
        }
        
        private class FinalBlitPassData
        {
            internal TextureHandle Source;
            internal TextureHandle Destination;
        }

        private RTHandle _debugFontTexRTHandle;
        private RTHandle _weightTextureMaskRTHandle;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            PrepareDebugExposureData(cameraData);

            // Import textures
            //必须要开启CopyColor
            var colorBeforePostProcess = resource.cameraOpaqueTexture;
            if (!colorBeforePostProcess.IsValid())
            {
                Debug.LogError("Exposure Debug must open opaue texute in pipeline setting");
            }

            TextureHandle sourceBeforePostProcess = colorBeforePostProcess;
            TextureHandle colorAfterPostProcess = resource.activeColorTexture;
            TextureHandle debugOutputTexture = renderGraph.ImportTexture(DebugExposureTexture);

            // Stage 1: Generate debug histogram
            using (var builder = renderGraph.AddComputePass<DebugImageHistogramData>("Debug Image Histogram",
                       out var histogramPassData, new ProfilingSampler("Debug Image Histogram")))
            {
                histogramPassData.DebugImageHistogramCS = m_DebugImageHistogramCS;
                histogramPassData.DebugImageHistogramKernel = m_DebugImageHistogramKernel;
                builder.UseTexture(sourceBeforePostProcess);
                histogramPassData.SourceTexture = sourceBeforePostProcess;
                histogramPassData.HistogramBuffer = m_DebugImageHistogramBuffer;
                histogramPassData.CameraWidth = cameraData.camera.pixelWidth;
                histogramPassData.CameraHeight = cameraData.camera.pixelHeight;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (DebugImageHistogramData data, ComputeGraphContext context) =>
                {
                    DoGenerateDebugImageHistogram(data, context.cmd);
                });
            }

            // Stage 2: Render debug exposure overlay
            using (var builder = renderGraph.AddRasterRenderPass<DebugExposurePassData>("Debug Exposure Overlay",
                       out var debugPassData, profilingSampler))
            {
                PrepareDebugPassData(debugPassData, cameraData, renderGraph, colorAfterPostProcess, debugOutputTexture);
                
                builder.SetRenderAttachment(debugOutputTexture, 0);
                debugPassData.OutputTexture = debugOutputTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc(static (DebugExposurePassData data, RasterGraphContext context) =>
                {
                    DoDebugExposure(data, context.cmd);
                });
            }

            // Stage 3: Final blit to camera target
            using (var builder = renderGraph.AddRasterRenderPass<FinalBlitPassData>("Debug Exposure Final Blit",
                       out var blitPassData, new ProfilingSampler("Debug Exposure Final Blit")))
            {
                builder.UseTexture(debugOutputTexture);
                blitPassData.Source = debugOutputTexture;
                builder.SetRenderAttachment(resource.activeColorTexture, 0);
                blitPassData.Destination = resource.activeColorTexture;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (FinalBlitPassData data, RasterGraphContext context) =>
                {
                    // Blit debug output to camera target
                    Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }
        }

        private void PrepareDebugExposureData(UniversalCameraData cameraData)
        {
            settings = VolumeManager.instance.stack.GetComponent<Exposure>();
            
            var descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = (int)DepthBits.None;
            descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref DebugExposureTexture, descriptor, wrapMode: TextureWrapMode.Clamp, name: "ExposureDebug");
            
            PostProcessingUtils.ValidateComputeBuffer(ref m_DebugImageHistogramBuffer, k_DebugImageHistogramBins, 4 * sizeof(uint));
            m_DebugImageHistogramBuffer.SetData(m_EmptyDebugImageHistogram); // Clear the histogram
        }
        
        private static void DoGenerateDebugImageHistogram(DebugImageHistogramData data, ComputeCommandBuffer cmd)
        {
            var cs = data.DebugImageHistogramCS;
            var kernel = data.DebugImageHistogramKernel;
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, data.SourceTexture);
            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);

            int threadGroupSizeX = 16;
            int threadGroupSizeY = 16;
            int dispatchSizeX = PostProcessingUtils.DivRoundUp(data.CameraWidth / 2, threadGroupSizeX);
            int dispatchSizeY = PostProcessingUtils.DivRoundUp(data.CameraHeight / 2, threadGroupSizeY);
            cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);
        }

        private void PrepareDebugPassData(DebugExposurePassData passData, UniversalCameraData cameraData,
            RenderGraph renderGraph, TextureHandle sourceTexture, TextureHandle outputTexture)
        {
            passData.DebugExposureMaterial = m_DebugExposureMaterial;
            passData.SourceTexture = sourceTexture;
            passData.OutputTexture = outputTexture;
            
            passData.HistogramBuffer = m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.FinalImageHistogramView ? m_DebugImageHistogramBuffer : m_Data.HistogramBuffer;

            var currentExposure = m_Data.GetExposureTexture();
            var previousExposure = m_Data.GetPreviousExposureTexture();
            var debugExposureData = m_Data.GetExposureDebugData();
            passData.CurrentExposure = renderGraph.ImportTexture(currentExposure);
            passData.PreviousExposure = renderGraph.ImportTexture(previousExposure);
            passData.ExposureDebugData = renderGraph.ImportTexture(debugExposureData);
            
            // Cache debug font RTHandle (only allocate once as it doesn't change)
            if (_debugFontTexRTHandle == null)
            {
                _debugFontTexRTHandle = RTHandles.Alloc(debugFontTex);
            }
            passData.DebugFontTex = renderGraph.ImportTexture(_debugFontTexRTHandle);
            
            // Import Texture2D resources as TextureHandle with cached RTHandle wrappers
            var currentWeightMask = settings.weightTextureMask.value;
            if (_weightTextureMaskRTHandle == null || currentWeightMask == null)
            {
                // Release old RTHandle if texture changed
                if (_weightTextureMaskRTHandle != null)
                {
                    RTHandles.Release(_weightTextureMaskRTHandle);
                    _weightTextureMaskRTHandle = null;
                }
                
                // Create new RTHandle wrapper
                if (currentWeightMask != null)
                {
                    _weightTextureMaskRTHandle = RTHandles.Alloc(currentWeightMask);
                }
            }
            
            RTHandle weightMaskHandle = _weightTextureMaskRTHandle ?? m_Data.GetWhiteTextureRT();
            passData.WeightTextureMask = renderGraph.ImportTexture(weightMaskHandle);
            
            
            settings.ComputeProceduralMeteringParams(cameraData.camera, 
                out passData.ProceduralMaskParams, out passData.ProceduralMaskParams2);
            Vector4 exposureParams = new Vector4(settings.compensation.value, settings.limitMin.value, settings.limitMax.value, 0f);

            Vector4 exposureVariants = new Vector4(1.0f, (int)settings.meteringMode.value, (int)settings.adaptationMode.value, 0.0f);
            Vector2 histogramFraction = settings.histogramPercentages.value / 100.0f;
            float evRange = settings.limitMax.value - settings.limitMin.value;
            float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
            float histBias = -settings.limitMin.value * histScale;
            Vector4 histogramParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);

            passData.ExposureParams = exposureParams;
            passData.ExposureVariants = exposureVariants;
            passData.HistogramParams = histogramParams;
            passData.ExposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, 
                ColorUtils.s_LightMeterCalibrationConstant);
            
            passData.MousePixelCoord = PostProcessingUtils.GetMouseCoordinates(cameraData.camera);
            passData.ExposureDebugMode = m_DebugSettings.exposureDebugMode;
            
            // Determine pass index and debug params based on debug mode
            passData.PassIndex = 0;
            if (passData.ExposureDebugMode == Exposure.ExposureDebugMode.MeteringWeighted)
            {
                passData.PassIndex = 1;
                passData.ExposureDebugParams = new Vector4(m_DebugSettings.displayMaskOnly ? 1 : 0, 0, 0, 0);
            }
            else if (passData.ExposureDebugMode == Exposure.ExposureDebugMode.HistogramView)
            {
                var tonemappingSettings = VolumeManager.instance.stack.GetComponent<Tonemapping>();
                // var tonemappingMode = tonemappingSettings.IsActive() ? tonemappingSettings.mode.value : TonemappingMode.None;
                var tonemappingMode = 0;
                bool centerAroundMiddleGrey = m_DebugSettings.centerHistogramAroundMiddleGrey;
                bool displayOverlay = m_DebugSettings.displayOnSceneOverlay;
                passData.ExposureDebugParams = new Vector4(0.0f, (int)tonemappingMode, 
                    centerAroundMiddleGrey ? 1 : 0, displayOverlay ? 1 : 0);
                passData.PassIndex = 2;
            }
            else if (passData.ExposureDebugMode == Exposure.ExposureDebugMode.FinalImageHistogramView)
            {
                bool finalImageRGBHistogram = m_DebugSettings.displayFinalImageHistogramAsRGB;
                passData.ExposureDebugParams = new Vector4(0, 0, 0, finalImageRGBHistogram ? 1 : 0);
                passData.PassIndex = 3;
            }
        }

        private static void DoDebugExposure(DebugExposurePassData data, RasterCommandBuffer cmd)
        {
            var material = data.DebugExposureMaterial;
            
            material.SetVector(ExposureShaderIDs._ProceduralMaskParams, data.ProceduralMaskParams);
            material.SetVector(ExposureShaderIDs._ProceduralMaskParams2, data.ProceduralMaskParams2);
            material.SetVector(ExposureShaderIDs._HistogramExposureParams, data.HistogramParams);
            material.SetVector(ExposureShaderIDs._Variants, data.ExposureVariants);
            material.SetVector(ExposureShaderIDs._ExposureParams, data.ExposureParams);
            material.SetVector(ExposureShaderIDs._ExposureParams2, data.ExposureParams2);
            material.SetVector(ExposureShaderIDs._MousePixelCoord, data.MousePixelCoord);
            material.SetTexture(ExposureShaderIDs._SourceTexture, data.SourceTexture);
            material.SetTexture(ExposureShaderIDs._DebugFullScreenTexture, data.SourceTexture);
            material.SetTexture(ExposureShaderIDs._PreviousExposureTexture, data.PreviousExposure);
            material.SetTexture(ExposureShaderIDs._ExposureTexture, data.CurrentExposure);
            material.SetTexture(ExposureShaderIDs._ExposureWeightMask, data.WeightTextureMask);
            material.SetBuffer(ExposureShaderIDs._HistogramBuffer, data.HistogramBuffer);
            material.SetTexture(ExposureShaderIDs._DebugFont, data.DebugFontTex);
            
            if (data.ExposureDebugMode == Exposure.ExposureDebugMode.HistogramView)
            {
                material.SetTexture(ExposureShaderIDs._ExposureDebugTexture, data.ExposureDebugData);
            }
            else if (data.ExposureDebugMode == Exposure.ExposureDebugMode.FinalImageHistogramView)
            {
                material.SetBuffer(ExposureShaderIDs._FullImageHistogram, data.HistogramBuffer);
            }
            
            material.SetVector(ExposureShaderIDs._ExposureDebugParams, data.ExposureDebugParams);
            
            cmd.DrawProcedural(Matrix4x4.identity, material, data.PassIndex, MeshTopology.Triangles, 3, 1);
        }
    }
}