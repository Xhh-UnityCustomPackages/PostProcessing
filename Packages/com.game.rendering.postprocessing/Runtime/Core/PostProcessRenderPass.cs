using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


namespace Game.Core.PostProcessing
{
    public partial class PostProcessRenderPass : ScriptableRenderPass
    {
        private readonly List<PostProcessRenderer> m_PostProcessRenderers;
        private readonly List<PostProcessRenderer> m_ActivePostProcessRenderers;
        string m_PassName;

        PostProcessFeatureData m_PostProcessFeatureData;
        PostProcessData m_Data;

        static RTHandle m_TempRT0;
        static RTHandle m_TempRT1;

        public PostProcessRenderPass(PostProcessInjectionPoint injectionPoint, List<PostProcessRenderer> renderers, PostProcessFeatureData resource, PostProcessData data)
        {
            m_PostProcessRenderers = renderers;
            m_PostProcessFeatureData = resource;
            m_Data = data;

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
                    renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing - 1;//这里-1 是为了适配RenderGraph
                    m_PassName = "PostProcessRenderPass AfterRenderingPostProcessing";
                    break;
            }
        }

        public void AddRenderPasses(ref RenderingData renderingData, ref PostProcessPassInput postProcessPassInput)
        {
            if (!Setup(ref renderingData, ref postProcessPassInput)) return;

            renderingData.cameraData.renderer.EnqueuePass(this);
        }

        private bool RenderInit(bool isSceneView,
                               ref PostProcessRenderer postProcessRenderer,
                               ref ScriptableRenderPassInput passInput,
                               ref PostProcessPassInput postProcessPassInput,
                               ref RenderingData renderingData)
        {
            if (isSceneView && !postProcessRenderer.visibleInSceneView) return false;

            if (!CheckRenderingMode(postProcessRenderer)) return false;
            
            if (postProcessRenderer.IsActive(ref renderingData))
            {
                postProcessRenderer.SetupInternal(this, ref renderingData, m_PostProcessFeatureData, m_Data);
                postProcessRenderer.AddRenderPasses(ref renderingData);

                m_ActivePostProcessRenderers.Add(postProcessRenderer);
                passInput |= postProcessRenderer.input;
                postProcessPassInput |= postProcessRenderer.postProcessPassInput;
            }
            postProcessRenderer.ShowHideInternal(ref renderingData);

            return true;
        }

        bool CheckRenderingMode(PostProcessRenderer postProcessRenderer)
        {
            var renderingMode = m_Data.renderingMode;
            var supportRenderPath = postProcessRenderer.supportRenderPath;
            // 检查是否支持当前 RenderingMode
            if (renderingMode == RenderingMode.Deferred && (supportRenderPath & SupportRenderPath.Deferred) == 0) return false;
            if (renderingMode == RenderingMode.Forward && (supportRenderPath & SupportRenderPath.Forward) == 0) return false;
            if (renderingMode == RenderingMode.ForwardPlus && (supportRenderPath & SupportRenderPath.Forward) == 0) return false;

            return true;
        }

        private bool Setup(ref RenderingData renderingData, ref PostProcessPassInput postProcessPassInput)
        {
            bool isSceneView = renderingData.cameraData.isSceneViewCamera;
            // TODO isPreviewCamera

            ScriptableRenderPassInput passInput = ScriptableRenderPassInput.None;

            m_ActivePostProcessRenderers.Clear();

            for (int index = 0; index < m_PostProcessRenderers.Count; index++)
            {
                var postProcessRenderer = m_PostProcessRenderers[index];
                //
                if (!RenderInit(isSceneView, ref postProcessRenderer, ref passInput, ref postProcessPassInput, ref renderingData))
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

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TempRT0, sourceDesc, name: "_TempRT0");
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_TempRT1, sourceDesc, name: "_TempRT1");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get(m_PassName);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RTHandle source = cameraColorTarget;
            RTHandle target = m_TempRT0;

            int lastCameraIndex = -1;
            for (int i = 0; i < m_ActivePostProcessRenderers.Count; ++i)
            {
                if (m_ActivePostProcessRenderers[i].renderToCamera)
                    lastCameraIndex = i;
            }

            for (int index = 0; index < m_ActivePostProcessRenderers.Count; ++index)
            {
                var renderer = m_ActivePostProcessRenderers[index];

                if (!renderer.renderToCamera)
                {
                    using (new ProfilingScope(cmd, renderer.profilingSampler))
                    {
                        renderer.Render(cmd, source, null, ref renderingData);
                    }

                    continue;
                }

                bool isLastCamera = index == lastCameraIndex;
                if (isLastCamera)
                {
                    if (source == cameraColorTarget && !renderer.dontCareSourceTargetCopy)
                    {
                        Blit(cmd, source, target);
                        CoreUtils.Swap(ref source, ref target);
                    }
                    target = cameraColorTarget;
                }
                else
                {
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
