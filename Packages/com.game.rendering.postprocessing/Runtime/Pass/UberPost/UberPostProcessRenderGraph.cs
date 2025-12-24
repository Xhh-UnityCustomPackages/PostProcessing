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
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalPostProcessingData postProcessingData = frameData.Get<UniversalPostProcessingData>();
            
            var stack = VolumeManager.instance.stack;
            
            m_Tonemapping = stack.GetComponent<Tonemapping>();
            m_Vignette = stack.GetComponent<Vignette>();

            Render(renderGraph);
        }

        void Render(RenderGraph renderGraph)
        {
            if (m_Material == null)
                m_Material = Material.Instantiate(m_PostProcessFeatureData.materials.UberPost);
            
            TonemappingRenderer.ExecutePass(m_Material, m_Tonemapping);
            VignetteRenderer.ExecutePass(m_Descriptor, m_Material, m_Vignette);

            using (var builder = renderGraph.AddRasterRenderPass<UberPostPassData>("Blit Post Processing", out var passData, m_ProfilingRenderPostProcessing))
            {
                passData.material = m_Material;

                builder.SetRenderFunc(static (UberPostPassData data, RasterGraphContext context) =>
                { 
                    var cmd = context.cmd;
                    
                    // Vector4 scaleBias = RenderingUtils.GetFinalBlitScaleBias(sourceTextureHdl, dest, cameraData);
                    // Blitter.BlitTexture(cmd, sourceTextureHdl, scaleBias, data.material, 0);
                    // Blitter.BlitCameraTexture(cmd, tempTextureHdl, destination, data.material, 0);
                });
            }
        }
    }
}