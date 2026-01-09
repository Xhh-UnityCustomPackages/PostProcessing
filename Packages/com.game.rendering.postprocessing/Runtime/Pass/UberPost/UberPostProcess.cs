using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;


namespace Game.Core.PostProcessing
{
    public partial class UberPostProcess : ScriptableRenderPass
    {
        private static readonly ProfilingSampler m_ProfilingRenderPostProcessing = new ProfilingSampler("PostProcessRenderPass Uber Post Process");

        RenderTextureDescriptor m_Descriptor;
        Material m_Material;
        PostProcessFeatureData m_PostProcessFeatureData;
        RTHandle m_CameraTargetHandle;


        private Tonemapping m_Tonemapping;
        private Vignette m_Vignette;

        public UberPostProcess(PostProcessFeatureData PostProcessFeatureData)
        {
            m_PostProcessFeatureData = PostProcessFeatureData;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var stack = VolumeManager.instance.stack;
            m_Tonemapping = stack.GetComponent<Tonemapping>();
            m_Vignette = stack.GetComponent<Vignette>();

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingRenderPostProcessing))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                m_Descriptor = renderingData.cameraData.cameraTargetDescriptor;
                m_Descriptor.useMipMap = false;
                m_Descriptor.autoGenerateMips = false;

                Render(cmd, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        void Render(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (m_Material == null)
                m_Material = Material.Instantiate(m_PostProcessFeatureData.materials.UberPost);

            ref CameraData cameraData = ref renderingData.cameraData;
            ref ScriptableRenderer renderer = ref cameraData.renderer;
            var source = renderer.cameraColorTargetHandle;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_CameraTargetHandle, GetCompatibleDescriptor(), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempTarget");

            TonemappingRenderer.ExecutePass(m_Material, m_Tonemapping);
            VignetteRenderer.ExecutePass(m_Descriptor, m_Material, m_Vignette);

            Blitter.BlitCameraTexture(cmd, source, m_CameraTargetHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, m_Material, 0);

            Blit(cmd, m_CameraTargetHandle, source);
        }


        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture("_AutoExposureLUT", Texture2D.redTexture);
        }


        public void Dispose()
        {
            CoreUtils.Destroy(m_Material);

            m_CameraTargetHandle?.Release();
        }


        RenderTextureDescriptor GetCompatibleDescriptor()
          => GetCompatibleDescriptor(m_Descriptor.width, m_Descriptor.height, m_Descriptor.graphicsFormat);

        RenderTextureDescriptor GetCompatibleDescriptor(int width, int height, GraphicsFormat format, DepthBits depthBufferBits = DepthBits.None)
            => GetCompatibleDescriptor(m_Descriptor, width, height, format, depthBufferBits);

        internal static RenderTextureDescriptor GetCompatibleDescriptor(RenderTextureDescriptor desc, int width, int height, GraphicsFormat format, DepthBits depthBufferBits = DepthBits.None)
        {
            desc.depthBufferBits = (int)depthBufferBits;
            desc.msaaSamples = 1;
            desc.width = width;
            desc.height = height;
            desc.graphicsFormat = format;
            return desc;
        }
    }
}
