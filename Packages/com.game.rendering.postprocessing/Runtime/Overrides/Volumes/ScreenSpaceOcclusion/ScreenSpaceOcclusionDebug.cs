using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ScreenSpaceOcclusionDebug : ScriptableRenderPass
    {
        public RTHandle finalRT { get; set; }

        public ScreenSpaceOcclusionDebug()
        {
            this.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
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

    }
}
