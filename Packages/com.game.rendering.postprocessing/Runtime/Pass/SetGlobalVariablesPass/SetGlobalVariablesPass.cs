using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class SetGlobalVariablesPass : ScriptableRenderPass
    {
        private PostProcessData m_Data;
        
        public SetGlobalVariablesPass(PostProcessData rendererData)
        {
            m_Data = rendererData;
            renderPassEvent = PostProcessingRenderPassEvent.SetGlobalVariablesPass;
            profilingSampler = new ProfilingSampler("Set Global Variables");
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                m_Data.PushGlobalBuffers(cmd, ref renderingData);
                // m_Context.BindHistoryColor(cmd, renderingData);
                // m_Context.BindAmbientProbe(cmd);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}