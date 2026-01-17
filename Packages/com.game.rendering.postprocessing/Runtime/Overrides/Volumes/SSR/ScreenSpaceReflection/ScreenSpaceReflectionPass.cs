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

            internal static readonly int Params1 = Shader.PropertyToID("_Params1");
            
            public static readonly int SsrIntensity = Shader.PropertyToID("_SSRIntensity");
            public static readonly int Thickness = Shader.PropertyToID("_Thickness");
            public static readonly int SsrThicknessScale = Shader.PropertyToID("_SsrThicknessScale");
            public static readonly int SsrThicknessBias = Shader.PropertyToID("_SsrThicknessBias");
            public static readonly int Steps = Shader.PropertyToID("_Steps");
            public static readonly int StepSize = Shader.PropertyToID("_StepSize");
            public static readonly int SsrDepthPyramidMaxMip = Shader.PropertyToID("_SsrDepthPyramidMaxMip");
            public static readonly int SsrColorPyramidMaxMip = Shader.PropertyToID("_SsrColorPyramidMaxMip");
            public static readonly int SsrDownsamplingDivider = Shader.PropertyToID("_SsrDownsamplingDivider");
            public static readonly int SsrRoughnessFadeEnd = Shader.PropertyToID("_SsrRoughnessFadeEnd");
            public static readonly int SsrRoughnessFadeEndTimesRcpLength = Shader.PropertyToID("_SsrRoughnessFadeEndTimesRcpLength");
            public static readonly int SsrRoughnessFadeRcpLength = Shader.PropertyToID("_SsrRoughnessFadeRcpLength");
            public static readonly int SsrEdgeFadeRcpLength = Shader.PropertyToID("_SsrEdgeFadeRcpLength");
            public static readonly int SsrPBRBias = Shader.PropertyToID("_SsrPBRBias");

            public static readonly int SEPARATION_POS = Shader.PropertyToID("SEPARATION_POS");
            public static readonly int _CameraDepthTexture = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _BlitScaleBias = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _GBuffer2 = MemberNameHelpers.ShaderPropertyID();
            public static readonly int _SsrAccumPrev = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int SSR_Lighting_Texture = Shader.PropertyToID("SSR_Lighting_Texture");
            public static readonly int _DepthPyramidMipLevelOffsets = MemberNameHelpers.ShaderPropertyID();
            public static readonly int ShaderVariablesScreenSpaceReflection = MemberNameHelpers.ShaderPropertyID();

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
            
            public static string GetApproxKeyword(ScreenSpaceReflection.ScreenSpaceReflectionAlgorithm algorithmMode)
            {
                switch (algorithmMode)
                {
                    case ScreenSpaceReflection.ScreenSpaceReflectionAlgorithm.Approximation:
                        return "SSR_APPROX";
                    default:
                        return "_";
                }
            }

            public static string GetUseMipmapKeyword(bool enableMipmap)
            {
                return enableMipmap ? "SSR_USE_COLOR_PYRAMID" : "_";
            }
        }

        internal enum ShaderPasses
        {
            Test = 0,
            HizTest = 1,
            Reproject = 2,
            Composite = 3,
        }


        RenderTextureDescriptor m_SSRTestDescriptor;
        RenderTextureDescriptor m_SSRColorDescriptor;
        readonly string[] m_ShaderKeywords = new string[3];
        Material m_ScreenSpaceReflectionMaterial;
        private ComputeShader m_ComputeShader;
        private bool m_UseCS;

        RTHandle m_SsrHitPointRT;
        RTHandle m_SsrLightingRT;
        private bool m_NeedAccumulate; //usePBRAlgo
        private float m_ScreenSpaceAccumulationResolutionScale;
        
        private int m_AccumulateNoWorldSpeedRejectionBothKernel;
        private int m_AccumulateSmoothSpeedRejectionBothKernel;
        private int m_AccumulateNoWorldSpeedRejectionBothDebugKernel;
        private int m_AccumulateSmoothSpeedRejectionBothDebugKernel;
        private int m_TracingKernel;
        private int m_ReprojectionKernel;

        private readonly ProfilingSampler m_TracingSampler = new("SSR Tracing");
        private readonly ProfilingSampler m_ReprojectionSampler = new("SSR Reprojection");
        private readonly ProfilingSampler m_AccumulationSampler = new("SSR Accumulation");
        

        public override bool visibleInSceneView => settings.visibleInSceneView.value;
        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Motion;
        public override PostProcessPassInput postProcessPassInput
        {
            get
            {
                PostProcessPassInput postInput = PostProcessPassInput.None;
                if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.HiZTracing)
                    postInput |= PostProcessPassInput.DepthPyramid;
                
                if(settings.enableMipmap.value)
                    postInput |= PostProcessPassInput.ColorPyramid;
                else
                    postInput |= PostProcessPassInput.PreviousFrameColor;
                return postInput;
            }
        }

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
            
            m_TracingKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionCS");
            m_ReprojectionKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionReprojectionCS");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
           
            m_UseCS = settings.useComputeShader.value;
            
            GetSSRDesc(renderingData.cameraData.cameraTargetDescriptor);

            if (m_UseCS)
            {
                m_SSRTestDescriptor.enableRandomWrite = true;
                m_SSRColorDescriptor.enableRandomWrite = true;
            }

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrHitPointRT, m_SSRTestDescriptor, FilterMode.Point, name: "SSR_Hit_Point_Texture");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_SsrLightingRT, m_SSRColorDescriptor, FilterMode.Bilinear, name: "SSR_Lighting_Texture");

            m_NeedAccumulate = renderingData.cameraData.cameraType == CameraType.Game
                               && settings.usedAlgorithm.value == ScreenSpaceReflection.ScreenSpaceReflectionAlgorithm.PBRAccumulation;

            if (m_NeedAccumulate)
            {
                postProcessData.AllocateScreenSpaceAccumulationHistoryBuffer(GetScaleFactor());
            }
            

            var cameraDepthTexture = cameraData.renderer.cameraDepthTargetHandle;
            var gbuffer = UniversalRenderingUtility.GetGBuffer(renderingData.cameraData.renderer);
            
            using (new ProfilingScope(cmd, m_TracingSampler))
            {
                postProcessData.BindDitheredRNGData1SPP(cmd);
                
                if (m_UseCS)
                {
                    ConstantBuffer.Push(cmd, m_Variables, m_ComputeShader, ShaderConstants.ShaderVariablesScreenSpaceReflection);
                    var offsetBuffer = postProcessData.DepthMipChainInfo.GetOffsetBufferData(postProcessData.DepthPyramidMipLevelOffsetsBuffer);
                    
                    //只支持HiZ模式
                    cmd.SetComputeBufferParam(m_ComputeShader, m_TracingKernel, ShaderConstants._DepthPyramidMipLevelOffsets, offsetBuffer);
                    // cmd.SetComputeTextureParam(m_ComputeShader, m_TracingKernel, ShaderConstants._CameraDepthTexture, cameraDepthTexture, 0, RenderTextureSubElement.Stencil);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_TracingKernel, ShaderConstants._GBuffer2, gbuffer[2]);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_TracingKernel, ShaderConstants.SsrHitPointTexture, m_SsrHitPointRT);
                    
                    int groupsX = PostProcessingUtils.DivRoundUp(m_SSRTestDescriptor.width, 8);
                    int groupsY = PostProcessingUtils.DivRoundUp(m_SSRTestDescriptor.height, 8);
                    cmd.DispatchCompute(m_ComputeShader, m_TracingKernel, groupsX, groupsY, 1);
                }
                else
                { 
                    var propertyBlock = new MaterialPropertyBlock();
                    // We need to set the "_BlitScaleBias" uniform for user materials with shaders relying on core Blit.hlsl to work
                    propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                    SetupMaterials(propertyBlock, renderingData.cameraData.camera);
                    cmd.SetRenderTarget(m_SsrHitPointRT);
                    if (settings.mode.value == ScreenSpaceReflection.RaytraceModes.LinearTracing)
                    {
                        // Blit(cmd, source, m_SsrHitPointRT, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Test);
                        cmd.DrawProcedural(Matrix4x4.identity, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Test, MeshTopology.Triangles, 3, 1, propertyBlock);
                    }
                    else
                    {
                        var offsetBuffer = postProcessData.DepthMipChainInfo.GetOffsetBufferData(postProcessData.DepthPyramidMipLevelOffsetsBuffer);
                        propertyBlock.SetBuffer(ShaderConstants._DepthPyramidMipLevelOffsets, offsetBuffer);
                        cmd.DrawProcedural(Matrix4x4.identity, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.HizTest, MeshTopology.Triangles, 3, 1, propertyBlock);
                    }
                }
            }

            using (new ProfilingScope(cmd, m_ReprojectionSampler))
            {
                RTHandle preFrameColorRT = source;
                if (settings.enableMipmap.value)
                {
                    var colorBufferMipChain = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);
                    if (colorBufferMipChain != null)
                    {
                        preFrameColorRT = colorBufferMipChain;
                    }
                }
                else
                {
                    if (postProcessData.CameraPreviousColorTextureRT != null)
                        preFrameColorRT = postProcessData.CameraPreviousColorTextureRT;
                }

                // if (m_UseCS)
                // {
                //     ConstantBuffer.Push(cmd, m_Variables, m_ComputeShader, ShaderConstants.ShaderVariablesScreenSpaceReflection);
                //     cmd.SetComputeTextureParam(m_ComputeShader, m_ReprojectionKernel, PipelineShaderIDs._ColorPyramidTexture, preFrameColorRT);
                //     cmd.SetComputeTextureParam(m_ComputeShader, m_ReprojectionKernel, ShaderConstants.SsrHitPointTexture, m_SsrHitPointRT);
                //     cmd.SetComputeTextureParam(m_ComputeShader, m_ReprojectionKernel, ShaderConstants.SsrLightingTexture, m_SsrLightingRT);
                //     
                //     int groupsX = PostProcessingUtils.DivRoundUp(m_SSRColorDescriptor.width, 8);
                //     int groupsY = PostProcessingUtils.DivRoundUp(m_SSRColorDescriptor.height, 8);
                //     cmd.DispatchCompute(m_ComputeShader, m_ReprojectionKernel, groupsX, groupsY, 1);
                // }
                // else
                {
                    var propertyBlock = new MaterialPropertyBlock();
                    SetupMaterials(propertyBlock, renderingData.cameraData.camera);
                    propertyBlock.SetTexture(PipelineShaderIDs._ColorPyramidTexture, preFrameColorRT);
                    propertyBlock.SetTexture(ShaderConstants.SsrHitPointTexture, m_SsrHitPointRT);
                    // We need to set the "_BlitScaleBias" uniform for user materials with shaders relying on core Blit.hlsl to work
                    propertyBlock.SetVector(ShaderConstants._BlitScaleBias, new Vector4(1, 1, 0, 0));
                    cmd.SetRenderTarget(m_SsrLightingRT);
                    cmd.DrawProcedural(Matrix4x4.identity, m_ScreenSpaceReflectionMaterial, (int)ShaderPasses.Reproject, MeshTopology.Triangles, 3, 1, propertyBlock);
                }

               
            }

            RTHandle finalResult;
            if (m_NeedAccumulate)
            {
                using (new ProfilingScope(cmd, m_AccumulationSampler))
                {
                    finalResult = m_SsrLightingRT;
                    ExecuteAccumulation(cmd, ref cameraData, false);
                }
            }
            else
            {
                finalResult = m_SsrLightingRT;
            }

            //一些做法是SetGlobal 然后后续进行合成 就能减少一次Blit
            cmd.SetGlobalTexture(ShaderConstants.SSR_Lighting_Texture, finalResult);
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

           
            
            var ssrAccum = postProcessData.GetCurrentFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation);
            var ssrAccumPrev = postProcessData.GetPreviousFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation);
            
            int groupsX = PostProcessingUtils.DivRoundUp(m_SSRTestDescriptor.width, 8);
            int groupsY = PostProcessingUtils.DivRoundUp(m_SSRTestDescriptor.height, 8);
            
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