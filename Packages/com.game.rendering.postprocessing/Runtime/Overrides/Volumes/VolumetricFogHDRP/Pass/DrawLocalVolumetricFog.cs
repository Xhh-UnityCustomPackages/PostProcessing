using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class DrawLocalVolumetricFog : ScriptableRenderPass, IDisposable
    {
        private PostProcessData postProcessData;
        private ShaderTagId s_VolumetricFogPassNames;
        public DrawLocalVolumetricFog(PostProcessData postProcessData)
        {
            this.postProcessData = postProcessData;
            
            s_VolumetricFogPassNames = new ShaderTagId("FogVolumeVoxelize");
        }

        public void Dispose()
        {
        }
        
        class LocalVolumetricFogParams
        {
            public Vector3Int viewportSize;
            public TextureHandle densityBuffer;
            public RendererListHandle localVolumeRendererList;
        }

        static void ExecutePass(LocalVolumetricFogParams data, RasterGraphContext context)
        {
            context.cmd.SetViewport(new Rect(0, 0, data.viewportSize.x, data.viewportSize.y));
            context.cmd.DrawRendererList(data.localVolumeRendererList);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "DrawLocalFogVoxel";

            if (!SystemInfo.supportsRenderTargetArrayIndexFromVertexShader)
            {
                Debug.LogError("Hardware not supported for Volumetric Materials");
                return;
            }

            int frameIndex = (int)VolumetricFogHDRPRenderer.VolumetricFrameIndex(postProcessData);
            var currIdx = (frameIndex + 0) & 1;
            var currParams = postProcessData.vBufferParams[currIdx];
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            CullingResults cullingResults = renderingData.cullResults;

            using (var builder = renderGraph.AddRasterRenderPass<LocalVolumetricFogParams>(passName, out var passData))
            {
                builder.AllowPassCulling(false);
                var vfxFogVolumeRendererListDesc = new RendererListDesc(s_VolumetricFogPassNames, cullingResults, postProcessData.camera)
                {
                    rendererConfiguration = PerObjectData.None,
                    renderQueueRange = RenderQueueRange.all,
                    sortingCriteria = SortingCriteria.RendererPriority,
                    excludeObjectMotionVectors = false
                };
                RendererListHandle listHandle = renderGraph.CreateRendererList(vfxFogVolumeRendererListDesc);
                builder.UseRendererList(listHandle);
                passData.localVolumeRendererList = listHandle;
                passData.densityBuffer = VolumetricFogHDRPRenderer.m_DensityTexture;
                passData.viewportSize = currParams.viewportSize;
                builder.SetRenderAttachment(VolumetricFogHDRPRenderer.m_DensityTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((LocalVolumetricFogParams data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }
    }
}