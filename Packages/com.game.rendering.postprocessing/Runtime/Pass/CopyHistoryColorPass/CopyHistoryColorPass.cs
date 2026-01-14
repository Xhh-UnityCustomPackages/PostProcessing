using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Game.Core.PostProcessing
{
    public class CopyHistoryColorPass : CopyColorPass
    {
        public static CopyHistoryColorPass Create(PostProcessFeatureContext context)
        {
            var shadersResources = GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineRuntimeShaders>();
            var blitMaterial = CoreUtils.CreateEngineMaterial(shadersResources.coreBlitPS);
            var samplingMaterial = CoreUtils.CreateEngineMaterial(shadersResources.samplingPS);
            return new CopyHistoryColorPass(context, samplingMaterial, blitMaterial);
        }
        
        private readonly PostProcessFeatureContext m_Context;
        private readonly Material m_BlitMaterial;
        private readonly Material m_SamplingMaterial;
        
        public CopyHistoryColorPass(PostProcessFeatureContext context, Material samplingMaterial, Material copyColorMaterial = null) 
            : base(RenderPassEvent.BeforeRenderingPostProcessing - 1, samplingMaterial, copyColorMaterial, "CopyHistoryColorPass")
        {
            m_Context = context;
            m_SamplingMaterial = samplingMaterial;
            m_BlitMaterial = copyColorMaterial;
        }
        
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            ConfigureDescriptor(Downsampling.None, ref descriptor, out var filterMode);
            var postProcessCamera = m_Context.GetPostProcessCamera(renderingData.cameraData.camera);
            if (postProcessCamera == null)
            {
                return;
            }

            RenderingUtils.ReAllocateHandleIfNeeded(ref postProcessCamera.CameraPreviousColorTextureRT, descriptor, filterMode, TextureWrapMode.Clamp, name: "_CameraPreviousColorTexture");
            ConfigureTarget(postProcessCamera.CameraPreviousColorTextureRT);
            ConfigureClear(ClearFlag.Color, Color.clear);
            Setup(renderingData.cameraData.renderer.cameraColorTargetHandle, postProcessCamera.CameraPreviousColorTextureRT, Downsampling.None);
            base.OnCameraSetup(cmd, ref renderingData);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var postProcessCamera = m_Context.GetPostProcessCamera(renderingData.cameraData.camera);
            if (postProcessCamera == null)
            {
                return;
            }
            base.Execute(context, ref renderingData);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_BlitMaterial);
            CoreUtils.Destroy(m_SamplingMaterial);
        }
    }
}