using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class PostProcessingDebugPass : ScriptableRenderPass
    {
        static readonly int k_DebugTextureNoStereoPropertyId = Shader.PropertyToID("_DebugTextureNoStereo");
        static readonly int k_DebugTextureDisplayRect = Shader.PropertyToID("_DebugTextureDisplayRect");
        static readonly int k_DebugRenderTargetSupportsStereo = Shader.PropertyToID("_DebugRenderTargetSupportsStereo");

        bool m_HasDebugRenderTarget;
        Vector4 m_DebugRenderTargetPixelRect;
        RTHandle m_DebugRenderTargetIdentifier;


        DebugHandler m_DebugHandler;

        public PostProcessingDebugPass(DebugHandler debugHandler)
        {
            m_DebugHandler = debugHandler;
            renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Setup(ref renderingData.cameraData);

            if (!m_HasDebugRenderTarget)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();

            cmd.EnableShaderKeyword(ShaderKeywordStrings.DEBUG_DISPLAY);
            cmd.SetGlobalTexture(k_DebugTextureNoStereoPropertyId, m_DebugRenderTargetIdentifier);
            cmd.SetGlobalVector(k_DebugTextureDisplayRect, m_DebugRenderTargetPixelRect);
            cmd.SetGlobalInteger(k_DebugRenderTargetSupportsStereo, 0);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }



        private void Setup(ref CameraData cameraData)
        {
            if (m_DebugHandler.TryGetFullscreenDebugMode(out DebugFullScreenMode fullScreenDebugMode, out int textureHeightPercent))
            {
                Camera camera = cameraData.camera;
                float screenWidth = camera.pixelWidth;
                float screenHeight = camera.pixelHeight;

                var relativeSize = Mathf.Clamp01(textureHeightPercent / 100f);
                var height = relativeSize * screenHeight;
                var width = relativeSize * screenWidth;
                float normalizedSizeX = width / screenWidth;
                float normalizedSizeY = height / screenHeight;
                Rect normalizedRect = new Rect(1 - normalizedSizeX, 1 - normalizedSizeY, normalizedSizeX, normalizedSizeY);

                switch (fullScreenDebugMode)
                {
                    case DebugFullScreenMode.HiZ:
                        {
                            SetDebugRenderTarget(PyramidDepthGenerator.HiZDepthRT, normalizedRect);
                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            }
            else
            {
                ResetDebugRenderTarget();
            }
        }



        internal void SetDebugRenderTarget(RTHandle renderTargetIdentifier, Rect displayRect)
        {
            if (renderTargetIdentifier == null)
                return;
            m_HasDebugRenderTarget = true;
            m_DebugRenderTargetIdentifier = renderTargetIdentifier;
            m_DebugRenderTargetPixelRect = new Vector4(displayRect.x, displayRect.y, displayRect.width, displayRect.height);
        }

        internal void ResetDebugRenderTarget()
        {
            m_HasDebugRenderTarget = false;
        }
    }
}
