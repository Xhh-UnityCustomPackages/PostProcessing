using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/曝光 (Exposure)")]
    public class Exposure : VolumeSetting
    {
        public Exposure()
        {
            displayName = "曝光 (Exposure)";
        }

        public enum ExposureMode
        {
            /// <summary>
            /// Allows you to manually sets the Scene exposure.
            /// </summary>
            Fixed = 0,
    
            /// <summary>
            /// Automatically sets the exposure depending on what is on screen.
            /// </summary>
            // Automatic = 1,
            AutomaticHistogram = 1
        }

        /// <summary>
        /// Metering methods that URP uses the filter the luminance source
        /// </summary>
        /// <seealso cref="Exposure.meteringMode"/>
        public enum MeteringMode
        {
            /// <summary>
            /// The Camera uses the entire luminance buffer to measure exposure.
            /// </summary>
            Average,

            /// <summary>
            /// The Camera only uses the center of the buffer to measure exposure. This is useful if you
            /// want to only expose light against what is in the center of your screen.
            /// </summary>
            Spot,

            /// <summary>
            /// The Camera applies a weight to every pixel in the buffer and then uses them to measure
            /// the exposure. Pixels in the center have the maximum weight, pixels at the screen borders
            /// have the minimum weight, and pixels in between have a progressively lower weight the
            /// closer they are to the screen borders.
            /// </summary>
            CenterWeighted,

            /// <summary>
            /// The Camera applies a weight to every pixel in the buffer and then uses them to measure
            /// the exposure. The weighting is specified by the texture provided by the user. Note that if
            /// no texture is provided, then this metering mode is equivalent to Average.
            /// </summary>
            MaskWeighted,

            /// <summary>
            /// Create a weight mask centered around the specified UV and with the desired parameters.
            /// </summary>
            ProceduralMask,
        }

        /// <summary>
        /// Methods that URP uses to change the exposure when the Camera moves from dark to light and vice versa.
        /// </summary>
        /// <seealso cref="Exposure.adaptationMode"/>
        public enum AdaptationMode
        {
            /// <summary>
            /// The exposure changes instantly.
            /// </summary>
            Fixed,

            /// <summary>
            /// The exposure changes over the period of time.
            /// </summary>
            /// <seealso cref="Exposure.adaptationSpeedDarkToLight"/>
            /// <seealso cref="Exposure.adaptationSpeedLightToDark"/>
            Progressive
        }
        
        /// <summary>
        /// The target grey value used by the exposure system. Note this is equivalent of changing the calibration constant K on the used virtual reflected light meter.
        /// </summary>
        public enum TargetMidGray
        {
            /// <summary>
            /// Mid Grey 12.5% (reflected light meter K set as 12.5)
            /// </summary>
            Grey125,

            /// <summary>
            /// Mid Grey 14.0% (reflected light meter K set as 14.0)
            /// </summary>
            Grey14,

            /// <summary>
            /// Mid Grey 18.0% (reflected light meter K set as 18.0). Note that this value is outside of the suggested K range by the ISO standard.
            /// </summary>
            Grey18
        }
        
        public enum ExposureDebugMode
        {
            /// <summary>
            /// No exposure debug.
            /// </summary>
            None,

            /// <summary>
            /// Display the EV100 values of the scene, color-coded.
            /// </summary>
            SceneEV100Values,

            /// <summary>
            /// Display the Histogram used for exposure.
            /// </summary>
            HistogramView,

            /// <summary>
            /// Display an RGB histogram of the final image (after post-processing).
            /// </summary>
            FinalImageHistogramView,

            /// <summary>
            /// Visualize the scene color weighted as the metering mode selected.
            /// </summary>
            MeteringWeighted,
        }

        [Tooltip("Specifies the method that URP uses to process exposure.")]
        public EnumParameter<ExposureMode> mode = new(ExposureMode.Fixed);

        /// <summary>
        /// Specifies the metering method that URP uses the filter the luminance source.
        /// </summary>
        /// <seealso cref="MeteringMode"/>
        [Tooltip("Specifies the metering method that URP uses the filter the luminance source.")]
        public EnumParameter<MeteringMode> meteringMode = new(MeteringMode.CenterWeighted);

        // /// <summary>
        // /// Specifies the luminance source that URP uses to calculate the current Scene exposure.
        // /// </summary>
        // /// <seealso cref="LuminanceSource"/>
        // [Tooltip("Specifies the luminance source that URP uses to calculate the current Scene exposure.")]
        // public LuminanceSourceParameter luminanceSource = new(LuminanceSource.ColorBuffer);

        /// <summary>
        /// Sets a static exposure value for Cameras in this Volume.
        /// This parameter is only used when <see cref="ExposureMode.Fixed"/> is set.
        /// </summary>
        [Tooltip("Sets a static exposure value for Cameras in this Volume.")]
        public FloatParameter fixedExposure = new(0f);

        /// <summary>
        /// Sets the compensation that the Camera applies to the calculated exposure value.
        /// This parameter is only used when any mode but <see cref="ExposureMode.Fixed"/> is set.
        /// </summary>
        [Tooltip("Sets the compensation that the Camera applies to the calculated exposure value.")]
        public FloatParameter compensation = new(0f);

        /// <summary>
        /// Sets the minimum value that the Scene exposure can be set to.
        /// This parameter is only used when <see cref="ExposureMode.Automatic"/> or <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Tooltip("Sets the minimum value that the Scene exposure can be set to.")]
        public FloatParameter limitMin = new(-1f);

        /// <summary>
        /// Sets the maximum value that the Scene exposure can be set to.
        /// This parameter is only used when <see cref="ExposureMode.Automatic"/> or <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Tooltip("Sets the maximum value that the Scene exposure can be set to.")]
        public FloatParameter limitMax = new(14f);

        /// <summary>
        /// Specifies a curve that remaps the Scene exposure on the x-axis to the exposure you want on the y-axis.
        /// This parameter is only used when <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Tooltip("Specifies a curve that remaps the Scene exposure on the x-axis to the exposure you want on the y-axis.")]
        public AnimationCurveParameter curveMap = new(AnimationCurve.Linear(-10f, -10f, 20f, 20f)); // TODO: Use TextureCurve instead?

        /// <summary>
        /// Specifies a curve that determines for each current exposure value (x-value) what minimum value is allowed to auto-adaptation (y-axis).
        /// This parameter is only used when <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Tooltip("Specifies a curve that determines for each current exposure value (x-value) what minimum value is allowed to auto-adaptation (y-axis).")]
        public AnimationCurveParameter limitMinCurveMap = new(AnimationCurve.Linear(-10f, -12f, 20f, 18f));

        /// <summary>
        /// Specifies a curve that determines for each current exposure value (x-value) what maximum value is allowed to auto-adaptation (y-axis).
        /// This parameter is only used when <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Tooltip("Specifies a curve that determines for each current exposure value (x-value) what maximum value is allowed to auto-adaptation (y-axis).")]
        public AnimationCurveParameter limitMaxCurveMap = new(AnimationCurve.Linear(-10f, -8f, 20f, 22f));

        /// <summary>
        /// Specifies the method that URP uses to change the exposure when the Camera moves from dark to light and vice versa.
        /// This parameter is only used when <see cref="ExposureMode.Automatic"/> or <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Header("Adaptation")]
        [Tooltip("Specifies the method that URP uses to change the exposure when the Camera moves from dark to light and vice versa.")]
        public EnumParameter<AdaptationMode> adaptationMode = new(AdaptationMode.Progressive);

        /// <summary>
        /// Sets the speed at which the exposure changes when the Camera moves from a dark area to a bright area.
        /// This parameter is only used when <see cref="ExposureMode.Automatic"/> or <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Tooltip("Sets the speed at which the exposure changes when the Camera moves from a dark area to a bright area.")]
        public MinFloatParameter adaptationSpeedDarkToLight = new(3f, 0.001f);

        /// <summary>
        /// Sets the speed at which the exposure changes when the Camera moves from a bright area to a dark area.
        /// This parameter is only used when <see cref="ExposureMode.Automatic"/> or <see cref="ExposureMode.CurveMapping"/> is set.
        /// </summary>
        [Tooltip("Sets the speed at which the exposure changes when the Camera moves from a bright area to a dark area.")]
        public MinFloatParameter adaptationSpeedLightToDark = new(1f, 0.001f);

        /// <summary>
        /// Sets the texture mask used to weight the pixels in the buffer when computing exposure.
        /// </summary>
        [Tooltip("Sets the texture mask to be used to weight the pixels in the buffer for the sake of computing exposure.")]
        public Texture2DParameter weightTextureMask = new(null);

        /// <summary>
        /// These values are the lower and upper percentages of the histogram that will be used to
        /// find a stable average luminance. Values outside of this range will be discarded and won't
        /// contribute to the average luminance.
        /// </summary>
        [Header("Histogram")]
        [Tooltip("Sets the range of values (in terms of percentages) of the histogram that are accepted while finding a stable average exposure. Anything outside the value is discarded.")]
        public FloatRangeParameter histogramPercentages = new(new Vector2(40.0f, 90.0f), 0.0f, 100.0f);

        /// <summary>
        /// Sets whether histogram exposure mode will remap the computed exposure with a curve remapping (akin to Curve Remapping mode)
        /// </summary>
        [Tooltip("Sets whether histogram exposure mode will remap the computed exposure with a curve remapping (akin to Curve Remapping mode).")]
        public BoolParameter histogramUseCurveRemapping = new(false);

        /// <summary>
        /// Sets the desired Mid gray level used by the auto exposure (i.e. to what grey value the auto exposure system maps the average scene luminance).
        /// Note that the lens model used in URP is not of a perfect lens, hence it will not map precisely to the selected value.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Sets the desired Mid gray level used by the auto exposure (i.e. to what grey value the auto exposure system maps the average scene luminance).")]
        public EnumParameter<TargetMidGray> targetMidGray = new(TargetMidGray.Grey125);

        // /// <summary>
        // /// Sets whether the procedural metering mask is centered around the exposure target (to be set on the camera)
        // /// </summary>
        // [Tooltip("Sets whether histogram exposure mode will remap the computed exposure with a curve remapping (akin to Curve Remapping mode).")]
        // public BoolParameter centerAroundExposureTarget = new(false);

        /// <summary>
        /// Sets the center of the procedural metering mask ([0,0] being bottom left of the screen and [1,1] top right of the screen)
        /// </summary>
        [Header("Procedural Mask")]
        public NoInterpVector2Parameter proceduralCenter = new(new Vector2(0.5f, 0.5f));
        
        /// <summary>
        /// Sets the radii of the procedural mask, in terms of fraction of half the screen (i.e. 0.5 means a mask that stretch half of the screen in both directions).
        /// </summary>
        public NoInterpVector2Parameter proceduralRadii = new(new Vector2(0.3f, 0.3f));
        
        /// <summary>
        /// All pixels below this threshold (in EV100 units) will be assigned a weight of 0 in the metering mask.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("All pixels below this threshold (in EV100 units) will be assigned a weight of 0 in the metering mask.")]
        public FloatParameter maskMinIntensity = new(-30.0f);
        
        /// <summary>
        /// All pixels above this threshold (in EV100 units) will be assigned a weight of 0 in the metering mask.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("All pixels above this threshold (in EV100 units) will be assigned a weight of 0 in the metering mask.")]
        public FloatParameter maskMaxIntensity = new(30.0f);

        /// <summary>
        /// Sets the softness of the mask, the higher the value the less influence is given to pixels at the edge of the mask.
        /// </summary>
        public NoInterpMinFloatParameter proceduralSoftness = new(0.5f, 0.0f);


        [Header("Debug")] 
        public EnumParameter<ExposureDebugMode> debugMode = new(ExposureDebugMode.None);

        public override bool IsActive() => true;
        
        public void ComputeProceduralMeteringParams(Camera camera, out Vector4 proceduralParams1, out Vector4 proceduralParams2)
        {
            Vector2 proceduralCenter = this.proceduralCenter.value;
            // if (camera.exposureTarget != null && m_Exposure.centerAroundExposureTarget.value)
            // {
            //     var transform = camera.exposureTarget.transform;
            //     // Transform in screen space
            //     Vector3 targetLocation = transform.position;
            //     var ndcLoc = camera.mainViewConstants.viewProjMatrix * (targetLocation);
            //     ndcLoc.x /= ndcLoc.w;
            //     ndcLoc.y /= ndcLoc.w;
            //
            //     Vector2 targetUV = new Vector2(ndcLoc.x, ndcLoc.y) * 0.5f + new Vector2(0.5f, 0.5f);
            //     targetUV.y = 1.0f - targetUV.y;
            //
            //     proceduralCenter += targetUV;
            // }

            proceduralCenter.x = Mathf.Clamp01(proceduralCenter.x);
            proceduralCenter.y = Mathf.Clamp01(proceduralCenter.y);

            var actualWidth = camera.pixelWidth;
            var actualHeight = camera.pixelHeight;
            proceduralCenter.x *= actualWidth;
            proceduralCenter.y *= actualHeight;

            // float screenDiagonal = 0.5f * (actualHeight + actualWidth);

            proceduralParams1 = new Vector4(proceduralCenter.x, proceduralCenter.y,
                proceduralRadii.value.x * actualWidth,
                proceduralRadii.value.y * actualHeight);

            proceduralParams2 = new Vector4(1.0f / proceduralSoftness.value,
                LightUtils.ConvertEvToLuminance(maskMinIntensity.value), 
                LightUtils.ConvertEvToLuminance(maskMaxIntensity.value), 0.0f);
        }
        
        public class LightUtils
        {
            private static float s_LuminanceToEvFactor => Mathf.Log(100f / ColorUtils.s_LightMeterCalibrationConstant, 2);
            private static float s_EvToLuminanceFactor => -Mathf.Log(100f / ColorUtils.s_LightMeterCalibrationConstant, 2);
        
            /// <summary>
            /// Convert EV100 to Luminance(nits)
            /// </summary>
            /// <param name="ev"></param>
            /// <returns></returns>
            public static float ConvertEvToLuminance(float ev)
            {
                return Mathf.Pow(2, ev + s_EvToLuminanceFactor);
            }
        }
    }

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
        public override bool renderToCamera => false;

        const int k_NumAutoExposureTextures = 2;
        RTHandle[] m_AutoExposurePool = new RTHandle[k_NumAutoExposureTextures];
        int m_PingPong;
        bool m_ResetHistory;

        LogHistogram m_LogHistogram;
        private ComputeShader _exposureCS;
        private ComputeShader _histogramExposureCs;
        
        private int _exposurePreparationKernel;
        private int _exposureReductionKernel;
        private const int ExposureCurvePrecision = 128;
        private const int HistogramBins = 128;   // Important! If this changes, need to change HistogramExposure.compute
        private readonly int[] _emptyHistogram = new int[HistogramBins];
        private readonly Color[] _exposureCurveColorArray = new Color[ExposureCurvePrecision];
        private readonly int[] _exposureVariants = new int[4];
        private Texture _textureMeteringMask;
        private Vector4 _proceduralMaskParams;
        private Vector4 _proceduralMaskParams2;
        private bool _histogramUsesCurve;
        private Vector4 _histogramExposureParams;
        private Vector4 _adaptationParams;
        private Texture2D _exposureCurveTexture;
        
        private Vector4 _exposureParams;
        private Vector4 _exposureParams2;
        private Texture _exposureCurve;

        private ComputeBuffer histogramBuffer;
        
        ExposureDebugPass m_DebugPass;


        public override void Setup()
        {
            _exposureCS = postProcessFeatureData.computeShaders.ExposureCS;
            _histogramExposureCs = postProcessFeatureData.computeShaders.HistogramExposureCS;
            _exposurePreparationKernel = _histogramExposureCs.FindKernel("KHistogramGen");
            _exposureReductionKernel = _histogramExposureCs.FindKernel("KHistogramReduce");
            
            m_LogHistogram = new LogHistogram(postProcessFeatureData.computeShaders.HistogramExposureCS);
            m_PingPong = 0;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // Setup variants
            var adaptationMode = settings.adaptationMode.value;
            _exposureVariants[0] = 1; // (int)exposureSettings.luminanceSource.value;
            _exposureVariants[1] = (int)settings.meteringMode.value;
            _exposureVariants[2] = (int)adaptationMode;
            _exposureVariants[3] = 0;
            
            bool useTextureMask = settings.meteringMode.value == Exposure.MeteringMode.MaskWeighted && settings.weightTextureMask.value != null;
            _textureMeteringMask = useTextureMask ? settings.weightTextureMask.value : Texture2D.whiteTexture;
            
            settings.ComputeProceduralMeteringParams(renderingData.cameraData.camera, out _proceduralMaskParams, out _proceduralMaskParams2);
            
            // exposureMode = m_Exposure.mode.value;
            // bool isHistogramBased = m_Exposure.mode.value == ExposureMode.AutomaticHistogram;
            // bool needsCurve = (isHistogramBased && m_Exposure.histogramUseCurveRemapping.value) || m_Exposure.mode.value == ExposureMode.CurveMapping;
            bool needsCurve = settings.histogramUseCurveRemapping.value;
            
            _histogramUsesCurve = settings.histogramUseCurveRemapping.value;
            
            // When recording with accumulation, unity_DeltaTime is adjusted to account for the subframes.
            // To match the ganeview's exposure adaptation when recording, we adjust similarly the speed.
            // float speedMultiplier = m_SubFrameManager.isRecording ? (float) m_SubFrameManager.subFrameCount : 1.0f;
            float speedMultiplier = 1.0f;
            _adaptationParams = new Vector4(settings.adaptationSpeedLightToDark.value * speedMultiplier, 
                settings.adaptationSpeedDarkToLight.value * speedMultiplier, 0.0f, 0.0f);
            
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
            _exposureParams = new Vector4(settings.compensation.value + m_DebugExposureCompensation, limitMin, limitMax, 0f);
            _exposureParams2 = new Vector4(curveMin, curveMax, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);

            _exposureCurve = _exposureCurveTexture;
            
            // if (isHistogramBased)
            {
                
                if (histogramBuffer == null)
                {
                    histogramBuffer = new ComputeBuffer(HistogramBins, sizeof(uint));
                }

                histogramBuffer.SetData(_emptyHistogram);    // Clear the histogram
                //
                Vector2 histogramFraction = settings.histogramPercentages.value / 100.0f;
                float evRange = limitMax - limitMin;
                float histScale = 1.0f / Mathf.Max(1e-5f, evRange);
                float histBias = -limitMin * histScale;
                _histogramExposureParams = new Vector4(histScale, histBias, histogramFraction.x, histogramFraction.y);
                bool _histogramOutputDebugData = settings.debugMode.value == Exposure.ExposureDebugMode.HistogramView;
                if (_histogramOutputDebugData)
                {
                    // _histogramExposureCs.EnableKeyword("OUTPUT_DEBUG_DATA");
                }
            }
            
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.width = 1;
            desc.height = 1;
            desc.colorFormat = RenderTextureFormat.RFloat;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;

            for (int i = 0; i < m_AutoExposurePool.Length; i++)
            {
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_AutoExposurePool[i], desc);
            }
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            if (settings.mode.value == Exposure.ExposureMode.Fixed)
            {
                DoFixedExposure(cmd);
            }
            else
            {
                DoHistogramBasedExposure(cmd, source, destination, ref renderingData);
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

            if (_exposureCurveTexture == null)
            {
                _exposureCurveTexture = new Texture2D(ExposureCurvePrecision, 1, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
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

            var pixels = _exposureCurveColorArray;

            // Fail safe in case the curve is deleted / has 0 point
            if (curve == null || curve.length == 0)
            {
                min = 0f;
                max = 0f;

                for (int i = 0; i < ExposureCurvePrecision; i++)
                    pixels[i] = Color.clear;
            }
            else
            {
                min = curve[0].time;
                max = curve[curve.length - 1].time;
                float step = (max - min) / (ExposureCurvePrecision - 1f);

                for (int i = 0; i < ExposureCurvePrecision; i++)
                {
                    float currTime = min + step * i;
                    pixels[i] = new Color(curve.Evaluate(currTime),
                        minCurveHasPoints ? minCurve.Evaluate(currTime) : defaultMin,
                        maxCurveHasPoints ? maxCurve.Evaluate(currTime) : defaultMax,
                        0f);
                }
            }

            _exposureCurveTexture.SetPixels(pixels);
            _exposureCurveTexture.Apply();
        }

        void DoFixedExposure(CommandBuffer cmd)
        {
            ComputeShader cs = _exposureCS;
            int kernel;
            float m_DebugExposureCompensation = 0;
            Vector4 exposureParams;
            Vector4 exposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);
            
            // if (_automaticExposure.mode.value == ExposureMode.Fixed)
            {
                kernel = cs.FindKernel("KFixedExposure");
                exposureParams = new Vector4(settings.compensation.value + m_DebugExposureCompensation, settings.fixedExposure.value, 0f, 0f);
            }
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, exposureParams2);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, m_AutoExposurePool[0]);
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
            
            cmd.SetGlobalTexture("_AutoExposureLUT", m_AutoExposurePool[0]);
        }

        public static int DivRoundUp(int x, int y) => (x + y - 1) / y;
        
        void DoHistogramBasedExposure(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            var cs = _histogramExposureCs;
            
            int pp = m_PingPong;
            var src = m_AutoExposurePool[++pp % 2];
            var dst = m_AutoExposurePool[++pp % 2];
            m_PingPong = ++pp % 2;

            
            var prevExposure = dst;
            var nextExposure = src;

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams, _proceduralMaskParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ProceduralMaskParams2, _proceduralMaskParams2);

            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._HistogramExposureParams, _histogramExposureParams);

            // Generate histogram.
            var kernel = _exposurePreparationKernel;
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._SourceTexture, source);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureWeightMask, _textureMeteringMask);

            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, _exposureVariants);

            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, histogramBuffer);
            
            int threadGroupSizeX = 16;
            int threadGroupSizeY = 8;
            int width = renderingData.cameraData.camera.pixelWidth;
            int height = renderingData.cameraData.camera.pixelHeight;
            int dispatchSizeX = DivRoundUp(width / 2, threadGroupSizeX);
            int dispatchSizeY = DivRoundUp(height / 2, threadGroupSizeY);
            
            cmd.DispatchCompute(cs, kernel, dispatchSizeX, dispatchSizeY, 1);
            
            // Now read the histogram
            kernel = _exposureReductionKernel;
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, _exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, _exposureParams2);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._AdaptationParams, _adaptationParams);
            cmd.SetComputeBufferParam(cs, kernel, ExposureShaderIDs._HistogramBuffer, histogramBuffer);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._PreviousExposureTexture, prevExposure);
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, nextExposure);
            
            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureCurveTexture, _exposureCurve);
            _exposureVariants[3] = 0;
            if (_histogramUsesCurve)
            {
                _exposureVariants[3] = 2;
            }
            cmd.SetComputeIntParams(cs, ExposureShaderIDs._Variants, _exposureVariants);

            // if (_histogramOutputDebugData)
            // {
            //     var exposureDebugData = _rendererData.GetExposureDebugData();
            //     cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._ExposureDebugTexture, exposureDebugData);
            // }
            
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
            
            cmd.SetGlobalTexture("_AutoExposureLUT", nextExposure);
        }


        public override void Dispose(bool disposing)
        {
            for (int i = 0; i < m_AutoExposurePool.Length; i++)
            {
                m_AutoExposurePool[i]?.Release();
            }
        }

        public override void AddRenderPasses(ref RenderingData renderingData)
        {
            if (settings.debugMode.value != Exposure.ExposureDebugMode.None)
            {
                if (m_DebugPass == null)
                {
                    m_DebugPass = new(postProcessFeatureData);
                }

                renderingData.cameraData.renderer.EnqueuePass(m_DebugPass);
            }
        }
    }
}