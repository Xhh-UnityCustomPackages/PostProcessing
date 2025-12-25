using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class UberPostProcess : ScriptableRenderPass
    {
        private class UberPostPassData
        {
            internal TextureHandle destinationTexture;
            internal TextureHandle sourceTexture;
            internal Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) 
        {
            
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalPostProcessingData postProcessingData = frameData.Get<UniversalPostProcessingData>();
            
            var stack = VolumeManager.instance.stack;
            
            m_Tonemapping = stack.GetComponent<Tonemapping>();
            m_Vignette = stack.GetComponent<Vignette>();

            m_Descriptor = cameraData.cameraTargetDescriptor;
            m_Descriptor.useMipMap = false;
            m_Descriptor.autoGenerateMips = false;
            
            Render(renderGraph, frameData);
        }

        void Render(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null)
                m_Material = Material.Instantiate(m_PostProcessFeatureData.materials.UberPost);
            
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            
            TextureHandle activeColor = resourceData.activeColorTexture;
            
            TonemappingRenderer.ExecutePass(m_Material, m_Tonemapping);
            VignetteRenderer.ExecutePass(m_Descriptor, m_Material, m_Vignette);

           
            var targetHdl = UniversalRenderer.CreateRenderGraphTexture(renderGraph,  GetCompatibleDescriptor(), "_TempTarget", false);

            
            using (var builder = renderGraph.AddUnsafePass<UberPostPassData>("Blit Post Processing", out var passData, m_ProfilingRenderPostProcessing))
            {
                builder.AllowPassCulling(false);
                
                passData.material = m_Material;
                passData.sourceTexture = activeColor;
                builder.UseTexture(activeColor, AccessFlags.Read);
                //
                passData.destinationTexture = targetHdl;
                builder.UseTexture(targetHdl, AccessFlags.ReadWrite);

                builder.SetRenderFunc(static (UberPostPassData data, UnsafeGraphContext context) =>
                { 
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    
                    var sourceTextureHdl = data.sourceTexture;
                    var tempHdl = data.destinationTexture;
                    Blitter.BlitCameraTexture(cmd, sourceTextureHdl, tempHdl, data.material, 0);
                    Blitter.BlitCameraTexture(cmd, tempHdl, sourceTextureHdl);
                });
            }
        }
    }
}