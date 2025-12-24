using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class LightShaftRenderer : PostProcessVolumeRenderer<LightShaft>
    {
        private class LightShaftPassData
        {
            // Setup
            public Material material;
            public LightShaft.Mode mode;
            // Inputs
            internal TextureHandle sourceTexture;
            // Pass textures
            internal TextureHandle lightShaftRT0;
            internal TextureHandle lightShaftRT1;
            // Output texture
            internal TextureHandle targetTexture;
        }
        
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            var lightData = frameData.Get<UniversalLightData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            SetupDirectionLight(ref lightData, ref renderingData, ref cameraData);
            SetupMaterials();

            using (var builder = renderGraph.AddUnsafePass<LightShaftPassData>(profilingSampler.name, out var passData))
            {
                passData.material = m_Material;
                passData.mode = settings.mode.value;
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);

                passData.targetTexture = destination;
                builder.UseTexture(destination, AccessFlags.Write);
                
                RenderTextureDescriptor cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
                m_Descriptor = cameraTargetDescriptor;
                GetCompatibleDescriptor(ref m_Descriptor, m_Descriptor.graphicsFormat);

                if ((int)settings.downSample.value > 1)
                {
                    DescriptorDownSample(ref m_Descriptor, (int)settings.downSample.value);
                }
                
                var lightShaftRT0 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_Descriptor, "_LightShaftRT0", false);
                var lightShaftRT1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, m_Descriptor, "_LightShaftRT1", false);

                passData.lightShaftRT0  = lightShaftRT0;
                builder.UseTexture(lightShaftRT0, AccessFlags.ReadWrite);
                
                passData.lightShaftRT1  = lightShaftRT1;
                builder.UseTexture(lightShaftRT1, AccessFlags.ReadWrite);

                builder.SetRenderFunc(static (LightShaftPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    RTHandle sourceTextureHdl = data.sourceTexture;
                    RTHandle lightShaftRT0 = data.lightShaftRT0;
                    RTHandle lightShaftRT1 = data.lightShaftRT1;
                    RTHandle targetTextureHdl = data.targetTexture;

                    if (data.mode == LightShaft.Mode.Occlusion)
                        Blitter.BlitCameraTexture(cmd, sourceTextureHdl, lightShaftRT0, data.material, (int)Pass.LightShaftsOcclusionPrefilter);
                    else
                        Blitter.BlitCameraTexture(cmd, sourceTextureHdl, lightShaftRT0, data.material, (int)Pass.LightShaftsBloomPrefilter);

                    //do radial blur for 3 times
                    RTHandle temp1 = lightShaftRT0;
                    RTHandle temp2 = lightShaftRT1;

                    for (int i = 0; i < 3; i++)
                    {
                        Blitter.BlitCameraTexture(cmd, temp1, temp2, data.material, (int)Pass.LightShaftsBlur);

                        (temp2, temp1) = (temp1, temp2);
                    }

                    data.material.SetTexture(ShaderConstants.LightShafts1, lightShaftRT1);
                    if (data.mode == LightShaft.Mode.Occlusion)
                        Blitter.BlitCameraTexture(cmd, sourceTextureHdl, targetTextureHdl, data.material, (int)Pass.LightShaftsOcclusionBlend);
                    else
                        Blitter.BlitCameraTexture(cmd, sourceTextureHdl, targetTextureHdl, data.material, (int)Pass.LightShaftsBloomBlend);
                });
            }
        }

        private void SetupDirectionLight(ref UniversalLightData lightData, ref UniversalRenderingData renderingData, ref UniversalCameraData cameraData)
        {
            if (lightData.mainLightIndex == -1)
            {
                return;
            }

            var camera = cameraData.camera;

            var mainLight = renderingData.cullResults.visibleLights[lightData.mainLightIndex];
            var lightDir = -mainLight.localToWorldMatrix.GetColumn(2);
            Vector4 lightScreenPos = new Vector4(camera.transform.position.x, camera.transform.position.y, camera.transform.position.z, 0) + lightDir * camera.farClipPlane;
            lightScreenPos = camera.WorldToViewportPoint(lightScreenPos);

            m_LightShaftInclude._LightSource = new Vector4(lightScreenPos.x, lightScreenPos.y, lightScreenPos.z, 0);

            m_Material.SetVector(ShaderConstants.lightSource, m_LightShaftInclude._LightSource);

            Vector3 cameraDirWS = cameraData.camera.transform.forward;
            float lightAtten = Mathf.Clamp(Vector3.Dot(lightDir, cameraDirWS), 0, 1);
            m_LightShaftInclude._ShaftsAtten = lightAtten;
            m_Material.SetFloat(ShaderConstants.Atten, m_LightShaftInclude._ShaftsAtten);
        }
    }
}