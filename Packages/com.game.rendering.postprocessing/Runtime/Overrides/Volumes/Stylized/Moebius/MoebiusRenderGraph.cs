using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class MoebiusRenderer : PostProcessVolumeRenderer<Moebius>
    {
        private class MoebiusPassData
        {
            public Material material;
            internal TextureHandle sourceTexture;
            internal TextureHandle targetTexture;
            internal TextureHandle sobelSourceTexture;
            internal TextureHandle sobelResultTexture;
            public Moebius.SobelSource sobelSource;
            public Moebius.DebugMode debugMode;
        }
        
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            if (m_Material == null)
                return;
            
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            SetupMaterials(cameraData.camera, m_Material);

            using (var builder = renderGraph.AddUnsafePass<MoebiusPassData>(profilingSampler.name, out var passData))
            {
                passData.material = m_Material;
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);

                passData.targetTexture = destination;
                builder.UseTexture(destination, AccessFlags.Write);
                
                passData.sobelSource = settings.sobelSource.value;
                passData.debugMode = settings.debugMode.value;

                TextureHandle sobelSourceTexture;
                if (passData.sobelSource == Moebius.SobelSource.Depth)
                {
                    sobelSourceTexture = resourceData.cameraDepthTexture;
                    
                }
                else
                {
                    sobelSourceTexture = resourceData.cameraNormalsTexture;
                }
                passData.sobelSourceTexture = sobelSourceTexture;
                builder.UseTexture(sobelSourceTexture, AccessFlags.Read);
                
                var desc = cameraData.cameraTargetDescriptor;
                GetCompatibleDescriptor(ref desc, GraphicsFormat.B10G11R11_UFloatPack32);
                var sobelResultTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_SobelResultRT", false);
                passData.sobelResultTexture = sobelResultTexture;
                builder.UseTexture(sobelResultTexture, AccessFlags.ReadWrite);

                builder.SetRenderFunc(static (MoebiusPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    
                    // Step 1 Sobel Filter
                    RTHandle sobelSourceTextureHdl = data.sobelSourceTexture;
                    RTHandle sobelResultTextureHdl = data.sobelResultTexture;
                    
                    Blitter.BlitCameraTexture(cmd, sobelSourceTextureHdl, sobelResultTextureHdl, data.material, 0);
                    
                    RTHandle targetTextureHdl = data.targetTexture;
                    
                    //Debug
                    if (data.debugMode != Moebius.DebugMode.Disabled)
                    {
                        if (data.debugMode == Moebius.DebugMode.Sobel)
                        {
                            Blitter.BlitCameraTexture(cmd, sobelResultTextureHdl, targetTextureHdl);

                        }
                        else
                        {
                            Blitter.BlitCameraTexture(cmd, sobelResultTextureHdl, targetTextureHdl);
                        }

                        return;
                    }

                    RTHandle sourceTextureHdl = data.sourceTexture;
                   
                    data.material.SetTexture(ShaderConstants.SobelResultRT, sobelResultTextureHdl);
                    Blitter.BlitCameraTexture(cmd, sourceTextureHdl, targetTextureHdl, data.material, 2);
                    
                });
            }
        }
    }
}
