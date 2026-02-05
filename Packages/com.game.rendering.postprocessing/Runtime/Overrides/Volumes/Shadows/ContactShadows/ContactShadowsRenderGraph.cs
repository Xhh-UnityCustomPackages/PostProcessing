using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ContactShadowsRenderer : PostProcessVolumeRenderer<ContactShadows>
    {
        class RenderContactShadowPassData
        {
            public ComputeShader contactShadowsCS;
            public int kernel;

            public Vector4 params1;
            public Vector4 params2;
            public Vector4 params3;

            public int deferredContactShadowKernel;
            public int numTilesX;
            public int numTilesY;

            // public RTHandle depthTexture;
            public TextureHandle contactShadowsTexture;
        }

        public TextureHandle ContactShadowsTexture { get; private set; }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            using (var builder = renderGraph.AddComputePass<RenderContactShadowPassData>(profilingSampler.name, out var passData))
            {
                RenderContractShadows(passData);

                var desc = cameraData.cameraTargetDescriptor;
                var format = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Linear | GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;
            
                GetCompatibleDescriptor(ref desc, format);
                desc.enableRandomWrite = true;
                desc.useMipMap = false;
                
                var contactShadowsTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ContactShadowMap", false);
                passData.contactShadowsTexture = contactShadowsTexture;
                builder.UseTexture(contactShadowsTexture, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(contactShadowsTexture, PipelineShaderIDs._ContactShadowMap);
                
                passData.contactShadowsCS = m_ContactShadowCS;
                passData.deferredContactShadowKernel = m_ContactShadowCS.FindKernel("ContactShadowMap");
                
                int width = cameraData.cameraTargetDescriptor.width;
                int height = cameraData.cameraTargetDescriptor.height;
                passData.numTilesX = Mathf.CeilToInt(width / 8.0f);
                passData.numTilesY = Mathf.CeilToInt(height / 8.0f);
                
                builder.SetRenderFunc((RenderContactShadowPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    var computeShader = data.contactShadowsCS;
                    cmd.SetComputeVectorParam(computeShader, ShaderConstants.ParametersID, data.params1);
                    cmd.SetComputeVectorParam(computeShader, ShaderConstants.Parameters2ID, data.params2);
                    cmd.SetComputeVectorParam(computeShader, ShaderConstants.Parameters3ID, data.params3);
                    cmd.SetComputeTextureParam(computeShader,  passData.deferredContactShadowKernel, ShaderConstants.TextureUAVID, data.contactShadowsTexture);

                    
                    cmd.DispatchCompute(computeShader,  passData.deferredContactShadowKernel, data.numTilesX, data.numTilesY, 1);

                });

                this.ContactShadowsTexture = contactShadowsTexture;
            }
        }
    }
}
