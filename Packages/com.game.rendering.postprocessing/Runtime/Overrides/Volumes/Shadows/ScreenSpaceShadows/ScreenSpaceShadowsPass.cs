using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ScreenSpaceShadowsPass : ScriptableRenderPass, IDisposable
    {
         // Private Variables
         private Material m_Material;
         private ScreenSpaceShadowsSettings m_CurrentSettings;
         private RTHandle m_RenderTarget;
         private int m_ScreenSpaceShadowmapTextureID;
         private PassData m_PassData;

         internal ScreenSpaceShadowsPass()
         {
             profilingSampler = new ProfilingSampler("Blit Screen Space Shadows");
             m_CurrentSettings = new ScreenSpaceShadowsSettings();
             m_ScreenSpaceShadowmapTextureID = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
             m_PassData = new PassData();
         }

         public void Dispose()
         {
             m_RenderTarget?.Release();
         }

         internal bool Setup()
         {
             const string k_ShaderName = "Hidden/Universal Render Pipeline/ScreenSpaceShadows Modify";
             if (m_Material != null)
             {
                 return true;
             }


             var m_Shader = Shader.Find(k_ShaderName);
             if (m_Shader == null)
             {
                 return false;
             }
             
             m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
             ConfigureInput(ScriptableRenderPassInput.Depth);
             return m_Material != null;
         }

         internal bool Setup(ScreenSpaceShadowsSettings featureSettings, Material material)
         {
             m_CurrentSettings = featureSettings;
             m_Material = material;
             ConfigureInput(ScriptableRenderPassInput.Depth);

             return m_Material != null;
         }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1;
            // UUM-41070: We require `Linear | Render` but with the deprecated FormatUsage this was checking `Blend`
            // For now, we keep checking for `Blend` until the performance hit of doing the correct checks is evaluated
            desc.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_RenderTarget, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceShadowmapTexture");
            cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

            // Disable obsolete warning for internal usage
#pragma warning disable CS0618
            ConfigureTarget(m_RenderTarget);
            ConfigureClear(ClearFlag.None, Color.white);
#pragma warning restore CS0618
        }

         private class PassData
         {
             internal TextureHandle target;
             internal Material material;
             internal int shadowmapID;
         }

         /// <summary>
         /// Initialize the shared pass data.
         /// </summary>
         /// <param name="passData"></param>
         private void InitPassData(ref PassData passData)
         {
             passData.material = m_Material;
             passData.shadowmapID = m_ScreenSpaceShadowmapTextureID;
         }

         public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
         {
             if (m_Material == null)
             {
                 Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceShadows pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                 return;
             }

             UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
             var desc = cameraData.cameraTargetDescriptor;
             desc.depthStencilFormat = GraphicsFormat.None;
             desc.msaaSamples = 1;
             // UUM-41070: We require `Linear | Render` but with the deprecated FormatUsage this was checking `Blend`
             // For now, we keep checking for `Blend` until the performance hit of doing the correct checks is evaluated
             desc.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                 ? GraphicsFormat.R8_UNorm
                 : GraphicsFormat.B8G8R8A8_UNorm;
             TextureHandle color = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceShadowmapTexture", true);

             using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
             {
                 passData.target = color;
                 builder.SetRenderAttachment(color, 0, AccessFlags.Write);

                 InitPassData(ref passData);
                 builder.AllowGlobalStateModification(true);

                 if (color.IsValid())
                     builder.SetGlobalTextureAfterPass(color, m_ScreenSpaceShadowmapTextureID);

                 builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => { ExecutePass(rgContext.cmd, data, data.target); });
             }
         }

         private static void ExecutePass(RasterCommandBuffer cmd, PassData data, RTHandle target)
         {
             // 处理屏幕空间阴影
             // Bind ContactShadow
             var contactShadowParam = VolumeManager.instance.stack.GetComponent<ContactShadows>();
             {
                 // var contactShadowRT = contactShadowParam.shadowDenoiser.value == ContactShadow.ShadowDenoiser.Spatial
                 //     ? _rendererData.ContactShadowsDenoisedRT
                 //     : _rendererData.ContactShadowsRT;
                 // // data.material.SetTexture();
             }
             CoreUtils.SetKeyword(cmd, PipelineKeywords._CONTACT_SHADOWS, contactShadowParam.enable.value);
             
             Blitter.BlitTexture(cmd, target, Vector2.one, data.material, 0);
             cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadows, false);
             cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowCascades, false);
             cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowScreen, true);
         }

         /// <inheritdoc/>
         public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
         {
             if (m_Material == null)
             {
                 Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceShadows pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                 return;
             }

             InitPassData(ref m_PassData);
             var cmd = renderingData.commandBuffer;
             using (new ProfilingScope(cmd, profilingSampler))
             {
                 ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(renderingData.commandBuffer), m_PassData, m_RenderTarget);
             }
         }
    }
}