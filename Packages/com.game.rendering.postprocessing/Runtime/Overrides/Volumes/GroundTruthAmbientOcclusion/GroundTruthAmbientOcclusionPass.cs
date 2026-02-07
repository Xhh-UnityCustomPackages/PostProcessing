using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [PostProcess("GroundTruthAmbientOcclusion", PostProcessInjectionPoint.BeforeRenderingDeferredLights | PostProcessInjectionPoint.BeforeRenderingOpaques)]
    public partial class GroundTruthAmbientOcclusionRenderer : PostProcessVolumeRenderer<GroundTruthAmbientOcclusion>
    {
        private ComputeShader m_GTAOCS;
        private ComputeShader m_GTAOSpatialDenoiseCS;
        private ComputeShader m_GTAOTemporalDenoiseCS;
        private ComputeShader m_GTAOCopyHistoryCS;
        private ComputeShader m_GTAOBlurAndUpsample;
        
        private ProfilingSampler m_SamplerHorizonSSAO = new ProfilingSampler("HorizonSSAO");
        private ProfilingSampler m_SamplerUpSampleSSAO = new ProfilingSampler("UpSampleSSAO");
        
        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Normal;

        protected override void Setup()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<GroundTruthAmbientOcclusionResources>();
            m_GTAOCS = runtimeShaders.GTAOCS;
            m_GTAOSpatialDenoiseCS = runtimeShaders.GTAOSpatialDenoiseCS;
            m_GTAOTemporalDenoiseCS = runtimeShaders.GTAOTemporalDenoiseCS;
            m_GTAOCopyHistoryCS = runtimeShaders.GTAOCopyHistoryCS;
            m_GTAOBlurAndUpsample = runtimeShaders.GTAOBlurAndUpsample;
        }

        public override void Dispose(bool disposing)
        {
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
        }

        
        struct RenderAOParameters
        {
            public Vector2 runningRes;
            public int viewCount;
            public bool fullResolution;
            public bool runAsync;
            public bool temporalAccumulation;
            public bool bilateralUpsample;

            public ShaderVariablesAmbientOcclusion cb;
        }

        RenderAOParameters PrepareRenderAOParameters(PostProcessData camera, Vector2 historySize, in PackedMipChainInfo depthMipInfo)
        {
            var parameters = new RenderAOParameters();

            ref var cb = ref parameters.cb;
            parameters.fullResolution = settings.fullResolution;

            if (parameters.fullResolution)
            {
                parameters.runningRes = new Vector2(camera.actualWidth, camera.actualHeight);
                cb._AOBufferSize = new Vector4(camera.actualWidth, camera.actualHeight, 1.0f / camera.actualWidth, 1.0f / camera.actualHeight);
            }
            else
            {
                // Ceil is needed because we upsample the AO too, round would loose a pixel is the resolution is odd
                parameters.runningRes = new Vector2(Mathf.CeilToInt(camera.actualWidth * 0.5f), Mathf.CeilToInt(camera.actualHeight * 0.5f));
                cb._AOBufferSize = new Vector4(parameters.runningRes.x, parameters.runningRes.y, 1.0f / parameters.runningRes.x, 1.0f / parameters.runningRes.y);
            }
            
            parameters.temporalAccumulation = settings.temporalAccumulation.value /*&& camera.frameSettings.IsEnabled(FrameSettingsField.MotionVectors)*/;

            parameters.viewCount = 1;
            parameters.runAsync = camera.EnableAsyncCompute;
            float invHalfTanFOV = -camera.mainViewConstants.projMatrix[1, 1];
            float aspectRatio = parameters.runningRes.y / parameters.runningRes.x;
            uint frameCount = camera.FrameCount;
            
            cb._AOParams0 = new Vector4(
                parameters.fullResolution ? 0.0f : 1.0f,
                parameters.runningRes.y * invHalfTanFOV * 0.25f,
                settings.radius.value,
                settings.stepCount
            );

            cb._AOParams1 = new Vector4(
                settings.intensity.value,
                1.0f / (settings.radius.value * settings.radius.value),
                (frameCount / 6) % 4,
                (frameCount % 6)
            );


            // We start from screen space position, so we bake in this factor the 1 / resolution as well.
            cb._AODepthToViewParams = new Vector4(
                2.0f / (invHalfTanFOV * aspectRatio * parameters.runningRes.x),
                2.0f / (invHalfTanFOV * parameters.runningRes.y),
                1.0f / (invHalfTanFOV * aspectRatio),
                1.0f / invHalfTanFOV
            );

            float scaleFactor = (parameters.runningRes.x * parameters.runningRes.y) / (540.0f * 960.0f);
            float radInPixels = Mathf.Max(16, settings.maximumRadiusInPixels * Mathf.Sqrt(scaleFactor));
            
            cb._AOParams2 = new Vector4(
                historySize.x,
                historySize.y,
                1.0f / (settings.stepCount + 1.0f),
                radInPixels
            );

            float stepSize = settings.fullResolution ? 1 : 0.5f;

            float blurTolerance = 1.0f - settings.blurSharpness.value;
            float maxBlurTolerance = 0.25f;
            float minBlurTolerance = -2.5f;
            blurTolerance = minBlurTolerance + (blurTolerance * (maxBlurTolerance - minBlurTolerance));

            float bTolerance = 1f - Mathf.Pow(10f, blurTolerance) * stepSize;
            bTolerance *= bTolerance;
            const float upsampleTolerance = -7.0f; // TODO: Expose?
            float uTolerance = Mathf.Pow(10f, upsampleTolerance);
            float noiseFilterWeight = 1f / (Mathf.Pow(10f, 0.0f) + uTolerance);

            cb._AOParams3 = new Vector4(
                bTolerance,
                uTolerance,
                noiseFilterWeight,
                stepSize
            );
            
            float upperNudgeFactor = 1.0f - settings.ghostingReduction.value;
            const float maxUpperNudgeLimit = 5.0f;
            const float minUpperNudgeLimit = 0.25f;
            upperNudgeFactor = minUpperNudgeLimit + (upperNudgeFactor * (maxUpperNudgeLimit - minUpperNudgeLimit));
            cb._AOParams4 = new Vector4(
                settings.directionCount,
                upperNudgeFactor,
                minUpperNudgeLimit,
                settings.spatialBilateralAggressiveness.value * 15.0f
            );

            cb._FirstTwoDepthMipOffsets = new Vector4(depthMipInfo.mipLevelOffsets[1].x, depthMipInfo.mipLevelOffsets[1].y, depthMipInfo.mipLevelOffsets[2].x, depthMipInfo.mipLevelOffsets[2].y);

            parameters.bilateralUpsample = settings.bilateralUpsample;
            parameters.temporalAccumulation = settings.temporalAccumulation.value /*&& camera.frameSettings.IsEnabled(FrameSettingsField.MotionVectors)*/;

            parameters.viewCount = 1;
            parameters.runAsync = camera.EnableAsyncCompute;

            return parameters;
        }

        public static class ShaderIDs
        {
            public static readonly int _ShaderVariablesAmbientOcclusion = Shader.PropertyToID("ShaderVariablesAmbientOcclusion");
            public static readonly int _OcclusionTexture = Shader.PropertyToID("_OcclusionTexture");
            public static readonly int _AOPackedData = Shader.PropertyToID("_AOPackedData");
            public static readonly int _AOPackedHistory = Shader.PropertyToID("_AOPackedHistory");
            public static readonly int _AOPackedBlurred = Shader.PropertyToID("_AOPackedBlurred");
            public static readonly int _AOOutputHistory = Shader.PropertyToID("_AOOutputHistory");
            public static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");
            public static readonly int _OutputTexture = Shader.PropertyToID("_OutputTexture");
        }
    }
}