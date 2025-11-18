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
        Material m_Material;
        RTHandle m_DebugTargetHandle;

        public PostProcessingDebugPass(DebugHandler debugHandler)
        {
            m_DebugHandler = debugHandler;
            renderPassEvent = RenderPassEvent.AfterRendering;
            m_Material = CoreUtils.CreateEngineMaterial("Hidden/PostProcessing/PostProcessingDebugPass");
        }

        public void Dispose()
        {
            m_DebugTargetHandle?.Release();
            CoreUtils.Destroy(m_Material);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_DebugTargetHandle, desc, name: "_PostProcessingDebugTarget");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Material == null)
                return;

            Setup(ref renderingData.cameraData);

            if (!m_HasDebugRenderTarget)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("PostProcessingDebug");

            cmd.EnableShaderKeyword("POSTPROCESS_DEBUG_DISPLAY");//和URP内置的DEBUG_DISPLAY 区别开来
            cmd.SetGlobalTexture(k_DebugTextureNoStereoPropertyId, m_DebugRenderTargetIdentifier);
            cmd.SetGlobalVector(k_DebugTextureDisplayRect, m_DebugRenderTargetPixelRect);
            cmd.SetGlobalInteger(k_DebugRenderTargetSupportsStereo, 0);

            m_Material.SetInt("_DebugFullScreenMode", (int)m_DebugHandler.PostProcessingSetting.fullScreenDebugMode);
            m_Material.SetInt("_HiZMipMapLevel", m_DebugHandler.PostProcessingSetting.hiZMipmapLevel);

            var target = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Blitter.BlitCameraTexture(cmd, target, m_DebugTargetHandle, m_Material, 0);
            cmd.Blit(m_DebugTargetHandle, target);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.DisableShaderKeyword("POSTPROCESS_DEBUG_DISPLAY");
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
                            ResetDebugRenderTarget();
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
