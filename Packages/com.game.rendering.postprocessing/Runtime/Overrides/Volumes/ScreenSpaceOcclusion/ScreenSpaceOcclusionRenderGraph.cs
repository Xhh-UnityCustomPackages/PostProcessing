using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ScreenSpaceOcclusionRenderer : PostProcessVolumeRenderer<ScreenSpaceOcclusion>
    {
        private void SetupMaterials(ref UniversalCameraData cameraData)
        {
            if (m_AmbientOcclusionMaterial == null)
                m_AmbientOcclusionMaterial = GetMaterial(postProcessFeatureData.shaders.screenSpaceOcclusionPS);

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
        }
    }
}
