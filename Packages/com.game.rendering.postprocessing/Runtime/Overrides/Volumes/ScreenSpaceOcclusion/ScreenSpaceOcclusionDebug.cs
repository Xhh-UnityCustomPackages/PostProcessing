using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ScreenSpaceOcclusionDebug : ScriptableRenderPass
    {
        public RTHandle finalRT { get; set; }

        public ScreenSpaceOcclusionDebug()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (finalRT == null)
                return;
            var cmd = CommandBufferPool.Get(nameof(ScreenSpaceOcclusionDebug));
            cmd.Clear();

            Blit(cmd, finalRT, renderingData.cameraData.renderer.cameraColorTargetHandle);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        // Create the custom data class that contains the new texture
        public class ScreenSpaceOcclusionDebugData : ContextItem
        {
            public TextureHandle occlusionFinalTexture;

            public override void Reset()
            {
                occlusionFinalTexture = TextureHandle.nullHandle;
            }
        }
        
        public class ScreenSpaceOcclusionDebugPassData
        {
            // Inputs
            internal TextureHandle occlusionFinalTexture;
            internal TextureHandle sourceTexture;
        }
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            
            using (var builder = renderGraph.AddUnsafePass<ScreenSpaceOcclusionDebugPassData>(profilingSampler.name, out var passData))
            {
                var customData = frameData.Get<ScreenSpaceOcclusionDebugData>();
                
                passData.occlusionFinalTexture = customData.occlusionFinalTexture;
                builder.UseTexture(customData.occlusionFinalTexture, AccessFlags.Read);
                
                passData.sourceTexture = resourceData.activeColorTexture;
                builder.UseTexture(passData.sourceTexture, AccessFlags.ReadWrite);
                
                builder.SetRenderFunc(static (ScreenSpaceOcclusionDebugPassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(ScreenSpaceOcclusionDebugPassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            RTHandle sourceTextureHdl = data.sourceTexture;
            RTHandle occlusionFinalHdl = data.occlusionFinalTexture;
            Blitter.BlitCameraTexture(cmd, occlusionFinalHdl, sourceTextureHdl);
        }

    }
}
