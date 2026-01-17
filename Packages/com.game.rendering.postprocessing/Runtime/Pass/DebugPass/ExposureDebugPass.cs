using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ExposureDebugPass : ScriptableRenderPass, IDisposable
    {
         private const int k_DebugImageHistogramBins = 256;   // Important! If this changes, need to change HistogramExposure.compute
         private readonly int[] m_EmptyDebugImageHistogram = new int[k_DebugImageHistogramBins * 4];
        
         private PostProcessData m_Data;
         
         private Exposure settings;
         private ComputeBuffer m_DebugImageHistogramBuffer;
         private RTHandle DebugExposureTexture;
         private Texture2D debugFontTex;
         private ExposureDebugSettings m_DebugSettings;
         private Material m_DebugExposureMaterial;
         private ComputeShader m_DebugImageHistogramCS;
         private int m_DebugImageHistogramKernel;
        

         public ExposureDebugPass(PostProcessData data, Shader DebugExposure, ComputeShader debugImageHistogramCS, ExposureDebugSettings debugSettings)
         {
             profilingSampler = new ProfilingSampler("Exposure Debug");
             renderPassEvent = RenderPassEvent.AfterRendering;
             m_Data = data;
             m_DebugExposureMaterial = CoreUtils.CreateEngineMaterial(DebugExposure);
             m_DebugImageHistogramCS = debugImageHistogramCS;
             m_DebugImageHistogramKernel = m_DebugImageHistogramCS.FindKernel("KHistogramGen");
            
             m_DebugSettings = debugSettings;

#if UNITY_EDITOR
             debugFontTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(PostProcessingUtils.packagePath + "/Textures/DebugFont.tga");
#endif
         }

#if UNITY_EDITOR
         public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
         {
             PrepareDebugExposureData(renderingData.cameraData);
             
             var cmd = CommandBufferPool.Get();
             using (new ProfilingScope(cmd, profilingSampler))
             {
                 var colorBeforePostProcess = renderingData.cameraData.renderer.GetCameraColorFrontBuffer(cmd);
                 DoGenerateDebugImageHistogram(cmd, colorBeforePostProcess, renderingData.cameraData.camera);
                 var colorAfterPostProcess = renderingData.cameraData.renderer.GetCameraColorBackBuffer(cmd);
                 DoDebugExposure(cmd, ref renderingData, colorAfterPostProcess);
                 cmd.Blit(DebugExposureTexture, colorAfterPostProcess);
             }
             context.ExecuteCommandBuffer(cmd);
             CommandBufferPool.Release(cmd);
         }
        
#endif
        private void PrepareDebugExposureData(CameraData cameraData)
        {
            settings = VolumeManager.instance.stack.GetComponent<Exposure>();
            
            var descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = (int)DepthBits.None;
            descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref DebugExposureTexture, descriptor, wrapMode: TextureWrapMode.Clamp, name: "ExposureDebug");
            
       
            PostProcessingUtils.ValidateComputeBuffer(ref m_DebugImageHistogramBuffer, k_DebugImageHistogramBins, 4 * sizeof(uint));
            m_DebugImageHistogramBuffer.SetData(m_EmptyDebugImageHistogram); // Clear the histogram
        }

        private void DoDebugExposure(CommandBuffer cmd, ref RenderingData renderingData, RTHandle sourceTexture)
         {
             var debugExposureData = m_Data.GetExposureDebugData();
             
             var camera = renderingData.cameraData.camera;

             var colorBuffer = sourceTexture;
             var debugFullScreenTexture = sourceTexture;
             var histogramBuffer = m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.FinalImageHistogramView ? m_DebugImageHistogramBuffer : m_Data.HistogramBuffer;
             // data.customToneMapCurve = 

             settings.ComputeProceduralMeteringParams(camera, out var proceduralMeteringParams1, out var proceduralMeteringParams2);
             Vector4 exposureParams = new Vector4(settings.compensation.value, settings.limitMin.value, settings.limitMax.value, 0f);

             Vector4 exposureVariants = new Vector4(1.0f, (int)settings.meteringMode.value, (int)settings.adaptationMode.value, 0.0f);
             Vector2 histogramFraction = settings.histogramPercentages.value / 100.0f;
             float evRange = settings.limitMax.value - settings.limitMin.value;
             float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
             float histBias = -settings.limitMin.value * histScale;
             Vector4 histogramParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
             var material = m_DebugExposureMaterial;
             material.SetVector(ExposureShaderIDs._ProceduralMaskParams, proceduralMeteringParams1);
             material.SetVector(ExposureShaderIDs._ProceduralMaskParams2, proceduralMeteringParams2);

             material.SetVector(ExposureShaderIDs._HistogramExposureParams, histogramParams);
             material.SetVector(ExposureShaderIDs._Variants, exposureVariants);
             material.SetVector(ExposureShaderIDs._ExposureParams, exposureParams);
             material.SetVector(ExposureShaderIDs._ExposureParams2, new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant));
             material.SetVector(ExposureShaderIDs._MousePixelCoord, PostProcessingUtils.GetMouseCoordinates(renderingData.cameraData.camera));
             material.SetTexture(ExposureShaderIDs._SourceTexture, colorBuffer);
             material.SetTexture(ExposureShaderIDs._DebugFullScreenTexture, debugFullScreenTexture);

             var previousExposure = m_Data.GetPreviousExposureTexture();
             var currentExposure = m_Data.GetExposureTexture();
             material.SetTexture(ExposureShaderIDs._PreviousExposureTexture, previousExposure);
             material.SetTexture(ExposureShaderIDs._ExposureTexture, currentExposure);

             material.SetTexture(ExposureShaderIDs._ExposureWeightMask, settings.weightTextureMask.value);
             material.SetBuffer(ExposureShaderIDs._HistogramBuffer, histogramBuffer);
             material.SetTexture(ExposureShaderIDs._DebugFont, debugFontTex);


             int passIndex = 0;
             Vector4 exposureDebugParams = Vector4.zero;
             if (m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.MeteringWeighted)
             {
                 passIndex = 1;
                 exposureDebugParams = new Vector4(m_DebugSettings.displayMaskOnly ? 1 : 0, 0, 0, 0);
             }

             if (m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.HistogramView)
             {
                 material.SetTexture(ExposureShaderIDs._ExposureDebugTexture, debugExposureData);
                 var tonemappingSettings = VolumeManager.instance.stack.GetComponent<Tonemapping>();
                 // var tonemappingMode = tonemappingSettings.IsActive() ? tonemappingSettings.mode.value : TonemappingMode.None;
                 var tonemappingMode = 0;
                 
                 bool centerAroundMiddleGrey = m_DebugSettings.centerHistogramAroundMiddleGrey;
                 bool displayOverlay = m_DebugSettings.displayOnSceneOverlay;
                 exposureDebugParams = new Vector4(0.0f, (int)tonemappingMode, centerAroundMiddleGrey ? 1 : 0, displayOverlay ? 1 : 0);
                 passIndex = 2;
             }

             if (m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.FinalImageHistogramView)
             {
                 bool finalImageRGBHistogram = m_DebugSettings.displayFinalImageHistogramAsRGB;
                 exposureDebugParams = new Vector4(0, 0, 0, finalImageRGBHistogram ? 1 : 0);
                 material.SetBuffer(ExposureShaderIDs._FullImageHistogram, m_DebugImageHistogramBuffer);
                 passIndex = 3;
             }
             
             material.SetVector(ExposureShaderIDs._ExposureDebugParams, exposureDebugParams);
             
             CoreUtils.SetRenderTarget(cmd, DebugExposureTexture);
             cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1);
         }

         private void DoGenerateDebugImageHistogram(CommandBuffer cmd, RTHandle sourceTexture, Camera camera)
         {
             if (m_DebugSettings.exposureDebugMode != Exposure.ExposureDebugMode.FinalImageHistogramView)
                 return;
             var cs = m_DebugImageHistogramCS;
             int kernel = m_DebugImageHistogramKernel;
             int cameraWidth = camera.pixelWidth;
             int cameraHeight = camera.pixelHeight;
             cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, sourceTexture);
             cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, m_DebugImageHistogramBuffer);

             int threadGroupSizeX = 16;
             int threadGroupSizeY = 16;
             int dispatchSizeX = ExposureRenderer.DivRoundUp(cameraWidth / 2, threadGroupSizeX);
             int dispatchSizeY = ExposureRenderer.DivRoundUp(cameraHeight / 2, threadGroupSizeY);
             cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);
         }

         public void Dispose()
         {
             CoreUtils.Destroy(m_DebugExposureMaterial);
             CoreUtils.SafeRelease(m_DebugImageHistogramBuffer);
         }
    }
}