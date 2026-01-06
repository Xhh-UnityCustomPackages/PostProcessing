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

        public static int MAX_BLUR_ITERATIONS = 4;
        
        public override bool IsActive() => intensity.value > 0;
        
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
        
        [Tooltip("最大追踪次数, 移动端会被固定到10次")]
        public ClampedIntParameter maximumIterationCount = new(256, 1, 256);

        [Tooltip("最大追踪距离")]
        public MinFloatParameter maximumMarchDistance = new(100f, 0f);

        [Tooltip("值越大, 未追踪部分天空颜色会越多, 过度边界会越硬")]
        public ClampedFloatParameter distanceFade = new(0.02f, 0f, 1f);

        [Tooltip("Jitter模式")]
        public EnumParameter<JitterMode> jitterMode = new(JitterMode.BlueNoise);
        
        [Tooltip("模糊迭代次数")]
        public ClampedIntParameter blurIterations = new(3, 0, MAX_BLUR_ITERATIONS);
        
        [Tooltip("边缘渐变")]
        [InspectorName("Screen Edge Fade Distance")]
        public ClampedFloatParameter vignette = new(0f, 0f, 1f);

        [Tooltip("减少闪烁问题, 需要MotionVector, SceneView未处理")]
        public BoolParameter antiFlicker = new(true);

        
        [Space(20)]
        [Header("Debug")]
        public EnumParameter<DebugMode> debugMode = new(DebugMode.Disabled);
        
        
        public enum RaytraceModes
        {
            LinearTracing = 0,
            HiZTracing = 1
        }
        
        public enum Resolution
        {
            Half,
            Full,
            Double
        }

        public enum DebugMode
        {
            Disabled,
            SSROnly,
            IndirectSpecular,
        }

        public enum JitterMode
        {
            Disabled,
            BlueNoise,
            Dither,
        }
    }
    
    
    [PostProcess("ScreenSpaceReflection", PostProcessInjectionPoint.BeforeRenderingDeferredLights, SupportRenderPath.Deferred)]
    public partial class ScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceReflection>
    {
        static class ShaderConstants
        {
            internal static readonly int ResolveTex = Shader.PropertyToID("_SSR_ResolveTex");
            internal static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
            internal static readonly int TestTex = Shader.PropertyToID("_SSR_TestTex");
            internal static readonly int _SSR_TestTex_TexelSize = Shader.PropertyToID("_SSR_TestTex_TexelSize");
            internal static readonly int HistoryTex = Shader.PropertyToID("_HistoryTex");

            internal static readonly int ViewMatrix = Shader.PropertyToID("_ViewMatrixSSR");
            internal static readonly int InverseViewMatrix = Shader.PropertyToID("_InverseViewMatrixSSR");
            internal static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrixSSR");

            internal static readonly int Params1 = Shader.PropertyToID("_Params1");
            internal static readonly int Params2 = Shader.PropertyToID("_Params2");
            internal static readonly int Offset = Shader.PropertyToID("_Offset");

            public static string GetDebugKeyword(ScreenSpaceReflection.DebugMode debugMode)
            {
                switch (debugMode)
                {
                    case ScreenSpaceReflection.DebugMode.SSROnly:
                        return "DEBUG_SCREEN_SPACE_REFLECTION";
                    case ScreenSpaceReflection.DebugMode.IndirectSpecular:
                        return "DEBUG_INDIRECT_SPECULAR";
                    case ScreenSpaceReflection.DebugMode.Disabled:
                    default:
                        return "_";
                }
            }
            
            public static string GetJitterKeyword(ScreenSpaceReflection.JitterMode jitterMode)
            {
                switch (jitterMode)
                {
                    case ScreenSpaceReflection.JitterMode.BlueNoise:
                        return "JITTER_BLURNOISE";
                    case ScreenSpaceReflection.JitterMode.Dither:
                        return "JITTER_DITHER";
                    case ScreenSpaceReflection.JitterMode.Disabled:
                    default:
                        return "_";
                }
            }
        }

        internal enum ShaderPasses
        {
            Test = 0,
            HizTest = 1,
            Resolve = 2,
            Reproject = 3,
            Composite = 4,
        }

        public class SSRTexturesInfo
        {
            public RTHandle current;
            public RTHandle previous;

            public void Clear()
            {
                current?.Release();
                current = null;

                previous?.Release();
                previous = null;
            }

            public bool CreateExposureRT(in CameraType cameraDataCameraType, in RenderTextureDescriptor desc)
            {
                string rtname = CoreUtils.GetTextureAutoName(desc.width, desc.height, desc.graphicsFormat, TextureDimension.Tex2D, string.Format("_SSR_Main_{0}", cameraDataCameraType));
                string rtname2 = CoreUtils.GetTextureAutoName(desc.width, desc.height, desc.graphicsFormat, TextureDimension.Tex2D, string.Format("_SSR_Second_{0}", cameraDataCameraType));
                var RTHandleSign = RenderingUtils.ReAllocateHandleIfNeeded(ref current, in desc, FilterMode.Point, TextureWrapMode.Clamp, name: rtname);
                var RTHandleSign2 = RenderingUtils.ReAllocateHandleIfNeeded(ref previous, in desc, FilterMode.Point, TextureWrapMode.Clamp, name: rtname2);
                return RTHandleSign & RTHandleSign2;
            }
        }

        private ProfilingSampler m_ProfilingSampler_Reproject;
        private ProfilingSampler m_ProfilingSampler_Blur;
        private ProfilingSampler m_ProfilingSampler_Compose;
        
        RenderTextureDescriptor m_ScreenSpaceReflectionDescriptor;
        readonly string[] m_ShaderKeywords = new string[2];
        Material m_ScreenSpaceReflectionMaterial;

        RTHandle m_TestRT;
        RTHandle m_ResloveRT;
        RTHandle m_ResloveBlurRT;
        
        
        RTHandle[] m_HistoryPingPongRT;
        int m_PingPong = 0;
        private static readonly Dictionary<CameraType, SSRTexturesInfo> m_TextureInfos = new ();
        private SSRTexturesInfo m_CurrentTexturesInfo;
        
        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
        public override PostProcessPassInput postProcessPassInput => settings.mode.value == ScreenSpaceReflection.RaytraceModes.HiZTracing ? PostProcessPassInput.HiZ : PostProcessPassInput.None;

        public void GetHistoryPingPongRT(ref RTHandle rt1, ref RTHandle rt2)
        {
            int index = m_PingPong;
            m_PingPong = ++m_PingPong % 2;
            
            rt1 = m_HistoryPingPongRT[index];
            rt2 = m_HistoryPingPongRT[m_PingPong];
        }

        public override void Setup()
        {

            m_ProfilingSampler_Reproject = new ProfilingSampler("SSR Reproject");
            m_ProfilingSampler_Blur = new ProfilingSampler("SSR Blur");
            m_ProfilingSampler_Compose = new ProfilingSampler("SSR Compose");
        }
        
        private SSRTexturesInfo GetOrCreateTextureInfoFromCurCamera(in CameraType cameraDataCameraType)
        {
            if (!m_TextureInfos.ContainsKey(cameraDataCameraType))
            {
                var info = new SSRTexturesInfo();
                bool isSuccess = info.CreateExposureRT(in cameraDataCameraType, m_ScreenSpaceReflectionDescriptor);
                m_TextureInfos.Add(cameraDataCameraType, info);
            }

            return m_TextureInfos[cameraDataCameraType];
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_ScreenSpaceReflectionDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            int size = Mathf.ClosestPowerOfTwo(Mathf.Min(m_ScreenSpaceReflectionDescriptor.width, m_ScreenSpaceReflectionDescriptor.height));

            if (settings.resolution.value == ScreenSpaceReflection.Resolution.Half)
                size >>= 1;
            else if (settings.resolution.value == ScreenSpaceReflection.Resolution.Double)
                size <<= 1;
            GetCompatibleDescriptor(ref m_ScreenSpaceReflectionDescriptor, size, size, m_ScreenSpaceReflectionDescriptor.graphicsFormat);


            // SSR 移动端用B10G11R11 见MakeRenderTextureGraphicsFormat 就算不管Alpha通道问题 精度也非常难受
            m_ScreenSpaceReflectionDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TestRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Point, name: "_SSR_TestTex");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_ResloveRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Bilinear, name: "_SSR_ResolveTex");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_ResloveBlurRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Bilinear, name: "_SSR_ResolveBlurTex");
            
            m_CurrentTexturesInfo = GetOrCreateTextureInfoFromCurCamera(renderingData.cameraData.cameraType);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            SetupMaterials(renderingData.cameraData.camera, desc.width, desc.height);
            
            if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.LinearTracing)
                Blit(cmd, source, m_TestRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Test);
            else
                Blit(cmd, source, m_TestRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.HizTest);
            
            m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.TestTex, m_TestRT);
            Blit(cmd, source, m_ResloveRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Resolve);

            RTHandle lastDownId = m_ResloveRT;
            using (new ProfilingScope(cmd, m_ProfilingSampler_Reproject))
            {
                // ----------------------------------------------------------------------------------
                // 简化版本没有用Jitter所以sceneview部分就不处理了
                if (!renderingData.cameraData.isSceneViewCamera && settings.antiFlicker.value)
                {
                    if (m_HistoryPingPongRT == null) m_HistoryPingPongRT = new RTHandle[2];
                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_HistoryPingPongRT[0], m_ScreenSpaceReflectionDescriptor, name:"_SSR_History_0");
                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_HistoryPingPongRT[1], m_ScreenSpaceReflectionDescriptor, name:"_SSR_History_1");

                    RTHandle rt1 = null, rt2 = null;
                    GetHistoryPingPongRT(ref rt1, ref rt2);
                    
                    var camera = renderingData.cameraData.camera;
                    // GrabExposureRequiredTextures(camera, out var rt1, out var rt2);

                    m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.HistoryTex, rt1);
                    Blit(cmd, m_ResloveRT, rt2, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Reproject);
                    lastDownId = rt2;
                }
            }
           


            // ------------------------------------------------------------------------------------------------
            // 简化版本 DualBlur替代 放弃不同粗糙度mipmap的采样
            var finalRT = m_ResloveRT;
            using (new ProfilingScope(cmd, m_ProfilingSampler_Blur))
            {
                var iter = settings.blurIterations.value;
                if (iter > 0)
                {
                    PyramidBlur.ComputeBlurPyramid(cmd, lastDownId, m_ResloveBlurRT, 0.1f, iter);
                    finalRT = m_ResloveBlurRT;
                }
            }
            
            //合成
            using (new ProfilingScope(cmd, m_ProfilingSampler_Compose))
            {
                m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.ResolveTex, finalRT);
                Blit(cmd, source, target, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Composite);
            }
            
            UpdateCurFrameSSRRT(m_CurrentTexturesInfo);
        }
        
        void GrabExposureRequiredTextures(Camera camera, out RTHandle prevExposure, out RTHandle nextExposure)
        {
            prevExposure = m_CurrentTexturesInfo.current;
            nextExposure = m_CurrentTexturesInfo.previous;

            // Debug.LogError($"Prev:{prevExposure.name}- Next:{nextExposure.name}");
        }
        
        private void UpdateCurFrameSSRRT(SSRTexturesInfo curCameraExposureTexturesInfo)
        {
            if (curCameraExposureTexturesInfo.current == null || curCameraExposureTexturesInfo.previous == null)
            {
                return;
            }

            (curCameraExposureTexturesInfo.current, curCameraExposureTexturesInfo.previous) = (curCameraExposureTexturesInfo.previous, curCameraExposureTexturesInfo.current);
        }

        
        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_ScreenSpaceReflectionMaterial);
            m_ScreenSpaceReflectionMaterial = null;

            m_ResloveRT?.Release();
            m_TestRT?.Release();

            foreach (var exposureInfo in m_TextureInfos.Values)
            {
                exposureInfo.Clear();
            }
            
            if (m_HistoryPingPongRT != null)
            {
                for (int i = 0; i < m_HistoryPingPongRT.Length; i++)
                    m_HistoryPingPongRT[i]?.Release();
            }
        }
    }
}