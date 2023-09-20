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
        public Vector2Parameter filtering = new Vector2Parameter(new Vector2(10f, 90f));//MinMax(1f, 99f), 

        [Range(LogHistogram.rangeMin, LogHistogram.rangeMax), InspectorName("Minimum (EV)"), Tooltip("Minimum average luminance to consider for auto exposure (in EV).")]
        public ClampedFloatParameter minEV = new ClampedFloatParameter(-10f, LogHistogram.rangeMin, LogHistogram.rangeMax);

        [InspectorName("Maximum (EV)"), Tooltip("Maximum average luminance to consider for auto exposure (in EV).")]
        public ClampedFloatParameter maxEV = new ClampedFloatParameter(10f, LogHistogram.rangeMin, LogHistogram.rangeMax);


        [InspectorName("曝光补偿 (Exposure Compensation)"), Tooltip("Use this to scale the global exposure of the scene.")]
        public MinFloatParameter compensation = new MinFloatParameter(2f, 0f);

        [InspectorName("Type"), Tooltip("Use \"Progressive\" if you want auto exposure to be animated. Use \"Fixed\" otherwise.")]
        public EyeAdaptationParameter eyeAdaptation = new EyeAdaptationParameter { value = EyeAdaptation.Fixed };

        [Min(0f), Tooltip("Adaptation speed from a dark to a light environment.")]
        public MinFloatParameter speedUp = new MinFloatParameter(2f, 0f);

        [Min(0f), Tooltip("Adaptation speed from a light to a dark environment.")]
        public MinFloatParameter speedDown = new MinFloatParameter(1f, 0f);

        public override bool IsActive() => true;
    }

    [PostProcess("Exposure", PostProcessInjectionPoint.BeforeRenderingPostProcessing)]
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

        public override bool renderToCamera => false;

        const int k_NumAutoExposureTextures = 2;
        RTHandle[] m_AutoExposurePool = new RTHandle[k_NumAutoExposureTextures];
        int m_PingPong;
        RTHandle m_ExposureTexture;

        bool m_FirstFrame;
        bool m_ResetHistory;

        LogHistogram m_LogHistogram;
        ComputeShader m_AutoExposureCS;

        public override void Setup()
        {
            m_AutoExposureCS = postProcessFeatureData.computeShaders.autoExposureCS;
            m_LogHistogram = new LogHistogram(postProcessFeatureData.computeShaders.LogHistogramCS);
            m_PingPong = 0;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.width = 1;
            desc.height = 1;
            desc.colorFormat = RenderTextureFormat.RFloat;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;

            for (int i = 0; i < m_AutoExposurePool.Length; i++)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_AutoExposurePool[i], desc);
            }
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {

            DoAutoExposure(cmd, source, destination, ref renderingData);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture("_AutoExposureLUT", Texture2D.redTexture);
        }


        void DoAutoExposure(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;

            // switch (settings.meteringMask)
            // {
            //     case AutoExposureSettings.MeteringMask.None:
            //         buffer.SetGlobalInt("_MeteringMask", 0);
            //         break;
            //     case AutoExposureSettings.MeteringMask.Vignette:
            //         buffer.SetGlobalInt("_MeteringMask", 1);
            //         break;
            //     case AutoExposureSettings.MeteringMask.Custom:
            //         buffer.SetGlobalInt("_MeteringMask", 2);
            //         break;
            //     default:
            //         break;
            // }

            //计算直方图
            m_LogHistogram.Generate(cmd, desc.width, desc.height, source);

            //计算曝光
            cmd.BeginSample("Auto Exposure");
            float lowPercent = settings.filtering.value.x;
            float highPercent = settings.filtering.value.y;
            const float kMinDelta = 1e-2f;
            highPercent = Mathf.Clamp(highPercent, 1f + kMinDelta, 99f);
            lowPercent = Mathf.Clamp(lowPercent, 1f, highPercent - kMinDelta);

            // Clamp min/max adaptation values as well
            float minLum = settings.minEV.value;
            float maxLum = settings.maxEV.value;

            Vector4 exposureParams = new Vector4(lowPercent, highPercent, minLum, maxLum);
            Vector4 adaptationParams = new Vector4(settings.speedDown.value, settings.speedUp.value, settings.compensation.value, Time.deltaTime);
            // Vector4 physcialParams = new Vector4(physcialSettings.fStop, 1f / physcialSettings.shutterSpeed, physcialSettings.ISO, MelodyColorUtils.lensImperfectionExposureScale);
            Vector4 scaleOffsetRes = m_LogHistogram.GetHistogramScaleOffsetRes(desc.width, desc.height);


            bool isFixed = settings.eyeAdaptation.value == Exposure.EyeAdaptation.Fixed ? true : false;
            // CheckTexture(0);
            // CheckTexture(1);
            bool firstFrame = m_ResetHistory || !Application.isPlaying;
            string adaptation = null;
            if (firstFrame || isFixed)
            {
                adaptation = "AutoExposureAvgLuminance_fixed";
            }
            else
            {
                adaptation = "AutoExposureAvgLuminance_progressive";
            }


            int kernel = m_AutoExposureCS.FindKernel(adaptation);
            cmd.SetComputeBufferParam(m_AutoExposureCS, kernel, "_HistogramBuffer", m_LogHistogram.data);
            cmd.SetComputeVectorParam(m_AutoExposureCS, "_Params1", new Vector4(exposureParams.x * 0.01f, exposureParams.y * 0.01f, Mathf.Pow(2, exposureParams.z), Mathf.Pow(2, exposureParams.w)));
            cmd.SetComputeVectorParam(m_AutoExposureCS, "_Params2", adaptationParams);
            // cmd.SetComputeVectorParam(m_AutoExposureCS, "_Params3", physicalParams);
            cmd.SetComputeVectorParam(m_AutoExposureCS, "_ScaleOffsetRes", scaleOffsetRes);

            // if (isPhysical)
            // {
            //     m_AutoExposureCS.EnableKeyword("PHYSCIAL_BASED");
            // }
            // else
            // {
            m_AutoExposureCS.DisableKeyword("PHYSCIAL_BASED");
            // }


            RTHandle currentAutoExposure;
            if (firstFrame)
            {
                //don't want eye adaptation when not in play mode because the GameView isn't animated, thus making it harder to tweak. Just use the final audo exposure value.
                currentAutoExposure = m_AutoExposurePool[0];
                cmd.SetComputeTextureParam(m_AutoExposureCS, kernel, "_DestinationTex", currentAutoExposure);
                cmd.DispatchCompute(m_AutoExposureCS, kernel, 1, 1, 1);
                //copy current exposure to the other pingpong target to avoid adapting from black
                cmd.Blit(m_AutoExposurePool[0], m_AutoExposurePool[1]);
                m_ResetHistory = false;
            }
            else
            {
                int pp = m_PingPong;
                var src = m_AutoExposurePool[++pp % 2];
                var dst = m_AutoExposurePool[++pp % 2];
                cmd.SetComputeTextureParam(m_AutoExposureCS, kernel, "_SourceTex", src);
                cmd.SetComputeTextureParam(m_AutoExposureCS, kernel, "_DestinationTex", dst);
                cmd.DispatchCompute(m_AutoExposureCS, kernel, 1, 1, 1);
                m_PingPong = ++pp % 2;
                currentAutoExposure = dst;
            }

            cmd.EndSample("Auto Exposure");


            cmd.SetGlobalTexture("_AutoExposureLUT", currentAutoExposure);
        }


    }
}
