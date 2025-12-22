using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class AtmosphericHeightFogRenderer : PostProcessVolumeRenderer<AtmosphericHeightFog>
    {
        private class AtmosphericHeightFogPassData
        {
            // Setup
            public Material material;
            // Inputs
            internal TextureHandle sourceTexture;
            // Output texture
            internal TextureHandle targetTexture;
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            var lightData = frameData.Get<UniversalLightData>();
            SetupMaterial(lightData);

            using (var builder = renderGraph.AddUnsafePass<AtmosphericHeightFogPassData>(profilingSampler.name, out var passData))
            {
                passData.material = m_GlobalMaterial;
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);

                passData.targetTexture = destination;
                builder.UseTexture(destination, AccessFlags.Write);

                builder.SetRenderFunc(static (AtmosphericHeightFogPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    RTHandle sourceTextureHdl = data.sourceTexture;
                    RTHandle targetTextureHdl = data.targetTexture;
                    Blitter.BlitCameraTexture(cmd, sourceTextureHdl, targetTextureHdl, data.material, 0);
                });
            }
        }

        void SetupMaterial(UniversalLightData lightData)
        {
            m_GlobalMaterial.SetFloat(ShaderConstants.FogIntensity, settings.fogIntensity.value);
            m_GlobalMaterial.SetVector(ShaderConstants.DistanceParam, new Vector4(settings.fogDistanceStart.value, settings.fogDistanceEnd.value, settings.fogDistanceFalloff.value, settings.fogColorDuo.value));
            m_GlobalMaterial.SetColor(ShaderConstants.FogColorStart, settings.fogColorStart.value);
            m_GlobalMaterial.SetColor(ShaderConstants.FogColorEnd, settings.fogColorEnd.value);

            if (settings.directionalFrom == AtmosphericHeightFog.DirectionalFrom.MainLight)
            {
                int mainLightIndex = lightData.mainLightIndex;
                if (mainLightIndex != -1)
                {
                    var mainLight = lightData.visibleLights[mainLightIndex];
                    m_GlobalMaterial.SetVector(ShaderConstants.DirectionalDir, -mainLight.localToWorldMatrix.GetColumn(2));
                }
            }
            else
            {
                m_GlobalMaterial.SetVector(ShaderConstants.DirectionalDir, settings.customDirectionalDirection.value);
            }
            m_GlobalMaterial.SetVector(ShaderConstants.DirectionalParam, new Vector4(settings.directionalIntensity.value, settings.directionalFalloff.value, 0, 0));
            m_GlobalMaterial.SetColor(ShaderConstants.DirectionalColor, settings.directionalColor.value);

            m_GlobalMaterial.SetVector(ShaderConstants.HeightParam, new Vector4(settings.fogHeightStart.value, settings.fogHeightEnd.value, settings.fogHeightFalloff.value, 0));

            m_GlobalMaterial.SetVector(ShaderConstants.SkyboxParam1, new Vector4(settings.skyboxFogIntensity.value, settings.skyboxFogHeight.value, settings.skyboxFogFalloff.value, settings.skyboxFogOffset.value));
            m_GlobalMaterial.SetVector(ShaderConstants.SkyboxParam2, new Vector4(settings.skyboxFogFill.value, 0, 0, 0));
        }
    }
}