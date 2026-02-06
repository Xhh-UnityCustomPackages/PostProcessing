using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

using ShaderIDs = Game.Core.PostProcessing.VolumetricFogShaderIDs;

namespace Game.Core.PostProcessing
{
    public class ComputeHeightFogVoxel : ScriptableRenderPass, IDisposable
    {
        private ComputeShader voxelizationCS;
        private PostProcessData postProcessData;
        
        public ComputeHeightFogVoxel(PostProcessData postProcessData)
        {
            this.postProcessData = postProcessData;

            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
            voxelizationCS = runtimeShaders.volumeVoxelization;
        }
        

        public void Dispose()
        {
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var fog = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();
            VolumetricFogHDRPRenderer.ComputeVolumetricFogSliceCountAndScreenFraction(fog, out var maxSliceCount, out _);

            int frameIndex = (int)VolumetricFogHDRPRenderer.VolumetricFrameIndex(postProcessData);
            var currIdx = (frameIndex + 0) & 1;
            var currentParams = postProcessData.vBufferParams[currIdx];
            var cvp = currentParams.viewportSize;

            var s_CurrentVolumetricBufferSize = VolumetricFogHDRPRenderer.s_CurrentVolumetricBufferSize;
            
            var desc = new RenderTextureDescriptor(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, GraphicsFormat.R16G16B16A16_SFloat, 0);
            desc.volumeDepth = s_CurrentVolumetricBufferSize.z;
            desc.dimension = TextureDimension.Tex3D;
            desc.enableRandomWrite = true;
            desc.msaaSamples = 1;
            
            RenderingUtils.ReAllocateHandleIfNeeded(ref VolumetricFogHDRPRenderer.m_DensityBuffer, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "VBufferDensity");
            
            int voxelizationKernel = 0;
            
            var resolution = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
            // VolumetricFogRenderer.UpdateShaderVariableslVolumetrics(ref VolumetricFogRenderer.m_ShaderVariablesVolumetricCB, postProcessData, resolution, maxSliceCount, true);
            // var volumetricGlobalCB = VolumetricFogRenderer.GetVolumetricGlobalParams();
            
            //TODO:需要水下再支持
            bool water = false;
            CoreUtils.SetKeyword(voxelizationCS, "SUPPORT_WATER_ABSORPTION", water);

            var cmd = CommandBufferPool.Get("VolumetricFog");
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetComputeTextureParam(voxelizationCS, voxelizationKernel, ShaderIDs._VBufferDensity, VolumetricFogHDRPRenderer.m_DensityBuffer);
                
                cmd.SetComputeIntParam(voxelizationCS, ShaderIDs._VBufferSliceCount, (int)VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferSliceCount);
                cmd.SetComputeVectorParam(voxelizationCS, ShaderIDs._HeightFogBaseScattering, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogBaseScattering);
                cmd.SetComputeVectorParam(voxelizationCS, ShaderIDs._HeightFogExponents, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogExponents);
                cmd.SetComputeFloatParam(voxelizationCS, ShaderIDs._HeightFogBaseHeight, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogBaseHeight);
                cmd.SetComputeFloatParam(voxelizationCS, ShaderIDs._HeightFogBaseExtinction, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogBaseExtinction);
                
                cmd.DispatchCompute(voxelizationCS, voxelizationKernel, ((int)resolution.x + 7) / 8, ((int)resolution.y + 7) / 8, 1);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private class HeightFogVoxelizationPassData
        {
            public ComputeShader voxelizationCS;
            public int voxelizationKernel;

            public Vector4 resolution;

            // public ShaderVariablesVolumetric volumetricCB;
            //public VolumetricGlobalParams volumetricGlobalParamsCB;

            public TextureHandle densityBuffer;
            public GraphicsBuffer volumetricAmbientProbeBuffer;

            // Underwater fog
            //TODO:需要水下再支持
            public bool water = false;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "ComputeHeightVoxel";

            var fog = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();
            VolumetricFogHDRPRenderer.ComputeVolumetricFogSliceCountAndScreenFraction(fog, out var maxSliceCount, out _);
            
            int frameIndex = (int)VolumetricFogHDRPRenderer.VolumetricFrameIndex(postProcessData);
            var currIdx = (frameIndex + 0) & 1;
            var currParams = postProcessData.vBufferParams[currIdx];

            using (var builder = renderGraph.AddComputePass<HeightFogVoxelizationPassData>(passName, out var passData))
            {
                builder.AllowPassCulling(false);
                passData.voxelizationCS = voxelizationCS;
                passData.voxelizationKernel = 0;
                var cvp = currParams.viewportSize;

                passData.resolution = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
                // VolumetricFogRenderer.UpdateShaderVariableslVolumetrics(ref VolumetricFogRenderer.m_ShaderVariablesVolumetricCB, postProcessData, passData.resolution, maxSliceCount, true);
                // passData.volumetricCB = VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB;
                //passData.volumetricGlobalParamsCB = GetVolumetricGlobalParams();
                var s_CurrentVolumetricBufferSize = VolumetricFogHDRPRenderer.s_CurrentVolumetricBufferSize;
                var desc = new TextureDesc(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, false, false);
                desc.format = GraphicsFormat.R16G16B16A16_SFloat;
                desc.slices = s_CurrentVolumetricBufferSize.z;
                desc.dimension = TextureDimension.Tex3D;
                desc.enableRandomWrite = true;
                desc.name = "VBufferDensity";
                passData.densityBuffer = renderGraph.CreateTexture(desc);

                builder.UseTexture(passData.densityBuffer, AccessFlags.Write);
                
                //passData.volumetricAmbientProbeBuffer = CreateGraphicsBuffer(passData.volumetricAmbientProbeBuffer);
                CoreUtils.SetKeyword(passData.voxelizationCS, "SUPPORT_WATER_ABSORPTION", passData.water);
                //判断是否支持异步
                builder.EnableAsyncCompute(postProcessData.EnableAsyncCompute);

                builder.SetRenderFunc((HeightFogVoxelizationPassData data, ComputeGraphContext ctx) =>
                    {
                        var computeShader = data.voxelizationCS;
                        int kernel = data.voxelizationKernel;
                        ctx.cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs._VBufferDensity, data.densityBuffer);

                        ctx.cmd.SetComputeIntParam(computeShader, ShaderIDs._VBufferSliceCount, (int)VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferSliceCount);
                        ctx.cmd.SetComputeVectorParam(computeShader, ShaderIDs._HeightFogBaseScattering, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogBaseScattering);
                        ctx.cmd.SetComputeVectorParam(computeShader, ShaderIDs._HeightFogExponents, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogExponents);
                        ctx.cmd.SetComputeFloatParam(computeShader, ShaderIDs._HeightFogBaseHeight, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogBaseHeight);
                        ctx.cmd.SetComputeFloatParam(computeShader, ShaderIDs._HeightFogBaseExtinction, VolumetricFogHDRPRenderer.volumetricGlobalCB._HeightFogBaseExtinction);

                        ctx.cmd.DispatchCompute(computeShader, kernel, ((int)data.resolution.x + 7) / 8, ((int)data.resolution.y + 7) / 8, 1);
                    }
                );
                
                VolumetricFogHDRPRenderer.m_DensityTexture = passData.densityBuffer;
            }
        }
    }
}