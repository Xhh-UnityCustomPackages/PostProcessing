using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ScreenSpaceCavityRenderer : PostProcessVolumeRenderer<ScreenSpaceCavity>
    {
        private class ScreenSpaceCavityRendererPassData
        {
            // Setup
            public Material material;
            // Inputs
            internal TextureHandle sourceTexture;

            internal ScreenSpaceCavity.OutputEffectTo output;
            // Pass textures
            internal TextureHandle tempTexture;
            // Output texture
            internal TextureHandle targetTexture;
        }

        private void SetupMaterials(ref UniversalCameraData cameraData)
        {
            if (m_Material == null)
                m_Material = GetMaterial(postProcessFeatureData.shaders.ScreenSpaceCavityPS);
            
            var sourceWidth = cameraData.cameraTargetDescriptor.width;
            var sourceHeight = cameraData.cameraTargetDescriptor.height;

            //float tanHalfFovY = Mathf.Tan(0.5f * cameraData.camera.fieldOfView * Mathf.Deg2Rad);
            //float invFocalLenX = 1.0f / (1.0f / tanHalfFovY * (sourceHeight / (float)sourceWidth));
            //float invFocalLenY = 1.0f / (1.0f / tanHalfFovY);
            //material.SetVector(ShaderProperties.uvToView, new Vector4(2.0f * invFocalLenX, -2.0f * invFocalLenY, -1.0f * invFocalLenX, 1.0f * invFocalLenY));

            m_Material.SetVector(ShaderConstants.inputTexelSize, new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
            int div = settings.cavityResolution.value == ScreenSpaceCavity.CavityResolution.Full ? 1 : settings.cavityResolution.value == ScreenSpaceCavity.CavityResolution.HalfUpscaled ? 2 : 2;
            m_Material.SetVector(ShaderConstants.cavityTexTexelSize, new Vector4(1f / (sourceWidth / div), 1f / (sourceHeight / div), sourceWidth / div, sourceHeight / div));
            m_Material.SetMatrix(ShaderConstants.worldToCameraMatrix, cameraData.camera.worldToCameraMatrix);

            m_Material.SetFloat(ShaderConstants.effectIntensity, settings.intensity.value);
            m_Material.SetFloat(ShaderConstants.distanceFade, settings.distanceFade.value);
            m_Material.SetInt(ShaderConstants.cavitySampleCount, (int)settings.cavitySamples.value);

            m_Material.SetFloat(ShaderConstants.curvaturePixelRadius, new float[] { 0f, 0.5f, 1f, 1.5f, 2.5f }[settings.curvaturePixelRadius.value]);
            m_Material.SetFloat(ShaderConstants.curvatureRidge, settings.curvatureBrights.value == 0f ? 999f : (5f - settings.curvatureBrights.value));
            m_Material.SetFloat(ShaderConstants.curvatureValley, settings.curvatureDarks.value == 0f ? 999f : (5f - settings.curvatureDarks.value));

            m_Material.SetFloat(ShaderConstants.cavityWorldRadius, settings.cavityRadius.value);
            m_Material.SetFloat(ShaderConstants.cavityRidge, settings.cavityBrights.value * 2f);
            m_Material.SetFloat(ShaderConstants.cavityValley, settings.cavityDarks.value * 2f);


            m_ShaderKeywords[0] = settings.debugMode.value == ScreenSpaceCavity.DebugMode.EffectOnly ? "DEBUG_EFFECT" : settings.debugMode.value == ScreenSpaceCavity.DebugMode.ViewNormals ? "DEBUG_NORMALS" : "__";
            m_ShaderKeywords[1] = settings.normalsSource.value == ScreenSpaceCavity.PerPixelNormals.ReconstructedFromDepth ? "NORMALS_RECONSTRUCT" : "__";
            m_ShaderKeywords[2] = settings.saturateCavity.value ? "SATURATE_CAVITY" : "__";
            m_ShaderKeywords[3] = settings.output == ScreenSpaceCavity.OutputEffectTo._ScreenSpaceCavityRT ? "OUTPUT_TO_TEXTURE" : "__";
            m_ShaderKeywords[4] = settings.cavityResolution.value == ScreenSpaceCavity.CavityResolution.HalfUpscaled ? "UPSCALE_CAVITY" : "__";


            m_Material.shaderKeywords = m_ShaderKeywords;
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ref UniversalResourceData resourceData, ref UniversalCameraData cameraData)
        {
            SetupMaterials(ref cameraData);

            RenderTextureDescriptor cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
            var desc = cameraTargetDescriptor;
            GetCompatibleDescriptor(ref desc, desc.graphicsFormat);
            desc.colorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.RHalf;
            
            var resolution = settings.cavityResolution.value;
            if (resolution != ScreenSpaceCavity.CavityResolution.Full)
            {
                DescriptorDownSample(ref desc, 2);
            }
            
            TextureHandle cameraNormalsTexture = resourceData.cameraNormalsTexture;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;
            
            var cavityFinalRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceCavityRT", true);
            var tempRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceCavityTempRT", true);
            
            using (var builder = renderGraph.AddUnsafePass<ScreenSpaceCavityRendererPassData>(profilingSampler.name, out var passData))
            {
                passData.output = settings.output.value;
                passData.material = m_Material;
                
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                
                passData.tempTexture = tempRT;
                builder.UseTexture(tempRT, AccessFlags.ReadWrite);

                if (settings.output.value == ScreenSpaceCavity.OutputEffectTo.Screen)
                {
                    passData.targetTexture = destination;
                    builder.UseTexture(destination, AccessFlags.Write);
                }
                else
                {
                    passData.targetTexture = cavityFinalRT;
                    builder.UseTexture(cavityFinalRT, AccessFlags.Write);
                }

               
                
                builder.UseTexture(cameraNormalsTexture, AccessFlags.Read);
                builder.UseTexture(cameraDepthTexture, AccessFlags.Read);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ScreenSpaceCavityRendererPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    
                     RTHandle sourceTextureHdl = data.sourceTexture;
                     RTHandle tempTextureHdl = data.tempTexture;
                     
                     Blitter.BlitCameraTexture(cmd, sourceTextureHdl, tempTextureHdl, data.material, Pass.GenerateCavity);

                     data.material.SetTexture("_CavityTex", sourceTextureHdl);
                     
                     RTHandle destination = data.targetTexture;
                     
                     if (data.output == ScreenSpaceCavity.OutputEffectTo.Screen)
                     {
                         Blitter.BlitCameraTexture(cmd, tempTextureHdl, destination, data.material, Pass.Final);
                     }
                     else
                     {
                         Blitter.BlitCameraTexture(cmd, tempTextureHdl, destination, data.material, Pass.Final);
                         cmd.SetGlobalTexture(ShaderConstants.globalSSCCTexture, destination);
                     }
                });

                if (settings.output.value != ScreenSpaceCavity.OutputEffectTo.Screen)
                {
                    builder.SetGlobalTextureAfterPass(cavityFinalRT, ShaderConstants.globalSSCCTexture);
                }

            }
        }
    }
}