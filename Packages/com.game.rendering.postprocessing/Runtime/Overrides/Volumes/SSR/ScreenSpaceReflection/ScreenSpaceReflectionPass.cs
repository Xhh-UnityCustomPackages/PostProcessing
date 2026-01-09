using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
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

            public static readonly int SEPARATION_POS = Shader.PropertyToID("SEPARATION_POS");
            public static readonly int _BlitTexture = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _CameraDepthTexture = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _BlitScaleBias = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _GBuffer2 = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _SsrAccumPrev = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int SSR_Lighting_Texture = Shader.PropertyToID("SSR_Lighting_Texture");

            public static string GetDebugKeyword(ScreenSpaceReflection.DebugMode debugMode)
            {
                switch (debugMode)
                {
                    case ScreenSpaceReflection.DebugMode.SSROnly:
                        return "DEBUG_SCREEN_SPACE_REFLECTION";
                    case ScreenSpaceReflection.DebugMode.Split:
                        return "SPLIT_SCREEN_SPACE_REFLECTION";
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
        private ComputeShader m_ComputeShader;
        private bool m_TracingInCS;

        RTHandle m_SsrHitPointRT;
        RTHandle m_SsrLightingRT;
        private bool m_NeedAccumulate; //usePBRAlgo
        private float m_ScreenSpaceAccumulationResolutionScale;
        
        private readonly int m_AccumulateNoWorldSpeedRejectionBothKernel;
        private readonly int m_AccumulateSmoothSpeedRejectionBothKernel;
        private readonly int m_AccumulateNoWorldSpeedRejectionBothDebugKernel;
        private readonly int m_AccumulateSmoothSpeedRejectionBothDebugKernel;

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

            if (m_ComputeShader == null)
            {
                m_ComputeShader = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceReflectionResources>().screenSpaceReflectionCS;
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            GetSSRDesc(renderingData.cameraData.cameraTargetDescriptor);

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrHitPointRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Point, name: "SSR_Hit_Point_Texture");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrLightingRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Bilinear, name: "SSR_Lighting_Texture");

            m_NeedAccumulate = renderingData.cameraData.cameraType == CameraType.Game
                               && settings.usedAlgorithm.value == ScreenSpaceReflection.ScreenSpaceReflectionAlgorithm.PBRAccumulation;

            if (m_NeedAccumulate)
            {
                AllocateScreenSpaceAccumulationHistoryBuffer(GetScaleFactor());
            }
        }

        private void AllocateScreenSpaceAccumulationHistoryBuffer(float scaleFactor)
        {
            if (!Mathf.Approximately(scaleFactor, m_ScreenSpaceAccumulationResolutionScale) || context.GetCurrentFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation) == null)
            {
                context.ReleaseHistoryFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation);

                var ssrAlloc = new PostProcessFeatureContext.CustomHistoryAllocator(new Vector2(scaleFactor, scaleFactor), GraphicsFormat.R16G16B16A16_SFloat, "SSR_Accum Packed history");
                context.AllocHistoryFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation, ssrAlloc.Allocator, 2);
                m_ScreenSpaceAccumulationResolutionScale = scaleFactor;
            }
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(renderingData.cameraData.camera);
            ref var cameraData = ref renderingData.cameraData;
            
            using (new ProfilingScope(cmd, m_TracingSampler))
            {
                if (m_TracingInCS)
                {
                }
                else
                {
                    if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.LinearTracing)
                        Blit(cmd, source, m_SsrHitPointRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Test);
                    else
                        Blit(cmd, source, m_SsrHitPointRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.HizTest);
                }
            }

            using (new ProfilingScope(cmd, m_ReprojectionSampler))
            {
                m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.SsrHitPointTexture, m_SsrHitPointRT);
                Blit(cmd, source, m_SsrLightingRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Reproject);
            }

            RTHandle finalResult;
            if (m_NeedAccumulate)
            {
                using (new ProfilingScope(cmd, m_AccumulationSampler))
                {
                    var histroy = context.GetPreviousFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation);
                    m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants._SsrAccumPrev, histroy);
                    m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.SsrHitPointTexture, m_SsrHitPointRT);
                    //合成累计Lighting
                    Blit(cmd, m_SsrLightingRT, histroy);
                    Blit(cmd, m_SsrLightingRT, target, m_ScreenSpaceReflectionMaterial, 4);
                    finalResult = histroy;
                    return;
                }
            }
            else
            {
                finalResult = m_SsrLightingRT;
            }


            //一些做法是SetGlobal 然后后续进行合成 就能减少一次Blit
            m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.SsrLightingTexture, finalResult);
            Blit(cmd, source, target, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Composite);
        }


        private void ExecuteAccumulation(CommandBuffer cmd, ref CameraData cameraData, bool useAsyncCompute)
        {
            int kernel;
            if (false)//debug
            {
                if (settings.enableWorldSpeedRejection.value)
                {
                    kernel = m_AccumulateSmoothSpeedRejectionBothDebugKernel;
                }
                else
                {
                    kernel = m_AccumulateNoWorldSpeedRejectionBothDebugKernel;
                }
            }
            else
            {
                if (settings.enableWorldSpeedRejection.value)
                {
                    kernel = m_AccumulateSmoothSpeedRejectionBothKernel;
                }
                else
                {
                    kernel = m_AccumulateNoWorldSpeedRejectionBothKernel;
                }
            }

           
            
            var ssrAccum = context.GetCurrentFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation);
            var ssrAccumPrev = context.GetPreviousFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation);
            // var preFrameColorRT = context.GetPreviousFrameColorRT(cameraData, out bool isNewFrame);
            
            int groupsX = GraphicsUtility.DivRoundUp(m_ScreenSpaceReflectionDescriptor.width, 8);
            int groupsY = GraphicsUtility.DivRoundUp(m_ScreenSpaceReflectionDescriptor.height, 8);
            
            // cmd.DispatchCompute(m_ComputeShader, kernel, groupsX, groupsY, 1);
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