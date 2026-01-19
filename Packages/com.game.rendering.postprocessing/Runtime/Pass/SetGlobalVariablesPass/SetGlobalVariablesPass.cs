using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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

        
        private class SetGlobalVariablesPassData
        {
            internal PostProcessData RendererData;
            internal UniversalCameraData CameraData;
            internal UniversalLightData LightData;
            // internal TextureHandle ActiveColor;
            // internal TextureHandle PreviousFrameColor;
            // internal TextureHandle MotionVectorColor;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            using (var builder = renderGraph.AddUnsafePass<SetGlobalVariablesPassData>("Set Global Variables", out var passData, profilingSampler))
            {
                // var resource = frameData.Get<UniversalResourceData>();
                // TextureHandle cameraColor = resource.activeColorTexture;
                // builder.UseTexture(cameraColor);
                // passData.ActiveColor = cameraColor;
                
                passData.RendererData = m_Data;
                passData.CameraData = cameraData;
                passData.LightData = lightData;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SetGlobalVariablesPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    // bool yFlip = RenderingUtils.IsHandleYFlipped(context, in data.ActiveColor);
                    data.RendererData.PushGlobalBuffers(cmd, data.CameraData, data.LightData);
                    // context.cmd.SetGlobalTexture(IllusionShaderProperties._HistoryColorTexture, data.PreviousFrameColor);
                    // context.cmd.SetGlobalTexture(IllusionShaderProperties._MotionVectorTexture, data.MotionVectorColor);
                    // data.RendererData.BindAmbientProbe(context.cmd);
                });
            }
        }
    }
}