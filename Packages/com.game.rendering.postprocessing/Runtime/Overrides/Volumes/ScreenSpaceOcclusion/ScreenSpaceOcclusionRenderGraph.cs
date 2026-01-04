using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ScreenSpaceOcclusionRenderer : PostProcessVolumeRenderer<ScreenSpaceOcclusion>
    {
        private class ScreenSpaceOcclusionPassData
        {
            // Setup
            public Material material;
            
            // Inputs
            internal TextureHandle sourceTexture;
            internal TextureHandle occlusionDepthTexture;
            internal TextureHandle occlusionTempTexture;
            internal TextureHandle occlusionFinalTexture;
            public ScreenSpaceOcclusion.BlurType blurType;
        }
        
        private void SetupMaterials(ref UniversalCameraData cameraData)
        {
            if (m_AmbientOcclusionMaterial == null)
            {
                var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<ScreenSpaceOcclusionResources>();
                m_AmbientOcclusionMaterial = GetMaterial(runtimeResources.ScreenSpaceOcclusionPS);
            }

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            var widthAO = m_AmbientOcclusionDescriptor.width;
            var heightAO = m_AmbientOcclusionDescriptor.height;

            float invFocalLenX = 1.0f / cameraData.camera.projectionMatrix.m00;
            float invFocalLenY = 1.0f / cameraData.camera.projectionMatrix.m11;

            if (settings.type.value == ScreenSpaceOcclusion.AOType.ScalableAmbientObscurance)
                m_AmbientOcclusionMaterial.SetMatrix(ShaderConstants.CameraProjMatrix, cameraData.camera.projectionMatrix);

            var targetScale = Vector4.one;
            switch (settings.resolution.value)
            {
                case ScreenSpaceOcclusion.Resolution.Half:
                    targetScale = new Vector4((width + 0.5f) / width, (height + 0.5f) / height, 1f, 1f);
                    break;
                case ScreenSpaceOcclusion.Resolution.Quarter:
                    targetScale = new Vector4((width + 0.25f) / width, (height + 0.25f) / height, 1f, 1f);
                    break;
            }


            float maxRadInPixels = Mathf.Max(16, settings.maxRadiusPixels.value * Mathf.Sqrt((width * height) / (1080.0f * 1920.0f)));

            m_AmbientOcclusionMaterial.SetVector(ShaderConstants.FullTexelSize, new Vector4(1f / width, 1f / height, width, height));
            m_AmbientOcclusionMaterial.SetVector(ShaderConstants.ScaledTexelSize, new Vector4(1f / widthAO, 1f / heightAO, widthAO, heightAO));
            m_AmbientOcclusionMaterial.SetVector(ShaderConstants.TargetScale, targetScale);
            m_AmbientOcclusionMaterial.SetVector(ShaderConstants.UVToView, new Vector4(2.0f * invFocalLenX, -2.0f * invFocalLenY, -1.0f * invFocalLenX, 1.0f * invFocalLenY));
            m_AmbientOcclusionMaterial.SetMatrix(ShaderConstants.WorldToCameraMatrix, cameraData.camera.worldToCameraMatrix);

            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.Radius, settings.radius.value);
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.RadiusToScreen, settings.radius.value * 0.5f * (height / (invFocalLenY * 2.0f)));
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.MaxRadiusPixels, maxRadInPixels);
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.InvRadius2, 1.0f / (settings.radius.value * settings.radius.value));
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.AngleBias, settings.bias.value);
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.AOMultiplier, 2.0f * (1.0f / (1.0f - settings.bias.value)));
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.Intensity, settings.intensity.value);
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.Thickness, settings.thickness.value);
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.MaxDistance, settings.maxDistance.value);
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.DistanceFalloff, settings.distanceFalloff.value);
            m_AmbientOcclusionMaterial.SetFloat(ShaderConstants.BlurSharpness, settings.sharpness.value);
            // -------------------------------------------------------------------------------------------------
            // local shader keywords
            m_ShaderKeywords[0] = ShaderConstants.GetAOTypeKeyword(settings.type.value);
            m_ShaderKeywords[1] = ShaderConstants.GetQualityKeyword(settings.quality.value);
            m_ShaderKeywords[2] = ShaderConstants.GetBlurRadiusKeyword(settings.blurType.value);
            m_ShaderKeywords[3] = ShaderConstants.GetReconstructNormal(settings.reconstructNormal.value);
            m_ShaderKeywords[4] = ShaderConstants.GetDebugKeyword(settings.debugMode.value);

            m_AmbientOcclusionMaterial.shaderKeywords = m_ShaderKeywords;
        }
        
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            SetupMaterials(ref cameraData);

            using (var builder = renderGraph.AddUnsafePass<ScreenSpaceOcclusionPassData>(profilingSampler.name, out var passData))
            {
                passData.material = m_AmbientOcclusionMaterial;
                passData.blurType = settings.blurType.value;
                
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                
                m_AmbientOcclusionDescriptor = cameraData.cameraTargetDescriptor;
                m_AmbientOcclusionDescriptor.msaaSamples = 1;
                m_AmbientOcclusionDescriptor.depthBufferBits = 0;
                
                if (settings.resolution == ScreenSpaceOcclusion.Resolution.Half)
                {
                    DescriptorDownSample(ref m_AmbientOcclusionDescriptor, 2);
                }
                else if (settings.resolution == ScreenSpaceOcclusion.Resolution.Quarter)
                {
                    DescriptorDownSample(ref m_AmbientOcclusionDescriptor, 4);
                }
                
                if (settings.debugMode.value != ScreenSpaceOcclusion.DebugMode.Disabled)
                {
                    m_AmbientOcclusionDescriptor.colorFormat = m_AmbientOcclusionColorFormat;
                }
                else
                {
                    m_AmbientOcclusionDescriptor.colorFormat = RenderTextureFormat.R8;
                }
                
                var occlusionFinalRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_AmbientOcclusionDescriptor, "OcclusionFinalRT", false);
                passData.occlusionFinalTexture = occlusionFinalRT;
                builder.UseTexture(occlusionFinalRT, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(occlusionFinalRT, ShaderConstants.ScreenSpaceOcclusionTexture);
                
                m_AmbientOcclusionDescriptor.colorFormat = m_AmbientOcclusionColorFormat;
                if (settings.blurType != ScreenSpaceOcclusion.BlurType.None)
                {
                    var occlusionTempRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_AmbientOcclusionDescriptor, "OcclusionTempRT", false);
                    passData.occlusionTempTexture = occlusionTempRT;
                    builder.UseTexture(occlusionTempRT, AccessFlags.ReadWrite);
                }

                var occlusionDepthRT = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_AmbientOcclusionDescriptor, "OcclusionDepthRT", false);
                passData.occlusionDepthTexture = occlusionDepthRT;
                builder.UseTexture(occlusionDepthRT, AccessFlags.ReadWrite);

                //在RenderPass之间传递数据
                if (settings.debugMode.value != ScreenSpaceOcclusion.DebugMode.Disabled)
                {
                    var customData = frameData.Create<ScreenSpaceOcclusionDebug.ScreenSpaceOcclusionDebugData>();
                    customData.occlusionFinalTexture = occlusionFinalRT;
                }

                builder.SetRenderFunc(static (ScreenSpaceOcclusionPassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(ScreenSpaceOcclusionPassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, true);
                    
            // AO
            RTHandle sourceTextureHdl = data.sourceTexture;
            RTHandle occlusionDepthHdl = data.occlusionDepthTexture;
            Blitter.BlitCameraTexture(cmd, sourceTextureHdl, occlusionDepthHdl, data.material, 0);

            // Blur
            if (data.blurType != ScreenSpaceOcclusion.BlurType.None)
            {
                RTHandle blurTempTextureHdl = data.occlusionTempTexture;
                Blitter.BlitCameraTexture(cmd, occlusionDepthHdl, blurTempTextureHdl, data.material, 1);
                Blitter.BlitCameraTexture(cmd, blurTempTextureHdl, occlusionDepthHdl, data.material, 2);
            } 
                    
            RTHandle finalTextureHdl = data.occlusionFinalTexture;
            // Composite
            // cmd.SetGlobalTexture(ShaderConstants.ScreenSpaceOcclusionTexture, finalTextureHdl);
            cmd.SetGlobalVector(ShaderConstants.AmbientOcclusionParam, new Vector4(1, 0, 0, 0.25f));
            Blitter.BlitCameraTexture(cmd, occlusionDepthHdl, finalTextureHdl, data.material, 3);
        }
    }
}
