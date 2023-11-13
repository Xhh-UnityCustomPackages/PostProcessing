using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/随机屏幕空间反射 (Stochastic Screen Space Reflection)")]
    public class StochasticScreenSpaceReflection : VolumeSetting
    {
        public StochasticScreenSpaceReflection()
        {
            displayName = "随机屏幕空间反射 (Stochastic Screen Space Reflection)";
        }

        public enum RenderResolution
        {
            Full = 1,
            Half = 2
        };

        private enum DebugPass
        {
            Combine = 9,
            SSRColor = 10
        };

        public enum TraceApprox
        {
            HiZTrace = 0,
            LinearTrace = 1
        };


        [Serializable]
        public class ResolutionParameter : VolumeParameter<RenderResolution>
        {
            public ResolutionParameter(RenderResolution value, bool overrideState = false) : base(value, overrideState) { }
        }

        [Serializable]
        public class TraceApproxParameter : VolumeParameter<TraceApprox>
        {
            public TraceApproxParameter(TraceApprox value, bool overrideState = false) : base(value, overrideState) { }
        }

        public EnableModeParameter enableMode = new(EnableMode.Disable);
        public ResolutionParameter resolution = new(RenderResolution.Full);
        public ClampedIntParameter RayNums = new ClampedIntParameter(1, 1, 4);
        public ClampedFloatParameter BRDFBias = new ClampedFloatParameter(0.7f, 0f, 1f);
        public ClampedFloatParameter Thickness = new ClampedFloatParameter(0.1f, 0.05f, 5f);
        public ClampedFloatParameter ScreenFade = new ClampedFloatParameter(0.1f, 0f, 0.5f);
        public TraceApproxParameter TraceMethod = new(TraceApprox.LinearTrace);


        // Show If TraceMethod==TraceApprox.HiZTrace
        [Space(10)]
        [Header("Linear_Trace Property")]
        public ClampedIntParameter HiZ_RaySteps = new ClampedIntParameter(58, 32, 512);
        public ClampedIntParameter HiZ_MaxLevel = new ClampedIntParameter(10, 4, 10);
        public ClampedIntParameter HiZ_StartLevel = new ClampedIntParameter(1, 0, 2);
        public ClampedIntParameter HiZ_StopLevel = new ClampedIntParameter(0, 0, 2);

        // Show If TraceMethod==TraceApprox.HiZTrace
        [Space(10)]
        [Header("Linear_Trace Property")]
        public BoolParameter Linear_TraceBehind = new(false);
        public BoolParameter Linear_TowardRay = new(true);
        public IntParameter Linear_RayDistance = new(512);
        public ClampedIntParameter Linear_RaySteps = new(256, 64, 512);
        public ClampedIntParameter Linear_StepSize = new(10, 5, 20);

        [Space(10)]
        [Header("Filtter Property")]
        public Texture2DParameter BlueNoise_LUT = new Texture2DParameter(null);
        public Texture2DParameter PreintegratedGF_LUT = new Texture2DParameter(null);
        public ClampedIntParameter SpatioSampler = new(9, 1, 9);
        public ClampedFloatParameter TemporalWeight = new(0.98f, 0f, 0.99f);
        public ClampedFloatParameter TemporalScale = new(1.25f, 1f, 5f);


        public override bool IsActive() => enableMode.value == EnableMode.Enable;
    }


    [PostProcess("StochasticScreenSpaceReflection", PostProcessInjectionPoint.BeforeRenderingDeferredLights)]
    public class StochasticScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<StochasticScreenSpaceReflection>
    {
        static class ShaderConstants
        {
            internal static readonly int SSR_Jitter_ID = Shader.PropertyToID("_SSR_Jitter");
            internal static readonly int SSR_BRDFBias_ID = Shader.PropertyToID("_SSR_BRDFBias");
            internal static readonly int SSR_NumSteps_Linear_ID = Shader.PropertyToID("_SSR_NumSteps_Linear");
            internal static readonly int SSR_NumSteps_HiZ_ID = Shader.PropertyToID("_SSR_NumSteps_HiZ");
            internal static readonly int SSR_NumRays_ID = Shader.PropertyToID("_SSR_NumRays");
            internal static readonly int SSR_NumResolver_ID = Shader.PropertyToID("_SSR_NumResolver");
            internal static readonly int SSR_ScreenFade_ID = Shader.PropertyToID("_SSR_ScreenFade");
            internal static readonly int SSR_Thickness_ID = Shader.PropertyToID("_SSR_Thickness");
            internal static readonly int SSR_TemporalScale_ID = Shader.PropertyToID("_SSR_TemporalScale");
            internal static readonly int SSR_TemporalWeight_ID = Shader.PropertyToID("_SSR_TemporalWeight");
            internal static readonly int SSR_ScreenSize_ID = Shader.PropertyToID("_SSR_ScreenSize");
            internal static readonly int SSR_RayCastSize_ID = Shader.PropertyToID("_SSR_RayCastSize");
            internal static readonly int SSR_NoiseSize_ID = Shader.PropertyToID("_SSR_NoiseSize");
            internal static readonly int SSR_RayStepSize_ID = Shader.PropertyToID("_SSR_RayStepSize");
            internal static readonly int SSR_ProjInfo_ID = Shader.PropertyToID("_SSR_ProjInfo");
            internal static readonly int SSR_CameraClipInfo_ID = Shader.PropertyToID("_SSR_CameraClipInfo");
            internal static readonly int SSR_TraceDistance_ID = Shader.PropertyToID("_SSR_TraceDistance");
            internal static readonly int SSR_BackwardsRay_ID = Shader.PropertyToID("_SSR_BackwardsRay");
            internal static readonly int SSR_TraceBehind_ID = Shader.PropertyToID("_SSR_TraceBehind");
            internal static readonly int SSR_CullBack_ID = Shader.PropertyToID("_SSR_CullBack");
            internal static readonly int SSR_HiZ_PrevDepthLevel_ID = Shader.PropertyToID("_SSR_HiZ_PrevDepthLevel");
            internal static readonly int SSR_HiZ_MaxLevel_ID = Shader.PropertyToID("_SSR_HiZ_MaxLevel");
            internal static readonly int SSR_HiZ_StartLevel_ID = Shader.PropertyToID("_SSR_HiZ_StartLevel");
            internal static readonly int SSR_HiZ_StopLevel_ID = Shader.PropertyToID("_SSR_HiZ_StopLevel");



            internal static readonly int SSR_Noise_ID = Shader.PropertyToID("_SSR_Noise");
            internal static readonly int SSR_PreintegratedGF_LUT_ID = Shader.PropertyToID("_SSR_PreintegratedGF_LUT");

            internal static readonly int SSR_HierarchicalDepth_ID = Shader.PropertyToID("_SSR_HierarchicalDepth_RT");
            internal static readonly int SSR_SceneColor_ID = Shader.PropertyToID("_SSR_SceneColor_RT");
            internal static readonly int SSR_CombineScene_ID = Shader.PropertyToID("_SSR_CombienReflection_RT");



            internal static readonly int SSR_Trace_ID = Shader.PropertyToID("_SSR_RayCastRT");
            internal static readonly int SSR_Mask_ID = Shader.PropertyToID("_SSR_RayMask_RT");
            internal static readonly int SSR_Spatial_ID = Shader.PropertyToID("_SSR_Spatial_RT");
            internal static readonly int SSR_TemporalPrev_ID = Shader.PropertyToID("_SSR_TemporalPrev_RT");
            internal static readonly int SSR_TemporalCurr_ID = Shader.PropertyToID("_SSR_TemporalCurr_RT");



            internal static readonly int SSR_ProjectionMatrix_ID = Shader.PropertyToID("_SSR_ProjectionMatrix");
            internal static readonly int SSR_ViewProjectionMatrix_ID = Shader.PropertyToID("_SSR_ViewProjectionMatrix");
            internal static readonly int SSR_LastFrameViewProjectionMatrix_ID = Shader.PropertyToID("_SSR_LastFrameViewProjectionMatrix");
            internal static readonly int SSR_InverseProjectionMatrix_ID = Shader.PropertyToID("_SSR_InverseProjectionMatrix");
            internal static readonly int SSR_InverseViewProjectionMatrix_ID = Shader.PropertyToID("_SSR_InverseViewProjectionMatrix");
            internal static readonly int SSR_WorldToCameraMatrix_ID = Shader.PropertyToID("_SSR_WorldToCameraMatrix");
            internal static readonly int SSR_CameraToWorldMatrix_ID = Shader.PropertyToID("_SSR_CameraToWorldMatrix");
            internal static readonly int SSR_ProjectToPixelMatrix_ID = Shader.PropertyToID("_SSR_ProjectToPixelMatrix");

        }

        enum PassIndex
        {
            RenderPass_Linear2D_SingelSPP = 0,
            RenderPass_HiZ3D_SingelSpp = 1,
            RenderPass_Linear2D_MultiSPP = 2,
            RenderPass_HiZ3D_MultiSpp = 3,
            RenderPass_Spatiofilter_SingleSPP = 4,
            RenderPass_Spatiofilter_MultiSPP = 5,
            RenderPass_Temporalfilter_SingleSPP = 6,
            RenderPass_Temporalfilter_MultiSpp = 7
        }

        Material m_Material;
        RTHandle[] m_SSR_TrackMask;


        private int m_SampleIndex = 0;
        private const int k_SampleCount = 64;
        private float GetHaltonValue(int index, int radix)
        {
            float result = 0f;
            float fraction = 1f / radix;

            while (index > 0)
            {
                result += (index % radix) * fraction;
                index /= radix;
                fraction /= radix;
            }
            return result;
        }

        private Vector2 GenerateRandomOffset()
        {
            var offset = new Vector2(GetHaltonValue(m_SampleIndex & 1023, 2), GetHaltonValue(m_SampleIndex & 1023, 3));
            if (m_SampleIndex++ >= k_SampleCount)
                m_SampleIndex = 0;
            return offset;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;

            if (m_SSR_TrackMask == null)
            {
                m_SSR_TrackMask = new RTHandle[2];
            }

            if (settings.resolution != StochasticScreenSpaceReflection.RenderResolution.Full)
                DescriptorDownSample(ref desc, (int)settings.resolution.value);

            RenderingUtils.ReAllocateIfNeeded(ref m_SSR_TrackMask[0], desc);
            RenderingUtils.ReAllocateIfNeeded(ref m_SSR_TrackMask[1], desc);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            if (m_Material == null)
                m_Material = GetMaterial(postProcessFeatureData.shaders.stochasticScreenSpaceReflectionPS);

            SetupMaterial(ref renderingData, m_Material);

            if (m_Material == null)
                return;

            if (settings.TraceMethod == StochasticScreenSpaceReflection.TraceApprox.HiZTrace)
            {
                Blit(cmd, m_SSR_TrackMask[0], m_SSR_TrackMask[0], m_Material, (settings.RayNums.value > 1) ? (int)PassIndex.RenderPass_HiZ3D_MultiSpp : (int)PassIndex.RenderPass_HiZ3D_SingelSpp);
            }
            else
            {
                Blit(cmd, m_SSR_TrackMask[1], m_SSR_TrackMask[0], m_Material, (settings.RayNums.value > 1) ? (int)PassIndex.RenderPass_Linear2D_MultiSPP : (int)PassIndex.RenderPass_Linear2D_SingelSPP);
            }
        }


        private void SetupMaterial(ref RenderingData renderingData, Material material)
        {


            if (material == null)
                return;

            var width = renderingData.cameraData.cameraTargetDescriptor.width;
            var height = renderingData.cameraData.cameraTargetDescriptor.height;
            var cameraSize = new Vector2(width, height);

            material.SetTexture(ShaderConstants.SSR_PreintegratedGF_LUT_ID, settings.PreintegratedGF_LUT.value);
            material.SetTexture(ShaderConstants.SSR_Noise_ID, settings.BlueNoise_LUT.value);
            material.SetVector(ShaderConstants.SSR_ScreenSize_ID, cameraSize);
            material.SetVector(ShaderConstants.SSR_RayCastSize_ID, cameraSize / (int)settings.resolution.value);
            material.SetVector(ShaderConstants.SSR_NoiseSize_ID, new Vector2(1024, 1024));
            material.SetFloat(ShaderConstants.SSR_BRDFBias_ID, settings.BRDFBias.value);
            material.SetFloat(ShaderConstants.SSR_ScreenFade_ID, settings.ScreenFade.value);
            material.SetFloat(ShaderConstants.SSR_Thickness_ID, settings.Thickness.value);
            material.SetInt(ShaderConstants.SSR_RayStepSize_ID, settings.Linear_StepSize.value);
            material.SetInt(ShaderConstants.SSR_TraceDistance_ID, settings.Linear_RayDistance.value);
            material.SetInt(ShaderConstants.SSR_NumSteps_Linear_ID, settings.Linear_RaySteps.value);
            material.SetInt(ShaderConstants.SSR_NumSteps_HiZ_ID, settings.HiZ_RaySteps.value);
            material.SetInt(ShaderConstants.SSR_NumRays_ID, settings.RayNums.value);
            material.SetInt(ShaderConstants.SSR_BackwardsRay_ID, settings.Linear_TowardRay.value ? 1 : 0);
            material.SetInt(ShaderConstants.SSR_CullBack_ID, settings.Linear_TowardRay.value ? 1 : 0);
            material.SetInt(ShaderConstants.SSR_TraceBehind_ID, settings.Linear_TraceBehind.value ? 1 : 0);
            material.SetInt(ShaderConstants.SSR_HiZ_MaxLevel_ID, settings.HiZ_MaxLevel.value);
            material.SetInt(ShaderConstants.SSR_HiZ_StartLevel_ID, settings.HiZ_StartLevel.value);
            material.SetInt(ShaderConstants.SSR_HiZ_StopLevel_ID, settings.HiZ_StopLevel.value);
            if (true)
            {
                material.SetInt(ShaderConstants.SSR_NumResolver_ID, settings.SpatioSampler.value);
                material.SetFloat(ShaderConstants.SSR_TemporalScale_ID, settings.TemporalScale.value);
                material.SetFloat(ShaderConstants.SSR_TemporalWeight_ID, settings.TemporalWeight.value);
            }
            else
            {
                material.SetInt(ShaderConstants.SSR_NumResolver_ID, 1);
                material.SetFloat(ShaderConstants.SSR_TemporalScale_ID, 0);
                material.SetFloat(ShaderConstants.SSR_TemporalWeight_ID, 0);
            }


        }


        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
        }
    }
}
