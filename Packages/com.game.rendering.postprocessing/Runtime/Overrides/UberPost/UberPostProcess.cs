using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;


namespace Game.Core.PostProcessing
{
    public class UberPostProcess : ScriptableRenderPass
    {
        private static readonly ProfilingSampler m_ProfilingRenderPostProcessing = new ProfilingSampler("Uber Post Process");

        RenderTextureDescriptor m_Descriptor;
        Material m_Material;
        PostProcessFeatureData m_PostProcessFeatureData;
        RTHandle m_CameraTargetHandle;


        Tonemapping m_Tonemapping;

        public UberPostProcess(PostProcessFeatureData PostProcessFeatureData)
        {
            m_PostProcessFeatureData = PostProcessFeatureData;
            m_Material = Material.Instantiate(m_PostProcessFeatureData.materials.UberPost);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var stack = VolumeManager.instance.stack;
            m_Tonemapping = stack.GetComponent<Tonemapping>();

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
            ref CameraData cameraData = ref renderingData.cameraData;
            ref ScriptableRenderer renderer = ref cameraData.renderer;
            var source = renderer.cameraColorTargetHandle;

            RenderingUtils.ReAllocateIfNeeded(ref m_CameraTargetHandle, GetCompatibleDescriptor(), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempTarget");

            TonemappingRenderer.ExecutePass(cmd, m_Material, m_Tonemapping);

            Blitter.BlitCameraTexture(cmd, source, m_CameraTargetHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, m_Material, 0);

            Blit(cmd, m_CameraTargetHandle, source);
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
