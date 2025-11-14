using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


namespace Game.Core.PostProcessing
{
    public class PostProcessRenderPass : ScriptableRenderPass
    {
        List<PostProcessRenderer> m_PostProcessRenderers;
        List<PostProcessRenderer> m_ActivePostProcessRenderers;
        string m_PassName;

        PostProcessFeatureData m_PostProcessFeatureData;

        static RTHandle m_TempRT0;
        static RTHandle m_TempRT1;

        public PostProcessRenderPass(PostProcessInjectionPoint injectionPoint, List<PostProcessRenderer> renderers, PostProcessFeatureData data)
        {
            m_PostProcessRenderers = renderers;
            m_PostProcessFeatureData = data;

            foreach (var renderer in m_PostProcessRenderers)
            {
                renderer.InitProfilingSampler();
            }

            m_ActivePostProcessRenderers = new List<PostProcessRenderer>();

            switch (injectionPoint)
            {
                case PostProcessInjectionPoint.BeforeRenderingGBuffer:
                    renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer;
                    m_PassName = "PostProcessRenderPass BeforeRenderingGBuffer";
                    break;
                case PostProcessInjectionPoint.BeforeRenderingDeferredLights:
                    renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights;
                    m_PassName = "PostProcessRenderPass BeforeRenderingDeferredLights";
                    break;
                case PostProcessInjectionPoint.BeforeRenderingOpaques:
                    renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                    m_PassName = "PostProcessRenderPass BeforeRenderingOpaques";
                    break;
                case PostProcessInjectionPoint.AfterRenderingOpaques:
                    renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
                    m_PassName = "PostProcessRenderPass AfterRenderingOpaques";
                    break;
                case PostProcessInjectionPoint.AfterRenderingSkybox:
                    // +1 为了放在MotionVector后面
                    renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1;
                    m_PassName = "PostProcessRenderPass AfterRenderingSkybox";
                    break;
                case PostProcessInjectionPoint.BeforeRenderingPostProcessing:
                    renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                    m_PassName = "PostProcessRenderPass BeforeRenderingPostProcessing";
                    break;
                case PostProcessInjectionPoint.AfterRenderingPostProcessing:
                    renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                    m_PassName = "PostProcessRenderPass AfterRenderingPostProcessing";
                    break;
            }
        }

        public void AddRenderPasses(ref RenderingData renderingData)
        {
            if (!Setup(ref renderingData))
                return;

            renderingData.cameraData.renderer.EnqueuePass(this);
        }

        private bool RenderInit(bool isSceneView,
                               ref PostProcessRenderer postProcessRenderer,
                               ref ScriptableRenderPassInput passInput,
                               ref RenderingData renderingData)
        {
            if (isSceneView && !postProcessRenderer.visibleInSceneView) return false;

            if (postProcessRenderer.IsActive(ref renderingData))
            {
                postProcessRenderer.SetupInternal(this, m_PostProcessFeatureData);
                postProcessRenderer.AddRenderPasses(ref renderingData);

                m_ActivePostProcessRenderers.Add(postProcessRenderer);
                passInput |= postProcessRenderer.input;
            }
            postProcessRenderer.ShowHideInternal(ref renderingData);

            return true;
        }

        public bool Setup(ref RenderingData renderingData)
        {
            bool isSceneView = renderingData.cameraData.isSceneViewCamera;
            // TODO isPreviewCamera

            ScriptableRenderPassInput passInput = ScriptableRenderPassInput.None;

            m_ActivePostProcessRenderers.Clear();

            for (int index = 0; index < m_PostProcessRenderers.Count; index++)
            {
                var postProcessRenderer = m_PostProcessRenderers[index];
                //
                if (!RenderInit(isSceneView, ref postProcessRenderer, ref passInput, ref renderingData))
                    continue;
            }

            // 放在外部先
            ConfigureInput(passInput);

            return m_ActivePostProcessRenderers.Count != 0;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            for (int index = 0; index < m_ActivePostProcessRenderers.Count; index++)
            {
                m_ActivePostProcessRenderers[index].OnCameraSetup(cmd, ref renderingData);
            }

            RenderTextureDescriptor sourceDesc = renderingData.cameraData.cameraTargetDescriptor;
            sourceDesc.msaaSamples = 1;
            sourceDesc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref m_TempRT0, sourceDesc, name: "_TempRT0");
            RenderingUtils.ReAllocateIfNeeded(ref m_TempRT1, sourceDesc, name: "_TempRT1");
        }

        // 在OnCameraSetup之后Execute之前，暂时先不放在这个阶段
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) { }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get(m_PassName);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RTHandle source = cameraColorTarget;
            RTHandle target = m_TempRT0;

            for (int index = 0; index < m_ActivePostProcessRenderers.Count; ++index)
            {
                var renderer = m_ActivePostProcessRenderers[index];

                if (!renderer.renderToCamera)
                {
                    // 不需要渲染到最终摄像机 就无所谓RT切换 (注意: 最终输出完全取决于内部 如果在队列最后一个 可能会导致RT没能切回摄像机)
                    using (new ProfilingScope(cmd, renderer.profilingSampler))
                    {
                        renderer.Render(cmd, source, null, ref renderingData);
                    }
                    continue;
                }

                // --------------------------------------------------------------------------
                if (index == m_ActivePostProcessRenderers.Count - 1)
                {
                    // 最后一个 target 正常必须是 m_CameraColorTarget
                    // 如果 source == m_CameraColorTarget 则需要把 m_CameraColorTarget copyto RT
                    if (source == cameraColorTarget && !renderer.dontCareSourceTargetCopy)
                    {
                        // blit source: m_CameraColorTarget target: m_TempRT
                        // copy
                        // swap source: m_TempRT target: m_CameraColorTarget
                        Blit(cmd, source, target);
                        CoreUtils.Swap(ref source, ref target);
                    }
                    target = cameraColorTarget;
                }
                else
                {
                    // 不是最后一个时 如果 target == m_CameraColorTarget 就改成非souce的那个RT
                    // source: lastRT target: nextRT
                    if (target == cameraColorTarget)
                    {
                        target = source == m_TempRT0 ? m_TempRT1 : m_TempRT0;
                    }
                }

                using (new ProfilingScope(cmd, renderer.profilingSampler))
                {
                    renderer.Render(cmd, source, target, ref renderingData);
                    CoreUtils.Swap(ref source, ref target);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            //
            for (int index = 0; index < m_ActivePostProcessRenderers.Count; index++)
            {
                m_ActivePostProcessRenderers[index].OnCameraCleanup(cmd);
            }
        }

        public void Dispose(bool disposing)
        {
            for (int index = 0; index < m_PostProcessRenderers.Count; index++)
            {
                m_PostProcessRenderers[index].Dispose(disposing);
            }

            m_TempRT0?.Release();
            m_TempRT1?.Release();
        }

    }
}
