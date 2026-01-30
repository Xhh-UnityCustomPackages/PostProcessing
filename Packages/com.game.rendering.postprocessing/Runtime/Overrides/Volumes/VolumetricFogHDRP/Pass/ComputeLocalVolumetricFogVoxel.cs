using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ComputeLocalVolumetricFogVoxel : ScriptableRenderPass, IDisposable
    {
        private PostProcessData postProcessData;
        private ComputeShader volumetricMaterial;
        
        internal static ComputeBuffer m_VisibleVolumeBoundsBuffer = null;
        internal static GraphicsBuffer m_VisibleVolumeGlobalIndices = null;
        
        public ComputeLocalVolumetricFogVoxel(PostProcessData postProcessData)
        {
            this.postProcessData = postProcessData;
            
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogResources>();
            volumetricMaterial = runtimeShaders.volumetricMaterial;
        }
        
        public void Dispose()
        {
        }
        
        
        class VolumetricFogVoxelizationPassData
        {
            // public List<LocalVolumetricFog> volumetricFogs;
            public int maxSliceCount;
            public int viewCount;

            //public Vector3Int viewportSize;
            //public TextureHandle densityBuffer;
            //public RendererListHandle vfxRendererList;
            //public RendererListHandle vfxDebugRendererList;
            public ShaderVariablesVolumetric volumetricCB;
            public VolumetricGlobalParams volumetricGlobalCB;
            //TODO:Debug OverDraw
            //public TextureHandle fogOverdrawOutput;
            public bool fogOverdrawDebugEnabled;

            // Regular fogs
            public ComputeShader volumetricMaterialCS;
            public GraphicsBuffer globalIndirectBuffer;
            public GraphicsBuffer globalIndirectionBuffer;
            public GraphicsBuffer materialDataBuffer;
            public GraphicsBuffer visibleVolumeGlobalIndices;
            public int computeRenderingParametersKernel;
            public ComputeBuffer visibleVolumeBoundsBuffer;
            public UniversalLightData lightData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "ComputeLocalFogVoxel";

            if (!SystemInfo.supportsRenderTargetArrayIndexFromVertexShader)
            {
                Debug.LogError("Hardware not supported for Volumetric Materials");
                return;
            }
            
            
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            
            //TODO:实现DebugOverDraw
            bool fogOverdrawDebugEnabled = false;

            using (var builder = renderGraph.AddComputePass<VolumetricFogVoxelizationPassData>(passName, out var passData))
            {
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                var m_ShaderVariablesVolumetricCB = VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB;
                passData.lightData = lightData;
                passData.volumetricCB = m_ShaderVariablesVolumetricCB;
                passData.fogOverdrawDebugEnabled = fogOverdrawDebugEnabled;
                passData.volumetricMaterialCS = volumetricMaterial;
                passData.computeRenderingParametersKernel = passData.volumetricMaterialCS.FindKernel("ComputeVolumetricMaterialRenderingParameters");
                passData.visibleVolumeBoundsBuffer = m_VisibleVolumeBoundsBuffer;
                // passData.globalIndirectBuffer = LocalVolumetricFogManager.manager.globalIndirectBuffer;
                // passData.globalIndirectionBuffer = LocalVolumetricFogManager.manager.globalIndirectionBuffer;
                // passData.volumetricFogs = m_VisibleLocalVolumetricFogVolumes;
                // passData.materialDataBuffer = LocalVolumetricFogManager.manager.volumetricMaterialDataBuffer;
                passData.maxSliceCount = (int)m_ShaderVariablesVolumetricCB._MaxSliceCount;
                passData.viewCount = 1;
                passData.visibleVolumeGlobalIndices = m_VisibleVolumeGlobalIndices;
                passData.volumetricGlobalCB = VolumetricFogHDRPRenderer.volumetricGlobalCB;
                builder.SetRenderFunc((VolumetricFogVoxelizationPassData data, ComputeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(VolumetricFogVoxelizationPassData data, ComputeGraphContext context)
        {
            int volumeCount = 0;
            
             context.cmd.SetComputeBufferParam(data.volumetricMaterialCS, data.computeRenderingParametersKernel, VolumetricFogShaderIDs._VolumeBounds, data.visibleVolumeBoundsBuffer);
                context.cmd.SetComputeBufferParam(data.volumetricMaterialCS, data.computeRenderingParametersKernel, VolumetricFogShaderIDs._VolumetricGlobalIndirectArgsBuffer, data.globalIndirectBuffer);
                context.cmd.SetComputeBufferParam(data.volumetricMaterialCS, data.computeRenderingParametersKernel, VolumetricFogShaderIDs._VolumetricGlobalIndirectionBuffer, data.globalIndirectionBuffer);
                context.cmd.SetComputeBufferParam(data.volumetricMaterialCS, data.computeRenderingParametersKernel, VolumetricFogShaderIDs._VolumetricVisibleGlobalIndicesBuffer, data.visibleVolumeGlobalIndices);
                context.cmd.SetComputeBufferParam(data.volumetricMaterialCS, data.computeRenderingParametersKernel, VolumetricFogShaderIDs._VolumetricMaterialData, data.materialDataBuffer);
                context.cmd.SetComputeIntParam(data.volumetricMaterialCS, VolumetricFogShaderIDs._VolumeCount, volumeCount);
                context.cmd.SetComputeIntParam(data.volumetricMaterialCS, VolumetricFogShaderIDs._MaxSliceCount, data.maxSliceCount);
                context.cmd.SetComputeIntParam(data.volumetricMaterialCS, VolumetricFogShaderIDs._VolumetricViewCount, data.viewCount);
                //更新光源组件体积光部分传入GPU的数据
                // vLightParams.UpadateSetVolumetricMainLightParams(context.cmd, data.lightData);
                // vLightParams.UpadateSetVolumetricAdditionalLightParams(context.cmd, data.lightData);

                ConstantBuffer.PushGlobal(data.volumetricCB, VolumetricFogShaderIDs._ShaderVariablesVolumetric);
                int dispatchXCount = Mathf.Max(1, Mathf.CeilToInt((float)(volumeCount * data.viewCount) / 32.0f));
                context.cmd.DispatchCompute(data.volumetricMaterialCS, data.computeRenderingParametersKernel, dispatchXCount, 1, 1);
                ConstantBuffer.PushGlobal(data.volumetricGlobalCB, VolumetricFogShaderIDs._VolumetricGlobalParams);

                //Debug.Log(data.volumetricGlobalCB._VBufferSliceCount);

                context.cmd.SetGlobalBuffer(VolumetricFogShaderIDs._VolumetricGlobalIndirectionBuffer, data.globalIndirectionBuffer);
        }

    }
}
