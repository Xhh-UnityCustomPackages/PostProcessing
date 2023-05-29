using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class TemporalAntialiasingCamera : ScriptableRenderPass
    {
        ProfilingSampler m_ProfilingSampler = new ProfilingSampler(nameof(TemporalAntialiasingCamera));

        Matrix4x4 m_JitteredProjectionMatrix;
        int m_TAAFrameIndex = 0;

        internal static readonly int TAA_FRAME_INDEX = Shader.PropertyToID("_TAAFrameIndex");

        public TemporalAntialiasingCamera()
        {
            this.renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer;
        }

        public void Setup(Matrix4x4 projectionMatrix, int taaFrameIndex)
        {
            m_JitteredProjectionMatrix = projectionMatrix;
            m_TAAFrameIndex = taaFrameIndex;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                cmd.SetGlobalFloat(TAA_FRAME_INDEX, m_TAAFrameIndex);
                cmd.SetViewProjectionMatrices(renderingData.cameraData.camera.worldToCameraMatrix, m_JitteredProjectionMatrix);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}