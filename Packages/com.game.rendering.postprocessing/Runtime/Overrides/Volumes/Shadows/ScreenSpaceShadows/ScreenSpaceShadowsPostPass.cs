using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ScreenSpaceShadowsPostPass : ScriptableRenderPass
    {
        private static readonly RTHandle k_CurrentActive = RTHandles.Alloc(BuiltinRenderTextureType.CurrentActive);

            internal ScreenSpaceShadowsPostPass()
            {
                profilingSampler = new ProfilingSampler("Set Screen Space Shadow Keywords");
            }

          
            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                // Disable obsolete warning for internal usage
                #pragma warning disable CS0618
                ConfigureTarget(k_CurrentActive);
                #pragma warning restore CS0618
            }

            private static void ExecutePass(RasterCommandBuffer cmd, UniversalShadowData shadowData)
            {
                int cascadesCount = shadowData.mainLightShadowCascadesCount;
                bool mainLightShadows = shadowData.supportsMainLightShadows;
                bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
                bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

                // Before transparent object pass, force to disable screen space shadow of main light
                cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowScreen, false);

                // then enable main light shadows with or without cascades
                cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadows, receiveShadowsNoCascade);
                cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowCascades, receiveShadowsCascades);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = renderingData.commandBuffer;
                UniversalShadowData shadowData = renderingData.frameData.Get<UniversalShadowData>();

                using (new ProfilingScope(cmd, profilingSampler))
                {
                    ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(renderingData.commandBuffer), shadowData);
                }
            }

            internal class PassData
            {
                internal ScreenSpaceShadowsPostPass pass;
                internal UniversalShadowData shadowData;
            }
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
                {
                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                    TextureHandle color = resourceData.activeColorTexture;
                    builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                    passData.shadowData = frameData.Get<UniversalShadowData>();
                    passData.pass = this;

                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
                    {
                        ExecutePass(rgContext.cmd, data.shadowData);
                    });
                }
            }
    }
}