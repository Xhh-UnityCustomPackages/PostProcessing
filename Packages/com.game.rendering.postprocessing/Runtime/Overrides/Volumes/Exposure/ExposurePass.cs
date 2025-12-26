using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Game.Core.PostProcessing.GraphicsUtility;

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
    public class ExposureRenderer : PostProcessVolumeRenderer<Exposure>
    {
        internal class ExposureTexturesInfo
        {
            public CameraType ownerCamera;
            public RTHandle current;
            public RTHandle previous;

            public void Clear()
            {
                if (current != null)
                {
                    current.Release();   
                }
                current = null;

                if (previous != null)
                {
                    previous.Release();    
                }
                
                previous = null;
            }
        
            public bool CreateExposureRT(in CameraType cameraDataCameraType, in RenderTextureDescriptor desc)
            {
                string rtname = CoreUtils.GetTextureAutoName(1, 1, k_ExposureGraphicsFormat, TextureDimension.Tex2D, string.Format("Exposure_Main_{0}", cameraDataCameraType), false, 0);
                string rtname2 = CoreUtils.GetTextureAutoName(1, 1, k_ExposureGraphicsFormat, TextureDimension.Tex2D, string.Format("Exposure_Second_{0}", cameraDataCameraType), false, 0);
                var RTHandleSign = RenderingUtils.ReAllocateHandleIfNeeded(ref current, in desc, FilterMode.Point, TextureWrapMode.Clamp, name:rtname);
                var RTHandleSign2 = RenderingUtils.ReAllocateHandleIfNeeded(ref previous, in desc, FilterMode.Point, TextureWrapMode.Clamp, name:rtname2);
                return RTHandleSign & RTHandleSign2;
            }
        
            static GraphicsFormat k_ExposureGraphicsFormat
            {
                get
                {
                    if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3)
                    {
                        return GraphicsFormat.R32G32B32A32_SFloat;
                    }
                    else
                    {
                        return GraphicsFormat.R32G32_SFloat;
                    }
                }
            }
        }
        
        public override bool renderToCamera => false;
        
        // Exposure data
        private const int k_ExposureCurvePrecision = 128;
        private const int k_HistogramBins = 128;   // Important! If this changes, need to change HistogramExposure.compute
        private readonly Color[] m_ExposureCurveColorArray = new Color[k_ExposureCurvePrecision];
        private readonly int[] m_ExposureVariants = new int[4];
        
        private Texture2D m_ExposureCurveTexture;
        RTHandle m_EmptyExposureTexture; // RGHalf
        RTHandle m_DebugExposureData;
        private ComputeBuffer m_HistogramBuffer;
        private ComputeBuffer m_DebugImageHistogramBuffer;
        private readonly int[] m_EmptyHistogram = new int[k_HistogramBins];
        
        private Dictionary<CameraType, ExposureTexturesInfo> m_ExposureInfos = new ();
        private ExposureTexturesInfo m_ExposureTexturesInfo; 
        
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
        private RenderTextureDescriptor m_RenderDescriptor;
        
        public override void Setup()
        {
            m_DynamicExposureData = new();
        }

        private ExposureTexturesInfo GetOrCreateExposureInfoFromCurCamera(in CameraType cameraDataCameraType)
        {
            if (!m_ExposureInfos.ContainsKey(cameraDataCameraType))
            {
                var info = new ExposureTexturesInfo();
                bool isSuccess = info.CreateExposureRT(in cameraDataCameraType, m_RenderDescriptor);
                m_ExposureInfos.Add(cameraDataCameraType, info);
            }

            return m_ExposureInfos[cameraDataCameraType];
        }
        

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.width = 1;
            desc.height = 1;
            desc.colorFormat = RenderTextureFormat.RFloat;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;
            m_RenderDescriptor = desc;

            m_ExposureTexturesInfo = GetOrCreateExposureInfoFromCurCamera(renderingData.cameraData.cameraType);
        }

        void PrepareExposurePassData(DynamicExposureData passData, Camera camera)
        {
            passData.exposureCS = postProcessFeatureData.computeShaders.ExposureCS;
            passData.histogramExposureCS = postProcessFeatureData.computeShaders.HistogramExposureCS;
            passData.histogramExposureCS.shaderKeywords = null;
            
            passData.camera = camera;
            
            // Setup variants
            var adaptationMode = settings.adaptationMode.value;
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
            bool needsCurve = settings.histogramUseCurveRemapping.value;

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
                ValidateComputeBuffer(ref m_HistogramBuffer, k_HistogramBins, sizeof(uint));
                m_HistogramBuffer.SetData(m_EmptyHistogram);    // Clear the histogram
                
                Vector2 histogramFraction = settings.histogramPercentages.value / 100.0f;
                float evRange = limitMax - limitMin;
                float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
                float histBias = -limitMin * histScale;
                passData.histogramExposureParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
                
                passData.histogramBuffer = m_HistogramBuffer;
                passData.histogramOutputDebugData = settings.debugMode.value == Exposure.ExposureDebugMode.HistogramView;
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
            
            GrabExposureRequiredTextures(camera, out var prevExposure, out var nextExposure);
            passData.prevExposure = prevExposure;
            passData.nextExposure = nextExposure;
        }

        void GrabExposureRequiredTextures(Camera camera, out RTHandle prevExposure, out RTHandle nextExposure)
        {
            prevExposure = m_ExposureTexturesInfo.current;
            nextExposure = m_ExposureTexturesInfo.previous;
            if (IsResetHistoryEnabled())
            {
                Debug.Log($"History Reset for camera: {camera.cameraType}");
                prevExposure = m_EmptyExposureTexture; // Use neutral texture
            }

            // Debug.LogError($"Prev:{prevExposure.name}- Next:{nextExposure.name}");
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
                DoHistogramBasedExposure(cmd, m_DynamicExposureData, source, destination, ref renderingData);
            }
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
            
            cmd.SetGlobalTexture("_AutoExposureLUT", exposureData.nextExposure);
        }

        public static int DivRoundUp(int x, int y) => (x + y - 1) / y;
        
        void DoHistogramBasedExposure(CommandBuffer cmd, DynamicExposureData data, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            var cs = data.histogramExposureCS;
            int kernel;
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams, data.proceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams2, data.proceduralMaskParams2);

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._HistogramExposureParams, data.histogramExposureParams);

            // Generate histogram.
            kernel = data.exposurePreparationKernel;
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, data.prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, source);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureWeightMask, data.textureMeteringMask);

            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, data.exposureVariants);

            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, data.histogramBuffer);
            
            int threadGroupSizeX = 16;
            int threadGroupSizeY = 8;
            int width = renderingData.cameraData.camera.pixelWidth;
            int height = renderingData.cameraData.camera.pixelHeight;
            int dispatchSizeX = DivRoundUp(width / 2, threadGroupSizeX);
            int dispatchSizeY = DivRoundUp(height / 2, threadGroupSizeY);
            
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
            
            cmd.SetGlobalTexture("_AutoExposureLUT", data.nextExposure);
        }


        public override void Dispose(bool disposing)
        {
            foreach (var exposureInfo in m_ExposureInfos.Values)
            {
                exposureInfo.Clear();
            }
        }
    }
}