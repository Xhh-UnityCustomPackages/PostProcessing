using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

using ShaderIDs = Game.Core.PostProcessing.VolumetricFogShaderIDs;

namespace Game.Core.PostProcessing
{
    public class ComputeLocalVolumetricFogVoxel : ScriptableRenderPass, IDisposable
    {
        private ComputeShader volumetricMaterial;


        static internal VolumetricLightsParams vLightParams;

        public ComputeLocalVolumetricFogVoxel()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
            volumetricMaterial = runtimeShaders.volumetricMaterial;

            vLightParams = new VolumetricLightsParams();
            vLightParams.InitVolumetricLightParams();
        }

        public void Dispose()
        {

        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!SystemInfo.supportsRenderTargetArrayIndexFromVertexShader)
            {
                Debug.LogError("Hardware not supported for Volumetric Materials");
                return;
            }

            var cmd = CommandBufferPool.Get("VolumetricFog");
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // 获取数据
                var lightData = renderingData.lightData;
                var volumetricFogs = VolumetricFogHDRPRenderer.m_VisibleLocalVolumetricFogVolumes;
                int volumeCount = volumetricFogs.Count;
                int maxSliceCount = (int)VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB._MaxSliceCount;
                int viewCount = 1;
                int kernel = volumetricMaterial.FindKernel("ComputeVolumetricMaterialRenderingParameters");

                // 设置Compute Buffer参数
                cmd.SetComputeBufferParam(volumetricMaterial, kernel, ShaderIDs._VolumeBounds, VolumetricFogHDRPRenderer.m_VisibleVolumeBoundsBuffer);
                cmd.SetComputeBufferParam(volumetricMaterial, kernel, ShaderIDs._VolumetricGlobalIndirectArgsBuffer, LocalVolumetricFogManager.manager.globalIndirectBuffer);
                cmd.SetComputeBufferParam(volumetricMaterial, kernel, ShaderIDs._VolumetricGlobalIndirectionBuffer, LocalVolumetricFogManager.manager.globalIndirectionBuffer);
                cmd.SetComputeBufferParam(volumetricMaterial, kernel, ShaderIDs._VolumetricVisibleGlobalIndicesBuffer, VolumetricFogHDRPRenderer.m_VisibleVolumeGlobalIndices);
                cmd.SetComputeBufferParam(volumetricMaterial, kernel, ShaderIDs._VolumetricMaterialData, LocalVolumetricFogManager.manager.volumetricMaterialDataBuffer);

                // 设置Int参数
                cmd.SetComputeIntParam(volumetricMaterial, ShaderIDs._VolumeCount, volumeCount);
                cmd.SetComputeIntParam(volumetricMaterial, ShaderIDs._MaxSliceCount, maxSliceCount);
                cmd.SetComputeIntParam(volumetricMaterial, ShaderIDs._VolumetricViewCount, viewCount);

                // 更新光源组件体积光部分传入GPU的数据
                vLightParams.UpadateSetVolumetricMainLightParams(cmd, lightData.universalLightData);
                vLightParams.UpadateSetVolumetricAdditionalLightParams(cmd, lightData.universalLightData);

                // Dispatch
                int dispatchXCount = Mathf.Max(1, Mathf.CeilToInt((float)(volumeCount * viewCount) / 32.0f));
                cmd.DispatchCompute(volumetricMaterial, kernel, dispatchXCount, 1, 1);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }


        class VolumetricFogVoxelizationPassData
        {
            public List<LocalVolumetricFog> volumetricFogs;
            public int maxSliceCount;
            public int viewCount;

            // public ShaderVariablesVolumetric volumetricCB;
            // public VolumetricGlobalParams volumetricGlobalCB;
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
                // passData.volumetricCB = m_ShaderVariablesVolumetricCB;
                passData.fogOverdrawDebugEnabled = fogOverdrawDebugEnabled;
                passData.volumetricMaterialCS = volumetricMaterial;
                passData.computeRenderingParametersKernel = passData.volumetricMaterialCS.FindKernel("ComputeVolumetricMaterialRenderingParameters");
                passData.visibleVolumeBoundsBuffer = VolumetricFogHDRPRenderer.m_VisibleVolumeBoundsBuffer;
                passData.globalIndirectBuffer = LocalVolumetricFogManager.manager.globalIndirectBuffer;
                passData.globalIndirectionBuffer = LocalVolumetricFogManager.manager.globalIndirectionBuffer;
                passData.volumetricFogs = VolumetricFogHDRPRenderer.m_VisibleLocalVolumetricFogVolumes;
                passData.materialDataBuffer = LocalVolumetricFogManager.manager.volumetricMaterialDataBuffer;
                passData.maxSliceCount = (int)m_ShaderVariablesVolumetricCB._MaxSliceCount;
                passData.viewCount = 1;
                passData.visibleVolumeGlobalIndices = VolumetricFogHDRPRenderer.m_VisibleVolumeGlobalIndices;
                // passData.volumetricGlobalCB = VolumetricFogHDRPRenderer.volumetricGlobalCB;
                builder.SetRenderFunc((VolumetricFogVoxelizationPassData data, ComputeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(VolumetricFogVoxelizationPassData data, ComputeGraphContext context)
        {
            int volumeCount = data.volumetricFogs.Count;

            var computeShader = data.volumetricMaterialCS;
            int kernel = data.computeRenderingParametersKernel;
            context.cmd.SetComputeBufferParam(computeShader, kernel, ShaderIDs._VolumeBounds, data.visibleVolumeBoundsBuffer);
            context.cmd.SetComputeBufferParam(computeShader, kernel, ShaderIDs._VolumetricGlobalIndirectArgsBuffer, data.globalIndirectBuffer);
            context.cmd.SetComputeBufferParam(computeShader, kernel, ShaderIDs._VolumetricGlobalIndirectionBuffer, data.globalIndirectionBuffer);
            context.cmd.SetComputeBufferParam(computeShader, kernel, ShaderIDs._VolumetricVisibleGlobalIndicesBuffer, data.visibleVolumeGlobalIndices);
            context.cmd.SetComputeBufferParam(computeShader, kernel, ShaderIDs._VolumetricMaterialData, data.materialDataBuffer);
            context.cmd.SetComputeIntParam(computeShader, ShaderIDs._VolumeCount, volumeCount);
            context.cmd.SetComputeIntParam(computeShader, ShaderIDs._MaxSliceCount, data.maxSliceCount);
            context.cmd.SetComputeIntParam(computeShader, ShaderIDs._VolumetricViewCount, data.viewCount);
            //更新光源组件体积光部分传入GPU的数据
            var cmd = CommandBufferHelpersExtensions.GetNativeCommandBuffer(context.cmd);
            vLightParams.UpadateSetVolumetricMainLightParams(cmd, data.lightData);
            vLightParams.UpadateSetVolumetricAdditionalLightParams(cmd, data.lightData);

            // ConstantBuffer.PushGlobal(data.volumetricCB, ShaderIDs._ShaderVariablesVolumetric);
            int dispatchXCount = Mathf.Max(1, Mathf.CeilToInt((float)(volumeCount * data.viewCount) / 32.0f));
            context.cmd.DispatchCompute(data.volumetricMaterialCS, data.computeRenderingParametersKernel, dispatchXCount, 1, 1);
            // ConstantBuffer.PushGlobal(data.volumetricGlobalCB, ShaderIDs._VolumetricGlobalParams);

            // context.cmd.SetGlobalBuffer(ShaderIDs._VolumetricGlobalIndirectionBuffer, data.globalIndirectionBuffer);
        }

    }
}
