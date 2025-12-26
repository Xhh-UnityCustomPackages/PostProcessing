using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Game.Core.PostProcessing.GraphicsUtility;

namespace Game.Core.PostProcessing
{
    public class ExposureDebugPass : ScriptableRenderPass, IDisposable
    {
         private const int k_DebugImageHistogramBins = 256;   // Important! If this changes, need to change HistogramExposure.compute
         private readonly int[] m_EmptyDebugImageHistogram = new int[k_DebugImageHistogramBins * 4];
         
         private Exposure settings;
         private ComputeBuffer m_DebugImageHistogramBuffer;
         
         private RTHandle DebugExposureTexture;
         private DebugExposureData m_DebugExposureData;
         private DebugImageHistogramData m_DebugImageHistogramData;
         
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

         public ExposureDebugPass(PostProcessFeatureData rendererData, ExposureDebugSettings debugSettings)
         {
             profilingSampler = new ProfilingSampler("Exposure Debug");
             renderPassEvent = RenderPassEvent.AfterRendering + 2;
             
             m_DebugExposureData = new();
             m_DebugExposureData.debugSettings = debugSettings;
             m_DebugExposureData.debugExposureMaterial = CoreUtils.CreateEngineMaterial(rendererData.shaders.DebugExposure);
             
             m_DebugImageHistogramData = new();
             m_DebugImageHistogramData.debugImageHistogramCS = rendererData.computeShaders.debugImageHistogramCS;
             m_DebugImageHistogramData.debugImageHistogramKernel = m_DebugImageHistogramData.debugImageHistogramCS.FindKernel("KHistogramGen");
         }

         public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
         {
             var descriptor = renderingData.cameraData.cameraTargetDescriptor;
             descriptor.depthBufferBits = (int)DepthBits.None;
             descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
             RenderingUtils.ReAllocateHandleIfNeeded(ref DebugExposureTexture, descriptor, wrapMode: TextureWrapMode.Clamp, name: "ExposureDebug");
         }

         public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
         {
             var cmd = CommandBufferPool.Get();
             using (new ProfilingScope(cmd, profilingSampler))
             {
                 var colorBeforePostProcess = renderingData.cameraData.renderer.GetCameraColorBackBuffer(cmd);
                 DoGenerateDebugImageHistogram(cmd, ref renderingData, colorBeforePostProcess, m_DebugImageHistogramData);
//                 var colorAfterPostProcess = renderingData.cameraData.renderer.GetCameraColorBackBuffer(cmd);
                 // DoDebugExposure(cmd, ref renderingData, colorAfterPostProcess);
                 // cmd.Blit(cmd, ref renderingData, DebugExposureTexture);
             }
             context.ExecuteCommandBuffer(cmd);
             CommandBufferPool.Release(cmd);
         }

         private void DoDebugExposure(CommandBuffer cmd, ref RenderingData renderingData, RTHandle sourceTexture, DebugExposureData data)
         {
             var camera = renderingData.cameraData.camera;

             data.camera = camera;
             
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
             data.debugExposureMaterial.SetVector(ExposureShaderIDs._MousePixelCoord, GetMouseCoordinates(ref renderingData.cameraData));
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._SourceTexture, data.colorBuffer);
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._DebugFullScreenTexture, data.colorBuffer);
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._PreviousExposureTexture, data.previousExposure);
             // data.debugExposureMaterial.SetTexture(ExposureShaderIDs._ExposureTexture, data.currentExposure);
             data.debugExposureMaterial.SetTexture(ExposureShaderIDs._ExposureWeightMask, settings.weightTextureMask.value);
             data.debugExposureMaterial.SetBuffer(ExposureShaderIDs._HistogramBuffer, data.histogramBuffer);
             // material.SetTexture(ExposureShaderIDs._DebugFont, _rendererData.RuntimeResources.debugFontTex);


             int passIndex = 0;
             if (settings.debugMode.value == Exposure.ExposureDebugMode.MeteringWeighted)
             {
                 passIndex = 1;
                 data.debugExposureMaterial.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(data.debugSettings.displayMaskOnly ? 1 : 0, 0, 0, 0));
             }

             if (settings.debugMode.value == Exposure.ExposureDebugMode.HistogramView)
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

             if (settings.debugMode.value == Exposure.ExposureDebugMode.FinalImageHistogramView)
             {
                 bool finalImageRGBHistogram = data.debugSettings.displayFinalImageHistogramAsRGB;
                 data.debugExposureMaterial.SetVector(ExposureShaderIDs._ExposureDebugParams, new Vector4(0, 0, 0, finalImageRGBHistogram ? 1 : 0));
                 data.debugExposureMaterial.SetBuffer(ExposureShaderIDs._FullImageHistogram, m_DebugImageHistogramBuffer);
                 passIndex = 3;
             }
             
             CoreUtils.SetRenderTarget(cmd, DebugExposureTexture);
             cmd.DrawProcedural(Matrix4x4.identity, data.debugExposureMaterial, passIndex, MeshTopology.Triangles, 3, 1);
         }

         private void DoGenerateDebugImageHistogram(CommandBuffer cmd, ref RenderingData renderingData, RTHandle sourceTexture, DebugImageHistogramData data)
         {
             // if (m_CurrentDebugDisplaySettings.data.lightingDebugSettings.exposureDebugMode != Exposure.ExposureDebugMode.FinalImageHistogramView)
             //     return;
             
             
             ValidateComputeBuffer(ref m_DebugImageHistogramBuffer, k_DebugImageHistogramBins, 4 * sizeof(uint));
             m_DebugImageHistogramBuffer.SetData(m_EmptyDebugImageHistogram); // Clear the histogram
             
             data.imageHistogram = m_DebugImageHistogramBuffer;
             
             int cameraWidth = renderingData.cameraData.camera.pixelWidth;
             int cameraHeight = renderingData.cameraData.camera.pixelHeight;
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
             CoreUtils.Destroy(m_DebugExposureData.debugExposureMaterial);
             m_DebugExposureData = null;
             m_DebugImageHistogramData = null;
         }
    }
}