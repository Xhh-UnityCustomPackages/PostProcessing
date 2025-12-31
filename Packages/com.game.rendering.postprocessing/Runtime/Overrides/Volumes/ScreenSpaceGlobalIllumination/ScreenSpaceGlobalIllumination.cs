using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Game.Core.PostProcessing
{
    // https://www.shadertoy.com/view/fddfDX
    [Serializable, VolumeComponentMenu("Post-processing Custom/屏幕空间全局光照 (Screen Space Global Illumination)")]
    public class ScreenSpaceGlobalIllumination : VolumeSetting
    {
        public ScreenSpaceGlobalIllumination()
        {
            displayName = "屏幕空间全局光照 (Screen Space Global Illumination)";
        }

        /// <summary>
        /// This defines the order in which the fall backs are used if a screen space global illumination ray misses.
        /// </summary>
        public enum RayMarchingFallbackHierarchy
        {
            /// <summary>
            /// When selected, ray tracing will fall back on reflection probes (if any) then on the sky.
            /// </summary>
            [InspectorName("Reflection Probes and Sky")]
            ReflectionProbesAndSky = 0x03,

            /// <summary>
            /// When selected, ray tracing will fall back on reflection probes (if any).
            /// </summary>
            [InspectorName("Reflection Probes")] 
            ReflectionProbes = 0x02,

            /// <summary>
            /// When selected, ray tracing will fall back on the sky.
            /// </summary>
            [InspectorName("Sky")] 
            Sky = 0x01,

            /// <summary>
            /// When selected, ray tracing will return a black color.
            /// </summary>
            [InspectorName("None")] 
            None = 0x00,
        }

        /// <summary>
        /// Enable or disable Screen Space Global Illumination.
        /// </summary>
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Compute and reproject at half resolution.
        /// </summary>
        [Tooltip("Compute and reproject at half resolution for better performance.")]
        public BoolParameter halfResolution = new(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// The thickness of the depth buffer value used for the ray marching step
        /// </summary>
        [Tooltip("Controls the thickness of the depth buffer used for ray marching.")]
        public ClampedFloatParameter depthBufferThickness = new(0.1f, 0.0f, 0.5f);

        /// <summary>
        /// Maximum number of ray marching steps.
        /// </summary>
        [Header("Ray Marching")] [Tooltip("Maximum number of ray marching steps. Higher values improve quality but reduce performance.")]
        public ClampedIntParameter maxRaySteps = new(64, 1, 256);

        /// <summary>
        /// Fallback mode when ray misses geometry.
        /// </summary>
        [Tooltip("Fallback mode when ray misses geometry.")]
        public EnumParameter<RayMarchingFallbackHierarchy> rayMiss = new(RayMarchingFallbackHierarchy.ReflectionProbesAndSky);

        [Tooltip("When enabled, probe volumes will be sampled when ray misses geometry.")]
        public BoolParameter enableProbeVolumes = new(true, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Enable denoising for SSGI.
        /// </summary>
        [Header("Denoising")] [Tooltip("Enable temporal and spatial denoising for smoother results.")]
        public BoolParameter denoise = new(true, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Denoiser radius for spatial filter.
        /// </summary>
        [Tooltip("Controls the radius of the GI denoiser (First Pass).")]
        public ClampedFloatParameter denoiserRadius = new(0.6f, 0.001f, 10.0f);

        /// <summary>
        /// Defines if the second denoising pass should be enabled.
        /// </summary>
        [Tooltip("Enable second denoising pass.")]
        public BoolParameter secondDenoiserPass = new(true, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Use half resolution denoising for better performance.
        /// </summary>
        [Tooltip("Apply the bilateral filter at half resolution and upsample. Improves performance.")]
        public BoolParameter halfResolutionDenoiser = new(false, BoolParameter.DisplayType.EnumPopup);

        public override bool IsActive() => true;
    }


    [PostProcess("Screen Space Global Illumination", PostProcessInjectionPoint.AfterRenderingSkybox, SupportRenderPath.Deferred)]
    public class ScreenSpaceGlobalIlluminationRenderer : PostProcessVolumeRenderer<ScreenSpaceGlobalIllumination>
    {
        private static class Properties
        {
            public static readonly int ShaderVariablesSSGI = Shader.PropertyToID("UnityScreenSpaceGlobalIllumination");
            public static readonly int IndirectDiffuseHitPointTextureRW = Shader.PropertyToID("_IndirectDiffuseHitPointTextureRW");
            public static readonly int IndirectDiffuseHitPointTexture = Shader.PropertyToID("_IndirectDiffuseHitPointTexture");
            public static readonly int IndirectDiffuseTextureRW = Shader.PropertyToID("_IndirectDiffuseTextureRW");
            public static readonly int IndirectDiffuseTexture = Shader.PropertyToID("_IndirectDiffuseTexture");
            public static readonly int HistoryDepthTexture = Shader.PropertyToID("_HistoryDepthTexture");

            // Upsample shader properties
            public static readonly int ShaderVariablesBilateralUpsample = Shader.PropertyToID("ShaderVariablesBilateralUpsample");
            public static readonly int LowResolutionTexture = Shader.PropertyToID("_LowResolutionTexture");
            public static readonly int HalfScreenSize = Shader.PropertyToID("_HalfScreenSize");
            public static readonly int OutputUpscaledTexture = Shader.PropertyToID("_OutputUpscaledTexture");

            // Bilateral denoiser shader properties
            public static readonly int PointDistributionRW = Shader.PropertyToID("_PointDistributionRW");
            public static readonly int PointDistribution = Shader.PropertyToID("_PointDistribution");
            public static readonly int DenoiseInputTexture = Shader.PropertyToID("_DenoiseInputTexture");
            public static readonly int DenoiseOutputTextureRW = Shader.PropertyToID("_DenoiseOutputTextureRW");
            public static readonly int DenoiserFilterRadius = Shader.PropertyToID("_DenoiserFilterRadius");
            public static readonly int PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");
            public static readonly int HalfResolutionFilter = Shader.PropertyToID("_HalfResolutionFilter");
            public static readonly int JitterFramePeriod = Shader.PropertyToID("_JitterFramePeriod");
            public static readonly int DepthTexture = Shader.PropertyToID("_DepthTexture");
            public static readonly int NormalBufferTexture = Shader.PropertyToID("_NormalBufferTexture");

            // Temporal filter shader properties
            public static readonly int ValidationBufferRW = Shader.PropertyToID("_ValidationBufferRW");
            public static readonly int ValidationBuffer = Shader.PropertyToID("_ValidationBuffer");
            public static readonly int HistoryBuffer = Shader.PropertyToID("_HistoryBuffer");
            public static readonly int VelocityBuffer = Shader.PropertyToID("_VelocityBuffer");
            public static readonly int HistoryValidity = Shader.PropertyToID("_HistoryValidity");
            public static readonly int ReceiverMotionRejection = Shader.PropertyToID("_ReceiverMotionRejection");
            public static readonly int OccluderMotionRejection = Shader.PropertyToID("_OccluderMotionRejection");
            public static readonly int DenoiserResolutionMultiplierVals = Shader.PropertyToID("_DenoiserResolutionMultiplierVals");
            public static readonly int EnableExposureControl = Shader.PropertyToID("_EnableExposureControl");
            public static readonly int AccumulationOutputTextureRW = Shader.PropertyToID("_AccumulationOutputTextureRW");
            public static readonly int HistoryNormalTexture = Shader.PropertyToID("_HistoryNormalTexture");
            // public static readonly int ObjectMotionStencilBit = Shader.PropertyToID("_ObjectMotionStencilBit");
            public static readonly int HistorySizeAndScale = Shader.PropertyToID("_HistorySizeAndScale");
            // public static readonly int StencilTexture = Shader.PropertyToID("_StencilTexture");
        
        }

        private ComputeShader _ssgiComputeShader;
        private ComputeShader _diffuseDenoiserCS;
        private ComputeShader _bilateralUpsampleCS;
        private ComputeShader _temporalFilterCS;

        private int _traceKernel;
        private int _traceHalfKernel;
        private int _reprojectKernel;
        private int _reprojectHalfKernel;
        private int _validateHistoryKernel;
        private int _temporalAccumulationColorKernel;
        private int _temporalFilterCopyHistoryKernel;
        private int _generatePointDistributionKernel;
        private int _bilateralFilterColorKernel;
        private int _gatherColorKernel;
        private int _bilateralUpsampleKernel;

        private RTHandle _hitPointRT;
        private RTHandle _outputRT;
        private RTHandle _denoisedRT;
        private RTHandle _temporalRT;
        private RTHandle _temporalRT2;
        private RTHandle _denoisedRT2;
        private RTHandle _upsampledRT;
        private RTHandle _intermediateRT;
        private RTHandle _validationBufferRT;

        private ScreenSpaceGlobalIlluminationVariables _giVariables;
        // private ShaderVariablesBilateralUpsample _upsampleVariables;

        private RenderTextureDescriptor _targetDescriptor;

        private float _rtWidth;
        private float _rtHeight;
        private float _screenWidth;
        private float _screenHeight;
        private bool _halfResolution;
        private float _historyResolutionScale;

        private GraphicsBuffer _pointDistribution;

        private bool _denoiserInitialized;

        private static readonly ProfilingSampler TracingSampler = new("Trace");
        private static readonly ProfilingSampler ReprojectSampler = new("Reproject");
        private static readonly ProfilingSampler DenoiseSampler = new("Denoise");
        private static readonly ProfilingSampler UpsampleSampler = new("Upsample");

        private bool _needDenoise;



        private struct ScreenSpaceGlobalIlluminationVariables
        {
            public int RayMarchingSteps;
            public float RayMarchingThicknessScale;
            public float RayMarchingThicknessBias;
            public int RayMarchingReflectsSky;

            public int RayMarchingFallbackHierarchy;
            public int IndirectDiffuseFrameIndex;
        }


        public override void Setup()
        {
            profilingSampler = new ProfilingSampler("Screen Space Global Illumination");
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceGlobalIlluminationResources>();
            _ssgiComputeShader = runtimeShaders.screenSpaceGlobalIlluminationCS;
            _traceKernel = _ssgiComputeShader.FindKernel("TraceGlobalIllumination");
            _traceHalfKernel = _ssgiComputeShader.FindKernel("TraceGlobalIlluminationHalf");
            _reprojectKernel = _ssgiComputeShader.FindKernel("ReprojectGlobalIllumination");
            _reprojectHalfKernel = _ssgiComputeShader.FindKernel("ReprojectGlobalIlluminationHalf");
            
            _diffuseDenoiserCS =runtimeShaders.diffuseDenoiserCS;
            _generatePointDistributionKernel = _diffuseDenoiserCS.FindKernel("GeneratePointDistribution");
            _bilateralFilterColorKernel = _diffuseDenoiserCS.FindKernel("BilateralFilterColor");
            _gatherColorKernel = _diffuseDenoiserCS.FindKernel("GatherColor");

            _bilateralUpsampleCS = runtimeShaders.bilateralUpsampleCS;
            _bilateralUpsampleKernel = _bilateralUpsampleCS.FindKernel("BilateralUpSampleColor");

            _temporalFilterCS = runtimeShaders.temporalFilterCS;
            _validateHistoryKernel = _temporalFilterCS.FindKernel("ValidateHistory");
            _temporalAccumulationColorKernel = _temporalFilterCS.FindKernel("TemporalAccumulationColor");
            _temporalFilterCopyHistoryKernel = _temporalFilterCS.FindKernel("CopyHistory");

            // Initialize point distribution buffer for denoiser (16 samples * 4 frame periods)
            _pointDistribution = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16 * 4, 2 * sizeof(float));
            _denoiserInitialized = false;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _needDenoise = settings.denoise.value;
            _screenWidth = renderingData.cameraData.cameraTargetDescriptor.width;
            _screenHeight = renderingData.cameraData.cameraTargetDescriptor.height;
            _halfResolution = settings.halfResolution.value;

            int resolutionDivider = _halfResolution ? 2 : 1;
            _rtWidth = _screenWidth / resolutionDivider;
            _rtHeight = _screenHeight / resolutionDivider;

            // Allocate hit point texture
            _targetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            _targetDescriptor.msaaSamples = 1;
            _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            _targetDescriptor.depthBufferBits = 0;
            _targetDescriptor.width = Mathf.CeilToInt(_rtWidth);
            _targetDescriptor.height = Mathf.CeilToInt(_rtHeight);
            _targetDescriptor.enableRandomWrite = true;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _hitPointRT, _targetDescriptor,
                name: "_IndirectDiffuseHitPointTexture", filterMode: FilterMode.Point);

            // Allocate output texture (low res if half resolution, full res otherwise)
            _targetDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _outputRT, _targetDescriptor,
                name: "_IndirectDiffuseTexture", filterMode: FilterMode.Point);

            // Allocate full resolution upsampled texture if half resolution mode
            if (_halfResolution)
            {
                var fullResDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                fullResDescriptor.msaaSamples = 1;
                fullResDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                fullResDescriptor.depthBufferBits = 0;
                fullResDescriptor.enableRandomWrite = true;
                fullResDescriptor.width = Mathf.CeilToInt(_screenWidth);
                fullResDescriptor.height = Mathf.CeilToInt(_screenHeight);
                RenderingUtils.ReAllocateHandleIfNeeded(ref _upsampledRT, fullResDescriptor,
                    name: "_IndirectDiffuseUpsampled", filterMode: FilterMode.Point);
            }

            // Allocate denoising buffers if enabled
            if (settings.denoise.value)
            {
                // Allocate validation buffer for temporal filter
                var validationDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                validationDescriptor.msaaSamples = 1;
                validationDescriptor.graphicsFormat = GraphicsFormat.R8_UInt;
                validationDescriptor.depthBufferBits = 0;
                validationDescriptor.enableRandomWrite = true;
                validationDescriptor.width = Mathf.CeilToInt(_screenWidth);
                validationDescriptor.height = Mathf.CeilToInt(_screenHeight);
                RenderingUtils.ReAllocateHandleIfNeeded(ref _validationBufferRT, validationDescriptor,
                    name: "_SSGIValidationBuffer", filterMode: FilterMode.Point);

                _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _temporalRT, _targetDescriptor,
                    name: "_SSGITemporalOutput", filterMode: FilterMode.Point);
                _targetDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _denoisedRT, _targetDescriptor,
                    name: "_SSGIDenoisedOutput", filterMode: FilterMode.Point);

                // Allocate second pass denoising buffers if enabled
                if (settings.secondDenoiserPass.value)
                {
                    _targetDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    RenderingUtils.ReAllocateHandleIfNeeded(ref _temporalRT2, _targetDescriptor,
                        name: "_SSGITemporalOutput2", filterMode: FilterMode.Point);
                    _targetDescriptor.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                    RenderingUtils.ReAllocateHandleIfNeeded(ref _denoisedRT2, _targetDescriptor,
                        name: "_SSGIDenoisedOutput2", filterMode: FilterMode.Point);
                }

                // Allocate intermediate buffer for half resolution bilateral filter
                if (settings.halfResolutionDenoiser.value)
                {
                    RenderingUtils.ReAllocateIfNeeded(ref _intermediateRT, _targetDescriptor,
                        name: "_DiffuseDenoiserIntermediate", filterMode: FilterMode.Point);
                }

                // Allocate first history buffer
                float scaleFactor = _halfResolution ? 0.5f : 1.0f;
                // if (scaleFactor != _historyResolutionScale ||
                //     _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination) == null)
                // {
                //     _rendererData.ReleaseHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination);
                //     var historyAllocator = new IllusionRendererData.CustomHistoryAllocator(
                //         new Vector2(scaleFactor, scaleFactor),
                //         GraphicsFormat.R16G16B16A16_SFloat,
                //         "IndirectDiffuseHistoryBuffer");
                //     _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination,
                //         historyAllocator.Allocator, 1);
                // }

                // Allocate second history buffer for second denoiser pass
                if (settings.secondDenoiserPass.value)
                {
                    // if (scaleFactor != _historyResolutionScale ||
                    //     _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination2) == null)
                    // {
                    //     _rendererData.ReleaseHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination2);
                    //     var historyAllocator2 = new IllusionRendererData.CustomHistoryAllocator(
                    //         new Vector2(scaleFactor, scaleFactor),
                    //         GraphicsFormat.R16G16B16A16_SFloat,
                    //         "IndirectDiffuseHistoryBuffer2");
                    //     _rendererData.AllocHistoryFrameRT((int)IllusionFrameHistoryType.ScreenSpaceGlobalIllumination2,
                    //         historyAllocator2.Allocator, 1);
                    // }
                }

                _historyResolutionScale = scaleFactor;
            }

            // if (settings.enableProbeVolumes.value && _rendererData.SampleProbeVolumes)
            // {
            //     _ssgiComputeShader.EnableKeyword("_PROBE_VOLUME_ENABLE");
            // }
            // else
            {
                _ssgiComputeShader.DisableKeyword("_PROBE_VOLUME_ENABLE");
            }
        }

        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth
                                                           | ScriptableRenderPassInput.Normal
                                                           | ScriptableRenderPassInput.Motion;

        private void PrepareVariables(ref CameraData cameraData)
        {
            var camera = cameraData.camera;
            var volume = VolumeManager.instance.stack.GetComponent<ScreenSpaceGlobalIllumination>();

            // Calculate thickness parameters
            float thickness = volume.depthBufferThickness.value;
            float n = camera.nearClipPlane;
            float f = camera.farClipPlane;
            float thicknessScale = 1.0f / (1.0f + thickness);
            float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

            // Ray marching parameters
            _giVariables.RayMarchingSteps = volume.maxRaySteps.value;
            _giVariables.RayMarchingThicknessScale = thicknessScale;
            _giVariables.RayMarchingThicknessBias = thicknessBias;
            _giVariables.RayMarchingReflectsSky = 1;

            // Fallback parameters
            _giVariables.RayMarchingFallbackHierarchy = (int)volume.rayMiss.value;

            // Frame index for temporal sampling
            // _giVariables.IndirectDiffuseFrameIndex = (int)(_rendererData.FrameCount % 16);
        }

        private void ExecuteTrace(CommandBuffer cmd, ref CameraData cameraData)
        {
        }

        private void ExecuteReproject(CommandBuffer cmd, ref CameraData cameraData)
        {
        }
        
        private void InitializeDiffuseDenoiser(CommandBuffer cmd)
        {
            // Generate point distribution (only needs to be done once)
            if (!_denoiserInitialized)
            {
                cmd.SetComputeBufferParam(_diffuseDenoiserCS, _generatePointDistributionKernel,
                    Properties.PointDistributionRW, _pointDistribution);
                cmd.DispatchCompute(_diffuseDenoiserCS, _generatePointDistributionKernel, 1, 1, 1);
                _denoiserInitialized = true;
            }
        }

        private static float GetPixelSpreadTangent(float fov, int width, int height)
        {
            // Calculate the pixel spread angle tangent for the current FOV and resolution
            return Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f) / (height * 0.5f);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            
            // Prepare shader variables
            PrepareVariables(ref cameraData);

            using (new ProfilingScope(cmd, profilingSampler))
            {
                using (new ProfilingScope(cmd, TracingSampler))
                {
                    ExecuteTrace(cmd, ref cameraData);
                }

                using (new ProfilingScope(cmd, ReprojectSampler))
                {
                    ExecuteReproject(cmd, ref cameraData);
                }
            }

        }

        public override void Dispose(bool disposing)
        {
            _hitPointRT?.Release();
            _outputRT?.Release();
            _denoisedRT?.Release();
            _temporalRT?.Release();
            _temporalRT2?.Release();
            _denoisedRT2?.Release();
            _upsampledRT?.Release();
            _intermediateRT?.Release();
            _validationBufferRT?.Release();
            _pointDistribution?.Release();
        }
    }
}