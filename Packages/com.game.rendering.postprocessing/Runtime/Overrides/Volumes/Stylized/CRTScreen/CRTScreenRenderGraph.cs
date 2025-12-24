using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Game.Core.PostProcessing
{
    public partial class CRTScreenRenderer : PostProcessVolumeRenderer<CRTScreen>
    {
        private class CRTScreenPassData
        {
            public Material material;
            internal TextureHandle sourceTexture;
            internal TextureHandle targetTexture;
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            if (m_Material == null)
                return;

            SetupMaterials(m_Material);

            using (var builder = renderGraph.AddUnsafePass<CRTScreenPassData>(profilingSampler.name, out var passData))
            {
                passData.material = m_Material;
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);

                passData.targetTexture = destination;
                builder.UseTexture(destination, AccessFlags.Write);
                
                builder.SetRenderFunc(static (CRTScreenPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    
                    RTHandle sourceTextureHdl = data.sourceTexture;
                    RTHandle targetTextureHdl = data.targetTexture;
                    
                    Blitter.BlitCameraTexture(cmd, sourceTextureHdl, targetTextureHdl, data.material, 0);
                });
            }
        }
    }
}
