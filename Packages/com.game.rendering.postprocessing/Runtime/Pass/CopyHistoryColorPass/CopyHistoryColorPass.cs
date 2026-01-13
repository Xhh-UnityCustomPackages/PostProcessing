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
            var blitMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal/CoreBlit");
            var samplingMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal Render Pipeline/Sampling");
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
            RenderingUtils.ReAllocateIfNeeded(ref m_Context.CameraPreviousColorTextureRT, descriptor, filterMode, TextureWrapMode.Clamp, name: "_CameraPreviousColorTexture");
            ConfigureTarget(m_Context.CameraPreviousColorTextureRT);
            ConfigureClear(ClearFlag.Color, Color.clear);
            Setup(renderingData.cameraData.renderer.cameraColorTargetHandle, m_Context.CameraPreviousColorTextureRT, Downsampling.None);
            base.OnCameraSetup(cmd, ref renderingData);
        }
    }
}