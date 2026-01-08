using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/屏幕空间反射 (Screen Space Reflection)")]
    public class ScreenSpaceReflection : VolumeSetting
    {
        public ScreenSpaceReflection()
        {
            displayName = "屏幕空间反射 (Screen Space Reflection)";
        }
        
        public BoolParameter Enable = new BoolParameter(false);
        
        public override bool IsActive() => Enable.value;
        
        [Tooltip("模式")] 
        public EnumParameter<RaytraceModes> mode = new(RaytraceModes.LinearTracing);

        [Tooltip("分辨率")] 
        public EnumParameter<Resolution> resolution = new(Resolution.Full);

        [Space(6)]
        [Tooltip("强度")]
        public ClampedFloatParameter intensity = new(1f, 0f, 5f);
        
        [Tooltip("实际上是追踪步长, 越大精度越低, 追踪范围越大, 越节省追踪次数")]
        public ClampedFloatParameter thickness = new(8f, 1f, 64f);
        
        public ClampedFloatParameter minSmoothness = new (0.9f, 0.0f, 1.0f);
        public ClampedFloatParameter smoothnessFadeStart = new (0.9f, 0.0f, 1.0f);
        
        [Tooltip("最大追踪次数")]
        public ClampedIntParameter maximumIterationCount = new(256, 1, 256);

        [Tooltip("最大追踪距离")]
        public MinFloatParameter maximumMarchDistance = new(100f, 0f);

        [Tooltip("值越大, 未追踪部分天空颜色会越多, 过度边界会越硬")]
        public ClampedFloatParameter distanceFade = new(0.02f, 0f, 1f);
        
        [Tooltip("边缘渐变")]
        [InspectorName("Screen Edge Fade Distance")]
        public ClampedFloatParameter vignette = new(0f, 0f, 1f);
        
        [Header("Debug")]
        public EnumParameter<DebugMode> debugMode = new(DebugMode.Disabled);
        
        public enum RaytraceModes
        {
            LinearTracing = 0,
            HiZTracing = 1
        }
        
        public enum Resolution
        {
            Quarter,
            Half,
            Full,
            Double
        }

        public enum DebugMode
        {
            Disabled,
            SSROnly,
        }
    }
    
    
    [PostProcess("ScreenSpaceReflection", PostProcessInjectionPoint.AfterRenderingSkybox, SupportRenderPath.Deferred)]
    public partial class ScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceReflection>
    {
        static class ShaderConstants
        {
            internal static readonly int SsrLightingTexture = Shader.PropertyToID("_SsrLightingTexture");
            internal static readonly int SsrHitPointTexture = Shader.PropertyToID("_SsrHitPointTexture");
            internal static readonly int _SSR_TestTex_TexelSize = Shader.PropertyToID("_SsrHitPointTexture_TexelSize");

            internal static readonly int ViewMatrix = Shader.PropertyToID("_ViewMatrixSSR");
            internal static readonly int InverseViewMatrix = Shader.PropertyToID("_InverseViewMatrixSSR");
            internal static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrixSSR");

            internal static readonly int Params1 = Shader.PropertyToID("_Params1");
            internal static readonly int Params2 = Shader.PropertyToID("_Params2");
            
            public static readonly int SsrIntensity = Shader.PropertyToID("_SSRIntensity");
            public static readonly int SsrRoughnessFadeEnd = Shader.PropertyToID("_SsrRoughnessFadeEnd");
            public static readonly int SsrRoughnessFadeEndTimesRcpLength = Shader.PropertyToID("_SsrRoughnessFadeEndTimesRcpLength");
            public static readonly int SsrRoughnessFadeRcpLength = Shader.PropertyToID("_SsrRoughnessFadeRcpLength");
            public static readonly int SsrEdgeFadeRcpLength = Shader.PropertyToID("_SsrEdgeFadeRcpLength");
            
            public static readonly int _BlitTexture = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _CameraDepthTexture = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _BlitScaleBias = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _GBuffer2 = MemberNameHelpers.ShaderPropertyID();
            public static readonly int SSR_Lighting_Texture = Shader.PropertyToID("SSR_Lighting_Texture");

            public static string GetDebugKeyword(ScreenSpaceReflection.DebugMode debugMode)
            {
                switch (debugMode)
                {
                    case ScreenSpaceReflection.DebugMode.SSROnly:
                        return "DEBUG_SCREEN_SPACE_REFLECTION";
                    case ScreenSpaceReflection.DebugMode.Disabled:
                    default:
                        return "_";
                }
            }
        }

        internal enum ShaderPasses
        {
            Test = 0,
            HizTest = 1,
            Reproject = 2,
            Composite = 3,
        }

        
        RenderTextureDescriptor m_ScreenSpaceReflectionDescriptor;
        readonly string[] m_ShaderKeywords = new string[1];
        Material m_ScreenSpaceReflectionMaterial;

        RTHandle m_SsrHitPointRT;
        RTHandle m_SsrLightingRT;
        
        private readonly ProfilingSampler m_TracingSampler = new("SSR Tracing");
        private readonly ProfilingSampler m_ReprojectionSampler = new("SSR Reprojection");
        private readonly ProfilingSampler m_AccumulationSampler = new("SSR Accumulation");
        
        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Motion;
        public override PostProcessPassInput postProcessPassInput => settings.mode.value == ScreenSpaceReflection.RaytraceModes.HiZTracing ? PostProcessPassInput.HiZ : PostProcessPassInput.None;

        public override void Setup()
        {
            base.Setup();
            
            if (m_ScreenSpaceReflectionMaterial == null)
            {
                var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceReflectionResources>();
                m_ScreenSpaceReflectionMaterial = GetMaterial(runtimeResources.screenSpaceReflectionPS);
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            GetSSRDesc(renderingData.cameraData.cameraTargetDescriptor);

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrHitPointRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Point, name: "SSR_Hit_Point_Texture");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrLightingRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Bilinear, name: "SSR_Lighting_Texture");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(renderingData.cameraData.camera);

            using (new ProfilingScope(cmd, m_TracingSampler))
            {
                if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.LinearTracing)
                    Blit(cmd, source, m_SsrHitPointRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Test);
                else
                    Blit(cmd, source, m_SsrHitPointRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.HizTest);
            }

            using (new ProfilingScope(cmd, m_ReprojectionSampler))
            {
                m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.SsrHitPointTexture, m_SsrHitPointRT);
                Blit(cmd, source, m_SsrLightingRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Reproject);
            }

            using (new ProfilingScope(cmd, m_AccumulationSampler))
            {
                m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.SsrLightingTexture, m_SsrLightingRT);
                Blit(cmd, source, target, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Composite);
            }
        }
        
        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_ScreenSpaceReflectionMaterial);
            m_ScreenSpaceReflectionMaterial = null;

            m_SsrLightingRT?.Release();
            m_SsrHitPointRT?.Release();
        }
    }
}