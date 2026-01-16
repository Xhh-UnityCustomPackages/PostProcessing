using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    internal static class ExposureShaderIDs
    {
        public static readonly int _PreviousExposureTexture = Shader.PropertyToID("_PreviousExposureTexture");
        public static readonly int _ExposureDebugTexture = Shader.PropertyToID("_ExposureDebugTexture");
        public static readonly int _ExposureParams = Shader.PropertyToID("_ExposureParams");
        public static readonly int _ExposureParams2 = Shader.PropertyToID("_ExposureParams2");
        public static readonly int _HistogramExposureParams = Shader.PropertyToID("_HistogramExposureParams");
        public static readonly int _HistogramBuffer = Shader.PropertyToID("_HistogramBuffer");
        public static readonly int _AdaptationParams = Shader.PropertyToID("_AdaptationParams");
        public static readonly int _ExposureCurveTexture = Shader.PropertyToID("_ExposureCurveTexture");
        public static readonly int _ExposureTexture = Shader.PropertyToID("_ExposureTexture");
        public static readonly int _ExposureWeightMask = Shader.PropertyToID("_ExposureWeightMask");
        public static readonly int _ProceduralMaskParams = Shader.PropertyToID("_ProceduralMaskParams");
        public static readonly int _ProceduralMaskParams2 = Shader.PropertyToID("_ProceduralMaskParams2");
        public static readonly int _Variants = Shader.PropertyToID("_Variants");
        public static readonly int _OutputTexture = Shader.PropertyToID("_OutputTexture");
        public static readonly int _SourceTexture = Shader.PropertyToID("_SourceTexture");
        // public static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");
        public static readonly int _MousePixelCoord = Shader.PropertyToID("_MousePixelCoord");
        public static readonly int _DebugFullScreenTexture = Shader.PropertyToID("_DebugFullScreenTexture");
        public static readonly int _ExposureDebugParams = Shader.PropertyToID("_ExposureDebugParams");
        public static readonly int _FullImageHistogram = Shader.PropertyToID("_FullImageHistogram");
        public static readonly int _DebugFont = Shader.PropertyToID("_DebugFont");
    }

    [PostProcess("Exposure", PostProcessInjectionPoint.BeforeRenderingPostProcessing)]
    public partial class ExposureRenderer : PostProcessVolumeRenderer<Exposure>
    {
        public override bool renderToCamera => false;
        
        // Exposure data
        private const int k_ExposureCurvePrecision = 128;
        private const int k_HistogramBins = 128;   // Important! If this changes, need to change HistogramExposure.compute
        private readonly Color[] m_ExposureCurveColorArray = new Color[k_ExposureCurvePrecision];
        private readonly int[] m_ExposureVariants = new int[4];
        
        private Texture2D m_ExposureCurveTexture;
        private static ComputeBuffer m_HistogramBuffer;
        private readonly int[] m_EmptyHistogram = new int[k_HistogramBins];

#if UNITY_EDITOR
        private static ExposureDebugSettings m_DebugSettings;
        private static RTHandle m_DebugExposureData;
#endif
        public static ComputeBuffer GetHistogramBuffer()
        {
            return m_HistogramBuffer;
        }

        class DynamicExposureData
        {
            public ComputeShader exposureCS;
            public ComputeShader histogramExposureCS;
            public int exposurePreparationKernel;
            public int exposureReductionKernel;

            public Texture textureMeteringMask;
            public Texture exposureCurve;
            
            public Camera camera;
            public Vector2Int viewportSize;
            
            public ComputeBuffer histogramBuffer;

            public Exposure.ExposureMode exposureMode;
            public bool histogramUsesCurve;
            public bool histogramOutputDebugData;
            
            public int[] exposureVariants;
            public Vector4 exposureParams;
            public Vector4 exposureParams2;
            public Vector4 proceduralMaskParams;
            public Vector4 proceduralMaskParams2;
            public Vector4 histogramExposureParams;
            public Vector4 adaptationParams;
            
            
            public RTHandle source;
            public RTHandle prevExposure;
            public RTHandle nextExposure;
            public RTHandle exposureDebugData;
            public RTHandle tmpTarget1024;
            public RTHandle tmpTarget32;
        }
        
        DynamicExposureData m_DynamicExposureData;
        
        public override void Setup()
        {
            m_DynamicExposureData = new();
            
        }

#if UNITY_EDITOR
        public static void SetDebugSetting(ExposureDebugSettings debugSettings, RTHandle debugExposureData)
        {
            m_DebugSettings = debugSettings;
            m_DebugExposureData = debugExposureData;
        }
#endif

        void PrepareExposurePassData(DynamicExposureData passData, Camera camera)
        {
            var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<ExposureResources>();
            passData.exposureCS = runtimeResources.exposureCS;
            passData.histogramExposureCS = runtimeResources.HistogramExposureCS;
            passData.histogramExposureCS.shaderKeywords = null;
            
            passData.camera = camera;
            passData.viewportSize = new Vector2Int(camera.pixelWidth, camera.pixelHeight);

            // Setup variants
            var adaptationMode = settings.adaptationMode.value;
            
            if (IsResetHistoryEnabled())
            {
                adaptationMode = Exposure.AdaptationMode.Fixed;
            }
            
            passData.exposureVariants = m_ExposureVariants;
            passData.exposureVariants[0] = 1; // (int)exposureSettings.luminanceSource.value;
            passData.exposureVariants[1] = (int)settings.meteringMode.value;
            passData.exposureVariants[2] = (int)adaptationMode;
            passData.exposureVariants[3] = 0;

            bool useTextureMask = settings.meteringMode.value == Exposure.MeteringMode.MaskWeighted && settings.weightTextureMask.value != null;
            passData.textureMeteringMask = useTextureMask ? settings.weightTextureMask.value : Texture2D.whiteTexture;

            settings.ComputeProceduralMeteringParams(camera, out passData.proceduralMaskParams, out passData.proceduralMaskParams2);

            bool isHistogramBased = settings.mode.value == Exposure.ExposureMode.AutomaticHistogram;
            // bool needsCurve = (isHistogramBased && settings.histogramUseCurveRemapping.value) || settings.mode.value == Exposure.ExposureMode.CurveMapping;
            bool needsCurve = (isHistogramBased && settings.histogramUseCurveRemapping.value);

            passData.histogramUsesCurve = settings.histogramUseCurveRemapping.value;

            // When recording with accumulation, unity_DeltaTime is adjusted to account for the subframes.
            // To match the ganeview's exposure adaptation when recording, we adjust similarly the speed.
            // float speedMultiplier = m_SubFrameManager.isRecording ? (float) m_SubFrameManager.subFrameCount : 1.0f;
            float speedMultiplier = 1.0f;
            passData.adaptationParams = new Vector4(settings.adaptationSpeedLightToDark.value * speedMultiplier, settings.adaptationSpeedDarkToLight.value * speedMultiplier, 0.0f, 0.0f);

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
            passData.exposureParams = new Vector4(settings.compensation.value + m_DebugExposureCompensation, limitMin, limitMax, 0f);
            passData.exposureParams2 = new Vector4(curveMin, curveMax, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);

            passData.exposureCurve = m_ExposureCurveTexture;
            
            if (isHistogramBased)
            {
                PostProcessingUtils.ValidateComputeBuffer(ref m_HistogramBuffer, k_HistogramBins, sizeof(uint));
                m_HistogramBuffer.SetData(m_EmptyHistogram);    // Clear the histogram
                
                Vector2 histogramFraction = settings.histogramPercentages.value / 100.0f;
                float evRange = limitMax - limitMin;
                float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
                float histBias = -limitMin * histScale;
                passData.histogramExposureParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
                
                passData.histogramBuffer = m_HistogramBuffer;
                passData.histogramOutputDebugData = false;
#if UNITY_EDITOR
                if (m_DebugSettings != null)
                {
                    passData.histogramOutputDebugData = m_DebugSettings.exposureDebugMode == Exposure.ExposureDebugMode.HistogramView;
                }
#endif
                if (passData.histogramOutputDebugData)
                {
                    passData.histogramExposureCS.EnableKeyword("OUTPUT_DEBUG_DATA");
                }
                
                passData.exposurePreparationKernel = passData.histogramExposureCS.FindKernel("KHistogramGen");
                passData.exposureReductionKernel = passData.histogramExposureCS.FindKernel("KHistogramReduce");
            } 
            else
            {
                passData.exposurePreparationKernel = passData.exposureCS.FindKernel("KPrePass");
                passData.exposureReductionKernel = passData.exposureCS.FindKernel("KReduction");
            }

            postProcessData.GrabExposureRequiredTextures(out var prevExposure, out var nextExposure);
            passData.prevExposure = prevExposure;
            passData.nextExposure = nextExposure;
        }
        
        public bool IsResetHistoryEnabled()
        {
            return false;
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            PrepareExposurePassData(m_DynamicExposureData, renderingData.cameraData.camera);
            
            if (settings.mode.value == Exposure.ExposureMode.Fixed)
            {
                DoFixedExposure(cmd, m_DynamicExposureData);
            }
            else
            {
                m_DynamicExposureData.source = source;
                DoHistogramBasedExposure(cmd, m_DynamicExposureData);
            }
            
            cmd.SetGlobalTexture("_AutoExposureLUT", m_DynamicExposureData.nextExposure);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture("_AutoExposureLUT", Texture2D.redTexture);
        }

        private void PrepareExposureCurveData(out float min, out float max)
        {
            var curve = settings.curveMap.value;
            var minCurve = settings.limitMinCurveMap.value;
            var maxCurve = settings.limitMaxCurveMap.value;

            if (m_ExposureCurveTexture == null)
            {
                m_ExposureCurveTexture = new Texture2D(k_ExposureCurvePrecision, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
                {
                    name = "Exposure Curve",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            bool minCurveHasPoints = minCurve.length > 0;
            bool maxCurveHasPoints = maxCurve.length > 0;
            float defaultMin = -100.0f;
            float defaultMax = 100.0f;

            var pixels = m_ExposureCurveColorArray;

            // Fail safe in case the curve is deleted / has 0 point
            if (curve == null || curve.length == 0)
            {
                min = 0f;
                max = 0f;

                for (int i = 0; i < k_ExposureCurvePrecision; i++)
                    pixels[i] = Color.clear;
            }
            else
            {
                min = curve[0].time;
                max = curve[curve.length - 1].time;
                float step = (max - min) / (k_ExposureCurvePrecision - 1f);

                for (int i = 0; i < k_ExposureCurvePrecision; i++)
                {
                    float currTime = min + step * i;
                    pixels[i] = new Color(curve.Evaluate(currTime),
                        minCurveHasPoints ? minCurve.Evaluate(currTime) : defaultMin,
                        maxCurveHasPoints ? maxCurve.Evaluate(currTime) : defaultMax,
                        0f);
                }
            }

            m_ExposureCurveTexture.SetPixels(pixels);
            m_ExposureCurveTexture.Apply();
        }

        void DoFixedExposure(CommandBuffer cmd, DynamicExposureData exposureData)
        {
            ComputeShader cs = exposureData.exposureCS;
            int kernel = 0;
            float m_DebugExposureCompensation = 0;
            Vector4 exposureParams;
            Vector4 exposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);
            
            // if (settings.mode.value == Exposure.ExposureMode.Fixed)
            {
                kernel = cs.FindKernel("KFixedExposure");
                exposureParams = new Vector4(settings.compensation.value + m_DebugExposureCompensation, settings.fixedExposure.value, 0f, 0f);
            }
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, exposureParams2);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, exposureData.nextExposure);
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }

        public static int DivRoundUp(int x, int y) => (x + y - 1) / y;
        
        static void DoHistogramBasedExposure(CommandBuffer cmd, DynamicExposureData data)
        {
#if UNITY_EDITOR
            data.exposureDebugData = m_DebugExposureData;
#endif
            
            var cs = data.histogramExposureCS;
            int kernel;
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams, data.proceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams2, data.proceduralMaskParams2);

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._HistogramExposureParams, data.histogramExposureParams);

            // Generate histogram.
            kernel = data.exposurePreparationKernel;
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, data.source);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureWeightMask, data.textureMeteringMask);

            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, data.exposureVariants);

            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.histogramBuffer);
            
            int threadGroupSizeX = 16;
            int threadGroupSizeY = 8;
            int dispatchSizeX = DivRoundUp(data.viewportSize.x / 2, threadGroupSizeX);
            int dispatchSizeY = DivRoundUp(data.viewportSize.y / 2, threadGroupSizeY);
            
            cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);
            
            // Now read the histogram
            kernel = data.exposureReductionKernel;
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, data.exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, data.exposureParams2);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._AdaptationParams, data.adaptationParams);
            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.histogramBuffer);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, data.nextExposure);
            
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureCurveTexture, data.exposureCurve);
            data.exposureVariants[3] = 0;
            if (data.histogramUsesCurve)
            {
                data.exposureVariants[3] = 2;
            }
            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants,  data.exposureVariants);

            if (data.histogramOutputDebugData)
            {
                cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureDebugTexture, data.exposureDebugData);
            }
            
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }


        public override void Dispose(bool disposing)
        {
            CoreUtils.SafeRelease(m_HistogramBuffer);
            m_HistogramBuffer = null;
        }
    }
}