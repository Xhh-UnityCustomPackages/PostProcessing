using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

        public enum EyeAdaptation
        {
            Progressive,
            Fixed
        }

        [Serializable]
        public sealed class EyeAdaptationParameter : VolumeParameter<EyeAdaptation> { }


        [InspectorName("Filtering (%)"), Tooltip("Filters the bright & dark part of the histogram when computing the average luminance to avoid very dark pixels & very bright pixels from contributing to the auto exposure. Unit is in percent.")]
        public Vector2Parameter filtering = new Vector2Parameter(new Vector2(50f, 95f));//MinMax(1f, 99f), 

        [Range(LogHistogram.rangeMin, LogHistogram.rangeMax), InspectorName("Minimum (EV)"), Tooltip("Minimum average luminance to consider for auto exposure (in EV).")]
        public ClampedFloatParameter minLuminance = new ClampedFloatParameter(0f, LogHistogram.rangeMin, LogHistogram.rangeMax);

        [InspectorName("Maximum (EV)"), Tooltip("Maximum average luminance to consider for auto exposure (in EV).")]
        public ClampedFloatParameter maxLuminance = new ClampedFloatParameter(0f, LogHistogram.rangeMin, LogHistogram.rangeMax);


        [InspectorName("Exposure Compensation"), Tooltip("Use this to scale the global exposure of the scene.")]
        public MinFloatParameter keyValue = new MinFloatParameter(1f, 0f);

        [InspectorName("Type"), Tooltip("Use \"Progressive\" if you want auto exposure to be animated. Use \"Fixed\" otherwise.")]
        public EyeAdaptationParameter eyeAdaptation = new EyeAdaptationParameter { value = EyeAdaptation.Progressive };

        [Min(0f), Tooltip("Adaptation speed from a dark to a light environment.")]
        public MinFloatParameter speedUp = new MinFloatParameter(2f, 0f);

        [Min(0f), Tooltip("Adaptation speed from a light to a dark environment.")]
        public MinFloatParameter speedDown = new MinFloatParameter(1f, 0f);

        public override bool IsActive() => true;
    }

    [PostProcess("Exposure", PostProcessInjectionPoint.AfterRenderingPostProcessing)]
    public class ExposureRenderer : PostProcessVolumeRenderer<Exposure>
    {
        static class ShaderConstants
        {
            internal static readonly int HistogramBuffer = Shader.PropertyToID("_HistogramBuffer");
            internal static readonly int Params1 = Shader.PropertyToID("_Params1");
            internal static readonly int Params2 = Shader.PropertyToID("_Params2");
            internal static readonly int ScaleOffsetRes = Shader.PropertyToID("_ScaleOffsetRes");
            internal static readonly int Destination = Shader.PropertyToID("_Destination");
            internal static readonly int Source = Shader.PropertyToID("_Source");
        }

        const int k_NumAutoExposureTextures = 2;
        RTHandle[] m_AutoExposurePool = new RTHandle[k_NumAutoExposureTextures];
        int m_PingPong;
        RTHandle m_ExposureTexture;

        bool m_FirstFrame;

        LogHistogram m_LogHistogram;

        public override void Setup()
        {
            m_LogHistogram = new LogHistogram();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.width = 1;
            desc.height = 1;
            desc.colorFormat = RenderTextureFormat.RFloat;
            desc.enableRandomWrite = true;

            for (int i = 0; i < m_AutoExposurePool.Length; i++)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_AutoExposurePool[i], desc);
            }
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            // Make sure filtering values are correct to avoid apocalyptic consequences
            float lowPercent = settings.filtering.value.x;
            float highPercent = settings.filtering.value.y;
            const float kMinDelta = 1e-2f;
            highPercent = Mathf.Clamp(highPercent, 1f + kMinDelta, 99f);
            lowPercent = Mathf.Clamp(lowPercent, 1f, highPercent - kMinDelta);

            // Clamp min/max adaptation values as well
            float minLum = settings.minLuminance.value;
            float maxLum = settings.maxLuminance.value;
            settings.minLuminance.value = Mathf.Min(minLum, maxLum);
            settings.maxLuminance.value = Mathf.Max(minLum, maxLum);


            // Compute average luminance & auto exposure

            string adaptation = null;

            if (m_FirstFrame || settings.eyeAdaptation.value == Exposure.EyeAdaptation.Fixed)
                adaptation = "KAutoExposureAvgLuminance_fixed";
            else
                adaptation = "KAutoExposureAvgLuminance_progressive";


            var compute = postProcessFeatureData.computeShaders.autoExposureCS;
            int kernel = compute.FindKernel(adaptation);
            cmd.SetComputeBufferParam(compute, kernel, ShaderConstants.HistogramBuffer, m_LogHistogram.data);
            cmd.SetComputeVectorParam(compute, ShaderConstants.Params1, new Vector4(lowPercent * 0.01f, highPercent * 0.01f, PostProcessingUtils.Exp2(settings.minLuminance.value), PostProcessingUtils.Exp2(settings.maxLuminance.value)));
            cmd.SetComputeVectorParam(compute, ShaderConstants.Params2, new Vector4(settings.speedDown.value, settings.speedUp.value, settings.keyValue.value, Time.deltaTime));
            cmd.SetComputeVectorParam(compute, ShaderConstants.ScaleOffsetRes, m_LogHistogram.GetHistogramScaleOffsetRes(ref renderingData));

            RTHandle currentAutoExposure;
            if (m_FirstFrame)
            {
                // We don't want eye adaptation when not in play mode because the GameView isn't
                // animated, thus making it harder to tweak. Just use the final audo exposure value.
                currentAutoExposure = m_AutoExposurePool[0];
                cmd.SetComputeTextureParam(compute, kernel, ShaderConstants.Destination, currentAutoExposure);
                cmd.DispatchCompute(compute, kernel, 1, 1, 1);

                // Copy current exposure to the other pingpong target to avoid adapting from black
                // RuntimeUtilities.CopyTexture(cmd, m_AutoExposurePool[0], m_AutoExposurePool[1]);

                m_FirstFrame = false;
            }
            else
            {
                int pp = m_PingPong;
                var src = m_AutoExposurePool[++pp % 2];
                var dst = m_AutoExposurePool[++pp % 2];

                cmd.SetComputeTextureParam(compute, kernel, ShaderConstants.Source, src);
                cmd.SetComputeTextureParam(compute, kernel, ShaderConstants.Destination, dst);
                cmd.DispatchCompute(compute, kernel, 1, 1, 1);

                m_PingPong = ++pp % 2;
                currentAutoExposure = dst;
            }

        }


    }
}
