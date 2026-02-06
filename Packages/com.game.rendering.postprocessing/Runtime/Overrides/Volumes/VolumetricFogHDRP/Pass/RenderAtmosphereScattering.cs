using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

using ShaderIDs = Game.Core.PostProcessing.VolumetricFogShaderIDs;

namespace Game.Core.PostProcessing
{
    public class RenderAtmosphereScattering : ScriptableRenderPass, IDisposable
    {
        private Material AtmospherMat;
        private PostProcessData m_PostProcessData;

        public RenderAtmosphereScattering(PostProcessData data)
        {
            m_PostProcessData = data;
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
            AtmospherMat = CoreUtils.CreateEngineMaterial(runtimeShaders.atmosphereScattering);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(AtmospherMat);
        }
        
        
        class AtmosphereParams
        {
            public TextureHandle volumetricLighting;
            public Matrix4x4 pixelCoordToViewDirWS;
            public TextureHandle depthTexture;
            public TextureHandle ColorTex;
            public Material mat;
        }

        static void ExecutePass(AtmosphereParams data, RasterGraphContext context)
        {
            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            //mpb.SetTexture(VolumetricFogShaderIDs._CameraDepthTexture, data.depthTexture);
            mpb.SetMatrix(ShaderIDs._PixelCoordToViewDirWSID, data.pixelCoordToViewDirWS);
            mpb.SetFloat(ShaderIDs._EnableVolumetricFog, VolumetricFogHDRPRenderer.volumetricGlobalCB._EnableVolumetricFog);
            mpb.SetFloat(ShaderIDs._VBufferRcpSliceCount, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferRcpSliceCount);
            mpb.SetVector(ShaderIDs._VBufferDistanceEncodingParams, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferDistanceEncodingParams);
            mpb.SetVector(ShaderIDs._VBufferLightingViewportLimit, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferLightingViewportLimit);
            mpb.SetVector(ShaderIDs._VBufferLightingViewportScale, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferLightingViewportScale);
            //mpb.SetTexture("_ColorTex", data.ColorTex);
            context.cmd.SetGlobalTexture(ShaderIDs._VolumetricLighting, data.volumetricLighting);
            context.cmd.DrawProcedural(Matrix4x4.identity, data.mat, 0, MeshTopology.Triangles, 3, 1, mpb);
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {

            const string passName = "RenderAtmosphereScattering";
            TextureHandle destination;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            var source = resourceData.activeColorTexture;

            // var targetDesc = renderGraph.GetTextureDesc(resourceData.cameraColor);
            // targetDesc.name = "_DrawAtmospherScattering";
            // targetDesc.clearBuffer = false;

            //destination =  UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraData.cameraTargetDescriptor, "DrawAtmosphere", false);
            destination = source;

            using (var builder = renderGraph.AddRasterRenderPass<AtmosphereParams>(passName, out var passData))
            {
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                passData.volumetricLighting = VolumetricFogHDRPRenderer.m_LightingTexture;
                builder.UseTexture(passData.volumetricLighting, AccessFlags.Read);
                passData.pixelCoordToViewDirWS = m_PostProcessData.pixelCoordToViewDirWS;
                //passData.ColorTex = source;
                //builder.UseTexture(passData.ColorTex, AccessFlags.Read);
                passData.depthTexture = resourceData.cameraDepthTexture;
                passData.mat = AtmospherMat;
                builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                builder.SetRenderFunc((AtmosphereParams data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }
    }
}