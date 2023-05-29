using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ScreenSpaceOcclusionDebug : ScriptableRenderPass
    {
        RTHandle m_SourceRT;

        public ScreenSpaceOcclusionDebug(RTHandle target)
        {
            this.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            m_SourceRT = target;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_SourceRT == null)
                return;
            var cmd = CommandBufferPool.Get(nameof(ScreenSpaceOcclusionDebug));
            cmd.Clear();

            Blit(cmd, m_SourceRT, renderingData.cameraData.renderer.cameraColorTargetHandle);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

    }
}
