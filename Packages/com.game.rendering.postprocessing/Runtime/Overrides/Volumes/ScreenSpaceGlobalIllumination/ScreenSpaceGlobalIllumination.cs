using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;

namespace Game.Core.PostProcessing
{
    // https://www.shadertoy.com/view/fddfDX
    [Serializable, VolumeComponentMenu("Post-processing Custom/全局光照 (Screen Space Global Illumination)")]
    public class ScreenSpaceGlobalIllumination : VolumeSetting
    {
        public ScreenSpaceGlobalIllumination()
        {
            displayName = "全局光照 (Screen Space Global Illumination)";
        }

        [Header("TraceProperty")]
        [Range(1, 16)]
        public ClampedIntParameter NumRays = new ClampedIntParameter(10, 1, 16);

        [Range(8, 32)]
        public ClampedIntParameter NumSteps = new ClampedIntParameter(8, 8, 32);

        [Range(0.05f, 5.0f)]
        public ClampedFloatParameter Thickness = new ClampedFloatParameter(0.1f, 0.05f, 5.0f);

        [Range(1, 5)]
        public ClampedFloatParameter Intensity = new ClampedFloatParameter(1f, 1f, 5f);

        /////////////////////////////////////////////////////////////////////////////////////////////
        [Header("FilterProperty")]
        [Range(1, 4)]
        public ClampedIntParameter NumSpatial = new ClampedIntParameter(1, 1, 4);

        [Range(1, 2)]
        public ClampedFloatParameter SpatialRadius = new ClampedFloatParameter(2f, 1f, 2f);

        [Range(0, 8)]
        public ClampedFloatParameter TemporalScale = new ClampedFloatParameter(1.25f, 0f, 8f);

        [Range(0, 0.99f)]
        public ClampedFloatParameter TemporalWeight = new ClampedFloatParameter(0.99f, 0f, 0.99f);

        [Range(0, 2)]
        public ClampedIntParameter NumBilateral = new ClampedIntParameter(2, 0, 2);

        [Range(0.1f, 1)]
        public ClampedFloatParameter BilateralColorWeight = new ClampedFloatParameter(1f, 0.1f, 1f);

        [Range(0.1f, 1)]
        public ClampedFloatParameter BilateralDepthWeight = new ClampedFloatParameter(1f, 0.1f, 1f);

        [Range(0.1f, 1)]
        public ClampedFloatParameter BilateralNormalWeight = new ClampedFloatParameter(0.1f, 0.1f, 1);

        public override bool IsActive() => true;
    }


    [PostProcess("Screen Space Global Illumination", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class ScreenSpaceGlobalIlluminationRenderer : PostProcessVolumeRenderer<ScreenSpaceGlobalIllumination>
    {
        static class ShaderConstants
        {
            public static int NumRays = Shader.PropertyToID("SSGi_NumRays");
            public static int NumSteps = Shader.PropertyToID("SSGi_NumSteps");
            public static int RayMask = Shader.PropertyToID("SSGi_RayMask");
            public static int FrameIndex = Shader.PropertyToID("SSGi_FrameIndex");
            public static int Thickness = Shader.PropertyToID("SSGi_Thickness");
            public static int Intensity = Shader.PropertyToID("SSGi_Intensity");
            public static int TraceResolution = Shader.PropertyToID("SSGi_TraceResolution");
            public static int UAV_ScreenIrradiance = Shader.PropertyToID("UAV_ScreenIrradiance");

            public static int Matrix_Proj = Shader.PropertyToID("Matrix_Proj");
            public static int Matrix_InvProj = Shader.PropertyToID("Matrix_InvProj");
            public static int Matrix_ViewProj = Shader.PropertyToID("Matrix_ViewProj");
            public static int Matrix_InvViewProj = Shader.PropertyToID("Matrix_InvViewProj");
            public static int Matrix_WorldToView = Shader.PropertyToID("Matrix_WorldToView");

            public static int SRV_PyramidColor = Shader.PropertyToID("SRV_PyramidColor");
            public static int SRV_PyramidDepth = Shader.PropertyToID("SRV_PyramidDepth");
            public static int SRV_SceneDepth = Shader.PropertyToID("SRV_SceneDepth");
            public static int SRV_GBufferNormal = Shader.PropertyToID("SRV_GBufferNormal");
        }

        public struct SSGiParameterDescriptor
        {
            public bool RayMask;
            public int NumRays;
            public int NumSteps;
            public float Thickness;
            public float Intensity;
        }

        public struct SSGiInputDescriptor
        {
            public int FrameIndex;
            public float4 TraceResolution;
            public float4x4 Matrix_Proj;
            public float4x4 Matrix_InvProj;
            public float4x4 Matrix_ViewProj;
            public float4x4 Matrix_InvViewProj;
            public float4x4 Matrix_WorldToView;
            public RenderTargetIdentifier SRV_PyramidColor;
            public RenderTargetIdentifier SRV_PyramidDepth;
            public RenderTargetIdentifier SRV_SceneDepth;
            public RenderTargetIdentifier SRV_GBufferNormal;
        }


        private Material m_Material;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.ScreenSpaceGlobalIlluminationPS);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            Blit(cmd, source, destination, m_Material, 0);
        }
    }
}
