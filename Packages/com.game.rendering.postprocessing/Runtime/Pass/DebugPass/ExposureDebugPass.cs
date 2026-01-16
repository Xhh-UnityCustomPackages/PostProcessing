using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ExposureDebugPass : ScriptableRenderPass, IDisposable
    {
         private const int k_DebugImageHistogramBins = 256;   // Important! If this changes, need to change HistogramExposure.compute
         private readonly int[] m_EmptyDebugImageHistogram = new int[k_DebugImageHistogramBins * 4];
        
         private PostProcessData m_Data;
         
         private Exposure settings;
         private ComputeBuffer m_DebugImageHistogramBuffer;
         private RTHandle DebugExposureTexture;
         private DebugExposureData m_DebugExposure;
         private DebugImageHistogramData m_DebugImageHistogramData;
         private Texture2D debugFontTex;
         private ExposureDebugSettings m_DebugSettings;
         
         public class DebugExposureData
         {
             public ExposureDebugSettings debugSettings;
             public Camera camera;
             public Material debugExposureMaterial;

             public Vector4 proceduralMeteringParams1;
             public Vector4 proceduralMeteringParams2;
             public RTHandle colorBuffer;
             public RTHandle debugFullScreenTexture;
             public RTHandle output;
             public RTHandle currentExposure;
             public RTHandle previousExposure;
             public RTHandle debugExposureData;
             public HableCurve customToneMapCurve;
             public int lutSize;
             public ComputeBuffer histogramBuffer;
         }
         
         class DebugImageHistogramData
         {
             public ComputeShader debugImageHistogramCS;
             public ComputeBuffer imageHistogram;

             public int debugImageHistogramKernel;

             public RTHandle source;
         }

         public ExposureDebugPass(PostProcessData data, Shader DebugExposure, ComputeShader debugImageHistogramCS, ExposureDebugSettings debugSettings)
         {
             profilingSampler = new ProfilingSampler("Exposure Debug");
             renderPassEvent = RenderPassEvent.AfterRendering;
             m_Data = data;
             m_DebugSettings = debugSettings;
             m_DebugExposure = new();
             m_DebugExposure.debugSettings = m_DebugSettings;
             m_DebugExposure.debugExposureMaterial = CoreUtils.CreateEngineMaterial(DebugExposure);

             m_DebugImageHistogramData = new();
             m_DebugImageHistogramData.debugImageHistogramCS = debugImageHistogramCS;
             m_DebugImageHistogramData.debugImageHistogramKernel = m_DebugImageHistogramData.debugImageHistogramCS.FindKernel("KHistogramGen");
            

#if UNITY_EDITOR
             debugFontTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(PostProcessingUtils.packagePath + "/Textures/DebugFont.tga");
#endif
         }

         public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
         {
             settings = VolumeManager.instance.stack.GetComponent<Exposure>();
             var descriptor = renderingData.cameraData.cameraTargetDescriptor;
             descriptor.depthBufferBits = (int)DepthBits.None;
             descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
             RenderingUtils.ReAllocateHandleIfNeeded(ref DebugExposureTexture, descriptor, wrapMode: TextureWrapMode.Clamp, name: "ExposureDebug");
         }

#if UNITY_EDITOR
         public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
         {
             var cmd = CommandBufferPool.Get();
             using (new ProfilingScope(cmd, profilingSampler))
             {
                 ExposureRenderer.SetDebugSetting(m_DebugSettings, m_Data.GetExposureDebugData());
                 var colorBeforePostProcess = renderingData.cameraData.renderer.GetCameraColorFrontBuffer(cmd);
                 DoGenerateDebugImageHistogram(cmd, renderingData.cameraData.camera, colorBeforePostProcess, m_DebugImageHistogramData);
                 var colorAfterPostProcess = renderingData.cameraData.renderer.GetCameraColorBackBuffer(cmd);
                 DoDebugExposure(cmd, ref renderingData, colorAfterPostProcess, m_DebugExposure);
                 cmd.Blit(DebugExposureTexture, colorAfterPostProcess);
             }
             context.ExecuteCommandBuffer(cmd);
             CommandBufferPool.Release(cmd);
         }

         public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
         {
             var resource = frameData.Get<UniversalResourceData>();
             var cameraData = frameData.Get<UniversalCameraData>();
             ExposureRenderer.SetDebugSetting(m_DebugSettings, m_Data.GetExposureDebugData());
             
             // Import textures
             // var colorBeforePostProcess = m_Data.GetPreviousFrameColorRT(frameData, out _);
             // TextureHandle sourceBeforePostProcess = renderGraph.ImportTexture(colorBeforePostProcess);
             // TextureHandle colorAfterPostProcess = resource.activeColorTexture;
             // TextureHandle debugOutputTexture = renderGraph.ImportTexture(m_Data.DebugExposureTexture);
         }
#endif
        // private void PrepareDebugExposureData(UniversalCameraData cameraData)
        // {
        //     var postProcessCamera = m_Context.GetPostProcessCamera(renderingData.cameraData.camera);
        //     settings = VolumeManager.instance.stack.GetComponent<Exposure>();
        //     IllusionRenderingUtils.ValidateComputeBuffer(ref postProcessCamera.DebugImageHistogram, k_DebugImageHistogramBins, 4 * sizeof(uint));
        //     postProcessCamera.DebugImageHistogram.SetData(_emptyDebugImageHistogram);    // Clear the histogram
        //     
        //     var descriptor = cameraData.cameraTargetDescriptor;
        //     descriptor.depthBufferBits = (int)DepthBits.None;
        //     descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        //     RenderingUtils.ReAllocateHandleIfNeeded(ref postProcessCamera.DebugExposureTexture, descriptor, wrapMode: TextureWrapMode.Clamp, name: "ExposureDebug");
        // }


        private void DoDebugExposure(CommandBuffer cmd, ref RenderingData renderingData, RTHandle sourceTexture, DebugExposureData data)
         {
             data.debugExposureData = m_Data.GetExposureDebugData();
             
             
             var camera = renderingData.cameraData.camera;

             data.camera = camera;
             data.colorBuffer = sourceTexture;
             data.debugFullScreenTexture = sourceTexture;
             data.histogramBuffer = data.debugSettings.exposureDebugMode == Exposure.ExposureDebugMode.FinalImageHistogramView ? m_DebugImageHistogramBuffer : ExposureRenderer.GetHistogramBuffer();
             // data.customToneMapCurve = 
             
             settings.ComputeProceduralMeteringParams(camera, out data.proceduralMeteringParams1, out data.proceduralMeteringParams2);
             Vector4 exposureParams = new Vector4(settings.compensation.value, settings.limitMin.value, settings.limitMax.value, 0f);

             Vector4 exposureVariants = new Vector4(1.0f, (int)settings.meteringMode.value, (int)settings.adaptationMode.value, 0.0f);
             Vector2 histogramFraction = settings.histogramPercentages.value / 100.0f;
             float evRange = settings.limitMax.value - settings.limitMin.value;
             float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
             float histBias = -settings.limitMin.value * histScale;
             Vector4 histogramParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
             
             data.debugExposureMaterial.SetVector(ExposureShaderIDs._ProceduralMaskParams, data.proceduralMeteringParams1);
             data.debugExposureMaterial.SetVector(ExposureShaderIDs._ProceduralMaskParams2, data.proceduralMeteringParams2);

             data.debugExposureMaterial.SetVector(ExposureShaderIDs._HistogramExposureParams, histogramParams);
             data.debugExposureMaterial.SetVector(ExposureShaderIDs._Variants, exposureVariants);
             data.debugExposureMaterial.SetVector(ExposureShaderIDs._ExposureParams, exposureParams);
             data.debugExposureMaterial.SetVector(ExposureShaderIDs._ExposureParams2, new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant));
             data.debugExposureMaterial.SetVector(ExposureShaderIDs._MousePixelCoord, PostProcessingUtils.GetMouseCoordinates(ref renderingData.cameraData));
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._SourceTexture, data.colorBuffer);
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._DebugFullScreenTexture, data.debugFullScreenTexture);

             data.previousExposure = m_Data.GetPreviousExposureTexture();
             data.currentExposure = m_Data.GetExposureTexture();
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._PreviousExposureTexture, data.previousExposure);
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._ExposureTexture, data.currentExposure);

             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._ExposureWeightMask, settings.weightTextureMask.value);
             data.debugExposureMaterial.SetBuffer(ExposureShaderIDs._HistogramBuffer, data.histogramBuffer);
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._DebugFont, debugFontTex);


             int passIndex = 0;
             if (m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.MeteringWeighted)
             {
                 passIndex = 1;
                 data.debugExposureMaterial.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(data.debugSettings.displayMaskOnly ? 1 : 0, 0, 0, 0));
             }

             if (m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.HistogramView)
             {
                 data.debugExposureMaterial.SetTexture(ExposureShaderIDs._ExposureDebugTexture, data.debugExposureData);
                 var tonemappingSettings = VolumeManager.instance.stack.GetComponent<Tonemapping>();
                 // var tonemappingMode = tonemappingSettings.IsActive() ? tonemappingSettings.mode.value : TonemappingMode.None;
                 var tonemappingMode = 0;
                 
                 bool centerAroundMiddleGrey = data.debugSettings.centerHistogramAroundMiddleGrey;
                 bool displayOverlay = data.debugSettings.displayOnSceneOverlay;
                 data.debugExposureMaterial.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(0.0f, (int)tonemappingMode, centerAroundMiddleGrey ? 1 : 0, displayOverlay ? 1 : 0));
                 passIndex = 2;
             }

             if (m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.FinalImageHistogramView)
             {
                 bool finalImageRGBHistogram = data.debugSettings.displayFinalImageHistogramAsRGB;
                 data.debugExposureMaterial.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(0, 0, 0, finalImageRGBHistogram ? 1 : 0));
                 data.debugExposureMaterial.SetBuffer(ExposureShaderIDs._FullImageHistogram, m_DebugImageHistogramBuffer);
                 passIndex = 3;
             }
             
             CoreUtils.SetRenderTarget(cmd, DebugExposureTexture);
             cmd.DrawProcedural(Matrix4x4.identity, data.debugExposureMaterial, passIndex, MeshTopology.Triangles, 3, 1);
         }

         private void DoGenerateDebugImageHistogram(CommandBuffer cmd, Camera camera, RTHandle sourceTexture, DebugImageHistogramData data)
         {
             if (m_DebugSettings.exposureDebugMode != Exposure.ExposureDebugMode.FinalImageHistogramView)
                 return;
             
             
             PostProcessingUtils.ValidateComputeBuffer(ref m_DebugImageHistogramBuffer, k_DebugImageHistogramBins, 4 * sizeof(uint));
             m_DebugImageHistogramBuffer.SetData(m_EmptyDebugImageHistogram); // Clear the histogram
             
             data.imageHistogram = m_DebugImageHistogramBuffer;
             
             int cameraWidth = camera.pixelWidth;
             int cameraHeight = camera.pixelHeight;
             cmd.SetComputeTextureParam(data.debugImageHistogramCS, data.debugImageHistogramKernel, ExposureShaderIDs._SourceTexture, sourceTexture);
             cmd.SetComputeBufferParam(data.debugImageHistogramCS, data.debugImageHistogramKernel, ExposureShaderIDs._HistogramBuffer, data.imageHistogram);

             int threadGroupSizeX = 16;
             int threadGroupSizeY = 16;
             int dispatchSizeX = ExposureRenderer.DivRoundUp(cameraWidth / 2, threadGroupSizeX);
             int dispatchSizeY = ExposureRenderer.DivRoundUp(cameraHeight / 2, threadGroupSizeY);
             cmd.DispatchCompute(data.debugImageHistogramCS, data.debugImageHistogramKernel, dispatchSizeX, dispatchSizeY, 1);
         }

         public void Dispose()
         {
             CoreUtils.Destroy(m_DebugExposure.debugExposureMaterial);
             m_DebugExposure = null;
             m_DebugImageHistogramData = null;
             
             m_DebugImageHistogramBuffer?.Release();
         }
    }
}