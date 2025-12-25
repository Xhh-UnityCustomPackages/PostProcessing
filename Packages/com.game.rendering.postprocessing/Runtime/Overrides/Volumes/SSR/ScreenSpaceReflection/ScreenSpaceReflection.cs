using System;
using UnityEngine;
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
        public ClampedFloatParameter vignette = new(0f, 0f, 1f);

        [Tooltip("减少闪烁问题, 需要MotionVector, SceneView未处理")]
        public BoolParameter antiFlicker = new(true);

        [Space(20)]
        public EnumParameter<DebugMode> debugMode = new(DebugMode.Disabled);
    }
    
    
    [PostProcess("ScreenSpaceReflection", PostProcessInjectionPoint.BeforeRenderingPostProcessing, SupportRenderPath.Deferred)]
    public partial class ScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceReflection>
    {
        static class ShaderConstants
        {
            internal static readonly int s_CameraNormalsTextureID = Shader.PropertyToID("_CameraNormalsTexture");
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
            Resolve = 1,
            Reproject = 2,
            Composite = 3,
            MobilePlanarReflection = 4,
            MobileAntiFlicker = 5,
            HizTest = 6,
        }

        RenderTextureDescriptor m_ScreenSpaceReflectionDescriptor;
        string[] m_ShaderKeywords = new string[2];
        Material m_ScreenSpaceReflectionMaterial;

        bool m_SupportARGBHalf = true;

        RTHandle m_TestRT;
        RTHandle m_ResloveRT;
        RTHandle m_ResloveBlurRT;
        

        const int k_NumHistoryTextures = 2;
        RTHandle[] m_HistoryPingPongRT = new RTHandle[k_NumHistoryTextures];
        int m_PingPong = 0;

        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;

        public void GetHistoryPingPongRT(ref RTHandle rt1, ref RTHandle rt2)
        {
            int index = m_PingPong;
            m_PingPong = ++m_PingPong % 2;

            rt1 = m_HistoryPingPongRT[index];
            rt2 = m_HistoryPingPongRT[m_PingPong];
        }

        public override void Setup()
        {
            m_SupportARGBHalf = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
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
            var testDesc = m_ScreenSpaceReflectionDescriptor;
            if (m_SupportARGBHalf)
            {
                testDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                m_ScreenSpaceReflectionDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            }
            else
            {
                // resolve需要一个渐变模糊后参与最终混合, 必须要Alpha通道
                // 移动端没办法 就只能降到LDR了
                m_ScreenSpaceReflectionDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            }

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TestRT, testDesc, FilterMode.Point, name: "_SSR_TestTex");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_ResloveRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Bilinear, name: "_SSR_ResolveTex");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_ResloveBlurRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Bilinear, name: "_SSR_ResolveBlurTex");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(renderingData.cameraData.camera, renderingData.cameraData.cameraTargetDescriptor.width, renderingData.cameraData.cameraTargetDescriptor.height);

            if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.LinearTracing)
                Blit(cmd, source, m_TestRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Test);
            else
                Blit(cmd, source, m_TestRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.HizTest);
            
            m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.TestTex, m_TestRT);
            Blit(cmd, source, m_ResloveRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Resolve);


            RTHandle lastDownId = m_ResloveRT;
            // ----------------------------------------------------------------------------------
            // 简化版本没有用Jitter所以sceneview部分就不处理了
            if (!renderingData.cameraData.isSceneViewCamera && settings.antiFlicker.value)
            {
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_HistoryPingPongRT[0], m_ScreenSpaceReflectionDescriptor);
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_HistoryPingPongRT[1], m_ScreenSpaceReflectionDescriptor);

                // 不确定移动端CopyTexture的支持，所以先用这种方法
                RTHandle rt1 = null, rt2 = null;
                GetHistoryPingPongRT(ref rt1, ref rt2);

                m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.HistoryTex, rt1);
                Blit(cmd, m_ResloveRT, rt2, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.MobileAntiFlicker);
                lastDownId = rt2;
            }
            // ----------------------------------------------------------------------------------


            // ------------------------------------------------------------------------------------------------
            // 简化版本 DualBlur替代 放弃不同粗糙度mipmap的采样
            var finalRT = m_ResloveRT;
            var iter = settings.blurIterations.value;
            if (iter > 0)
            {
                PyramidBlur.ComputeBlurPyramid(cmd, lastDownId, m_ResloveBlurRT, 0.1f, iter);
                finalRT = m_ResloveBlurRT;
            }
            
            m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.ResolveTex, finalRT);
            Blit(cmd, source, target, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Composite);
        }

        
        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_ScreenSpaceReflectionMaterial);
            m_ScreenSpaceReflectionMaterial = null;

            m_ResloveRT?.Release();
            m_TestRT?.Release();

            for (int i = 0; i < m_HistoryPingPongRT.Length; i++)
                m_HistoryPingPongRT[i]?.Release();
        }
    }
}