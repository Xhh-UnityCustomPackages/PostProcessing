using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

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
        

        private RTHandle densityBuffer;

        public void Dispose()
        {
            densityBuffer?.Release();
            densityBuffer = null;
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
            
            RenderingUtils.ReAllocateHandleIfNeeded(ref densityBuffer, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "VBufferDensity");
            
            
            int voxelizationKernel = 0;
           
            
            var resolution = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
            // VolumetricFogRenderer.UpdateShaderVariableslVolumetrics(ref VolumetricFogRenderer.m_ShaderVariablesVolumetricCB, postProcessData, resolution, maxSliceCount, true);
            var volumetricCB = VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB;
            // var volumetricGlobalCB = VolumetricFogRenderer.GetVolumetricGlobalParams();
            
            //TODO:需要水下再支持
            bool water = false;
            CoreUtils.SetKeyword(voxelizationCS, "SUPPORT_WATER_ABSORPTION", water);

            var cmd = CommandBufferPool.Get("VolumetricFog");
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetComputeTextureParam(voxelizationCS, voxelizationKernel, VolumetricFogShaderIDs._VBufferDensity, densityBuffer);

                // 尝试使用 PushGlobal，因为在某些 SRP 版本中 Compute Shader 的 CBuffer 绑定可能需要全局方式
                // 或者 ConstantBuffer.Push(cmd, ...) 对特定 Shader 的绑定存在问题
                // 非RenderGraph需要这样写
                ConstantBuffer.Push(volumetricCB, voxelizationCS, VolumetricFogShaderIDs._ShaderVariablesVolumetric);
                
                
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

            public ShaderVariablesVolumetric volumetricCB;
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
                passData.volumetricCB = VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB;
                //passData.volumetricGlobalParamsCB = GetVolumetricGlobalParams();
                var s_CurrentVolumetricBufferSize = VolumetricFogHDRPRenderer.s_CurrentVolumetricBufferSize;
                passData.densityBuffer = renderGraph.CreateTexture(new TextureDesc(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, false, false)
                    { slices = s_CurrentVolumetricBufferSize.z, format = GraphicsFormat.R16G16B16A16_SFloat, dimension = TextureDimension.Tex3D, enableRandomWrite = true, name = "VBufferDensity" });
                
                builder.UseTexture(passData.densityBuffer, AccessFlags.Write);
                
                //passData.volumetricAmbientProbeBuffer = CreateGraphicsBuffer(passData.volumetricAmbientProbeBuffer);
                CoreUtils.SetKeyword(passData.voxelizationCS, "SUPPORT_WATER_ABSORPTION", passData.water);
                //判断是否支持异步
                builder.EnableAsyncCompute(SystemInfo.supportsAsyncCompute);

                builder.SetRenderFunc((HeightFogVoxelizationPassData data, ComputeGraphContext ctx) =>
                    {
                        ctx.cmd.SetComputeTextureParam(data.voxelizationCS, data.voxelizationKernel, VolumetricFogShaderIDs._VBufferDensity, data.densityBuffer);
                        //ctx.cmd.SetComputeBufferParam(data.voxelizationCS, data.voxelizationKernel, VolumetricFogShaderIDs._VolumeAmbientProbeBuffer, data.volumetricAmbientProbeBuffer);

                        ConstantBuffer.Push(data.volumetricCB, data.voxelizationCS, VolumetricFogShaderIDs._ShaderVariablesVolumetric);
                        //ConstantBuffer.PushGlobal(data.volumetricCB, VolumetricFogShaderIDs._ShaderVariablesVolumetric);
                        // The shader defines GROUP_SIZE_1D = 8.
                        //
                        ctx.cmd.DispatchCompute(data.voxelizationCS, data.voxelizationKernel, ((int)data.resolution.x + 7) / 8, ((int)data.resolution.y + 7) / 8, 1);
                    }
                );
                
                // m_DensityBuffer = passData.densityBuffer;
            }
        }
    }
}