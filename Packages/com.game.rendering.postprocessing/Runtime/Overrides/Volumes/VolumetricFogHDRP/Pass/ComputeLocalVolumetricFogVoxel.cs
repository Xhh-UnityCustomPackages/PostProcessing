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
        
        static internal VolumetricLightsParams vLightParams;
        
        public ComputeLocalVolumetricFogVoxel(PostProcessData postProcessData)
        {
            this.postProcessData = postProcessData;
            
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
            volumetricMaterial = runtimeShaders.volumetricMaterial;
            
            vLightParams = new VolumetricLightsParams();
            vLightParams.InitVolumetricLightParams();
        }
        
        public void Dispose()
        {
        }
        
        
        class VolumetricFogVoxelizationPassData
        {
            // public List<LocalVolumetricFog> volumetricFogs;
            public int maxSliceCount;
            public int viewCount;

            public ShaderVariablesVolumetric volumetricCB;
            public VolumetricGlobalParams volumetricGlobalCB;
            //TODO:Debug OverDraw
            //public TextureHandle fogOverdrawOutput;
            public bool fogOverdrawDebugEnabled;

            // Regular fogs
            public ComputeShader volumetricMaterialCS;
            // public GraphicsBuffer globalIndirectBuffer;
            // public GraphicsBuffer globalIndirectionBuffer;
            // public GraphicsBuffer materialDataBuffer;
            // public GraphicsBuffer visibleVolumeGlobalIndices;
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
                // passData.visibleVolumeGlobalIndices = m_VisibleVolumeGlobalIndices;
                passData.volumetricGlobalCB = VolumetricFogHDRPRenderer.volumetricGlobalCB;
                builder.SetRenderFunc((VolumetricFogVoxelizationPassData data, ComputeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(VolumetricFogVoxelizationPassData data, ComputeGraphContext context)
        {
            int volumeCount = 0;

            var computeShader = data.volumetricMaterialCS;
            int kernel = data.computeRenderingParametersKernel;
            context.cmd.SetComputeBufferParam(computeShader, kernel, VolumetricFogShaderIDs._VolumeBounds, data.visibleVolumeBoundsBuffer);
            // context.cmd.SetComputeBufferParam(computeShader, kernel, VolumetricFogShaderIDs._VolumetricGlobalIndirectArgsBuffer, data.globalIndirectBuffer);
            // context.cmd.SetComputeBufferParam(computeShader, kernel, VolumetricFogShaderIDs._VolumetricGlobalIndirectionBuffer, data.globalIndirectionBuffer);
            // context.cmd.SetComputeBufferParam(computeShader, kernel, VolumetricFogShaderIDs._VolumetricVisibleGlobalIndicesBuffer, data.visibleVolumeGlobalIndices);
            // context.cmd.SetComputeBufferParam(computeShader, kernel, VolumetricFogShaderIDs._VolumetricMaterialData, data.materialDataBuffer);
            context.cmd.SetComputeIntParam(computeShader, VolumetricFogShaderIDs._VolumeCount, volumeCount);
            context.cmd.SetComputeIntParam(computeShader, VolumetricFogShaderIDs._MaxSliceCount, data.maxSliceCount);
            context.cmd.SetComputeIntParam(computeShader, VolumetricFogShaderIDs._VolumetricViewCount, data.viewCount);
            //更新光源组件体积光部分传入GPU的数据
            vLightParams.UpadateSetVolumetricMainLightParams(context.cmd, data.lightData);
            vLightParams.UpadateSetVolumetricAdditionalLightParams(context.cmd, data.lightData);

            // ConstantBuffer.PushGlobal(data.volumetricCB, VolumetricFogShaderIDs._ShaderVariablesVolumetric);
            int dispatchXCount = Mathf.Max(1, Mathf.CeilToInt((float)(volumeCount * data.viewCount) / 32.0f));
            context.cmd.DispatchCompute(data.volumetricMaterialCS, data.computeRenderingParametersKernel, dispatchXCount, 1, 1);
            // ConstantBuffer.PushGlobal(data.volumetricGlobalCB, VolumetricFogShaderIDs._VolumetricGlobalParams);

            //Debug.Log(data.volumetricGlobalCB._VBufferSliceCount);

            // context.cmd.SetGlobalBuffer(VolumetricFogShaderIDs._VolumetricGlobalIndirectionBuffer, data.globalIndirectionBuffer);
        }

    }
}
