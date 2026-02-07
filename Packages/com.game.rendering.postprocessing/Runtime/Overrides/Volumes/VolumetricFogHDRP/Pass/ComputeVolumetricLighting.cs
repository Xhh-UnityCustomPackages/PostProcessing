using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

using ShaderIDs = Game.Core.PostProcessing.VolumetricFogShaderIDs;

namespace Game.Core.PostProcessing
{
    public class ComputeVolumetricLighting : ScriptableRenderPass, IDisposable
    {
        private PostProcessData postProcessData;
        private ComputeShader m_VolumetricLightingCS;
        private ComputeShader m_VolumetricLightingFilteringCS;

        public ComputeVolumetricLighting(PostProcessData postProcessData)
        {
            this.postProcessData = postProcessData;
            
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
            m_VolumetricLightingCS = runtimeShaders.volumetricFogLighting;
            m_VolumetricLightingFilteringCS = runtimeShaders.volumetricLightingFilter;
        }
        
        private RTHandle m_IntermediateLightingBuffer;

        public void Dispose()
        {
            m_IntermediateLightingBuffer?.Release();
            m_IntermediateLightingBuffer = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get("VolumetricFog");

            int frameIndex = (int)VolumetricFogHDRPRenderer.VolumetricFrameIndex(postProcessData);
            var currIdx = (frameIndex + 0) & 1;
            var prevIdx = (frameIndex + 1) & 1;

            var currParams = postProcessData.vBufferParams[currIdx];
            var fog = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();

            var camera = renderingData.cameraData.camera;

            bool volumeAllowsReprojection = ((int)fog.denoisingMode.value & (int)VolumetricFogHDRP.FogDenoisingMode.Reprojection) != 0;
            var tiledLighting = false;
            bool enableAnisotropy = false;
            bool optimal = currParams.voxelSize == 8;
            var volumetricLightingCS = m_VolumetricLightingCS;
            var volumetricLightingFilteringCS = m_VolumetricLightingFilteringCS;
            volumetricLightingCS.shaderKeywords = null;
            volumetricLightingFilteringCS.shaderKeywords = null;
            var nearPlane = camera.nearClipPlane;
            var enableReprojection = postProcessData.IsVolumetricReprojectionEnabled() && volumeAllowsReprojection;
            var prevCameraPos = postProcessData.prevPos;
            
            var prevVP = camera.previousViewProjectionMatrix;
            CoreUtils.SetKeyword(volumetricLightingCS, "LIGHTLOOP_DISABLE_TILE_AND_CLUSTER", !tiledLighting);
            CoreUtils.SetKeyword(volumetricLightingCS, "ENABLE_REPROJECTION", enableReprojection);
            //Debug.Log(passData.enableReprojection); 
            CoreUtils.SetKeyword(volumetricLightingCS, "ENABLE_ANISOTROPY", enableAnisotropy);
            CoreUtils.SetKeyword(volumetricLightingCS, "VL_PRESET_OPTIMAL", optimal);
            CoreUtils.SetKeyword(volumetricLightingCS, "SUPPORT_LOCAL_LIGHTS", !fog.directionalLightsOnly.value);
            //CoreUtils.SetKeyword(passData.volumetricLightingCS, "_ADDITIONAL_LIGHT_SHADOWS", shadowData.additionalLightShadowsEnabled);
            CoreUtils.SetKeyword(volumetricLightingCS, "_RECEIVE_SHADOWS_OFF", false);
            //TODO:水下实时焦散
            CoreUtils.SetKeyword(volumetricLightingCS, "SUPPORT_WATER_ABSORPTION", false);
            
            var volumetricLightingKernel = volumetricLightingCS.FindKernel("VolumetricLighting");
            var volumetricFilteringKernel = volumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");

            var cvp = currParams.viewportSize;
            var resolution = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
            bool filterVolume = ((int)fog.denoisingMode.value & (int)VolumetricFogHDRP.FogDenoisingMode.Gaussian) != 0;
            int sliceCount = (int)(cvp.z);
            bool filteringNeedsExtraBuffer = !(SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.LoadStore));

            var s_CurrentVolumetricBufferSize = VolumetricFogHDRPRenderer.s_CurrentVolumetricBufferSize;
            RenderTextureDescriptor desc = new RenderTextureDescriptor(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, GraphicsFormat.R16G16B16A16_SFloat, 0);
            desc.dimension = TextureDimension.Tex3D;
            desc.volumeDepth = s_CurrentVolumetricBufferSize.z;
            desc.enableRandomWrite = true;
            desc.msaaSamples = 1;

            RTHandle lightingBufferHandle = null;
            RTHandle filteringBufferHandle = null;

            if (filterVolume && filteringNeedsExtraBuffer)
            {
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_IntermediateLightingBuffer, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "VBufferLighting");
                RenderingUtils.ReAllocateHandleIfNeeded(ref VolumetricFogHDRPRenderer.m_LightingBuffer, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "VBufferLightingFiltered");
                
                lightingBufferHandle = m_IntermediateLightingBuffer;
                filteringBufferHandle = VolumetricFogHDRPRenderer.m_LightingBuffer;

                CoreUtils.SetKeyword(volumetricLightingFilteringCS, "NEED_SEPARATE_OUTPUT", true);
            }
            else
            {
                RenderingUtils.ReAllocateHandleIfNeeded(ref VolumetricFogHDRPRenderer.m_LightingBuffer, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "VBufferLighting");
                lightingBufferHandle = VolumetricFogHDRPRenderer.m_LightingBuffer;
                
                CoreUtils.SetKeyword(volumetricLightingFilteringCS, "NEED_SEPARATE_OUTPUT", false);
            }

            if (enableReprojection)
            {
                if (postProcessData.volumetricValidFrames > 1)
                    postProcessData.volumetricHistoryIsValid = true; // For the next frame..
                else
                    postProcessData.volumetricValidFrames++;
            }

            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Dispatch Volumetric Lighting
                int volumetricNearPlaneID = Shader.PropertyToID("_VolumetricNearPlane");
                int goupsizeID = Shader.PropertyToID("_LightingGroupSize");
                int threadX = PostProcessingUtils.DivRoundUp((int)resolution.x, 8);
                int threadY = PostProcessingUtils.DivRoundUp((int)resolution.y, 8);
                Vector4 groupSize = new Vector4(threadX, threadY, 0, 0);

                cmd.SetComputeTextureParam(volumetricLightingCS, volumetricLightingKernel, ShaderIDs._VBufferDensity, VolumetricFogHDRPRenderer.m_DensityBuffer); // Read
                cmd.SetComputeTextureParam(volumetricLightingCS, volumetricLightingKernel, ShaderIDs._VBufferLighting, lightingBufferHandle); // Write
                cmd.SetComputeFloatParam(volumetricLightingCS, volumetricNearPlaneID, nearPlane);
                cmd.SetComputeVectorParam(volumetricLightingCS, goupsizeID, groupSize);

                cmd.SetComputeIntParam(volumetricLightingCS, ShaderIDs._VBufferSliceCount, (int)VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferSliceCount);
                cmd.SetComputeVectorParam(volumetricLightingCS, ShaderIDs._VBufferDistanceDecodingParams, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferDistanceDecodingParams);
                cmd.SetComputeFloatParam(volumetricLightingCS, ShaderIDs._VBufferRcpSliceCount, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferRcpSliceCount);
                cmd.SetComputeVectorParam(volumetricLightingCS, ShaderIDs._VBufferViewportSize, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferViewportSize);

                if (enableReprojection)
                {
                    var feedbackBuffer = postProcessData.volumetricHistoryBuffers[currIdx];
                    var historyBuffer = postProcessData.volumetricHistoryBuffers[prevIdx];

                    cmd.SetComputeVectorParam(volumetricLightingCS, ShaderIDs._PrevCamPosRWS, prevCameraPos);
                    cmd.SetComputeMatrixParam(volumetricLightingCS, ShaderIDs._PreVPMatrix, prevVP);
                    cmd.SetComputeTextureParam(volumetricLightingCS, volumetricLightingKernel, ShaderIDs._VBufferHistory, historyBuffer); // Read
                    cmd.SetComputeTextureParam(volumetricLightingCS, volumetricLightingKernel, ShaderIDs._VBufferFeedback, feedbackBuffer); // Write
                }

                ConstantBuffer.Push(VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB, volumetricLightingCS, ShaderIDs._ShaderVariablesVolumetric);

                cmd.DispatchCompute(volumetricLightingCS, volumetricLightingKernel, threadX, threadY, 1);

                // Dispatch Filtering
                if (filterVolume)
                {
                    ConstantBuffer.Push(VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB, volumetricLightingFilteringCS, ShaderIDs._ShaderVariablesVolumetric);
                    cmd.SetComputeTextureParam(volumetricLightingFilteringCS, volumetricFilteringKernel, ShaderIDs._VBufferLighting, lightingBufferHandle);
                    if (filteringNeedsExtraBuffer)
                    {
                        cmd.SetComputeTextureParam(volumetricLightingFilteringCS, volumetricFilteringKernel, ShaderIDs._VBufferLightingFiltered, filteringBufferHandle);
                    }

                    cmd.DispatchCompute(volumetricLightingFilteringCS, volumetricFilteringKernel, threadX, threadY, sliceCount);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        class VolumetricLightingPassData
        {
            public ComputeShader volumetricLightingCS;
            public ComputeShader volumetricLightingFilteringCS;
            public int volumetricLightingKernel;
            public int volumetricFilteringKernel;
            public bool tiledLighting;
            public Vector4 resolution;
            public bool enableReprojection;
            public int viewCount;
            public int sliceCount;
            public bool filterVolume;
            public bool filteringNeedsExtraBuffer;
            public ShaderVariablesVolumetric volumetricCB;
            //public ShaderVariablesLightList lightListCB;
            public float nearPlane;
            public TextureHandle densityBuffer;
            //public TextureHandle depthTexture;
            public TextureHandle lightingBuffer;
            public TextureHandle filteringOutputBuffer;
            public TextureHandle maxZBuffer;
            public TextureHandle historyBuffer;
            public TextureHandle feedbackBuffer;
            public Vector3 prevCameraPos;
            public Matrix4x4 prevVP;
            //public BufferHandle bigTileVolumetricLightListBuffer;
            // public GraphicsBuffer volumetricAmbientProbeBuffer;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            using (var builder = renderGraph.AddComputePass<VolumetricLightingPassData>("Volumetric Lighting", out var passData))
            {
                builder.AllowPassCulling(false);
                int frameIndex = (int)VolumetricFogHDRPRenderer.VolumetricFrameIndex(postProcessData);
                var currIdx = (frameIndex + 0) & 1;
                var prevIdx = (frameIndex + 1) & 1;

                var currParams = postProcessData.vBufferParams[currIdx];
                var fog = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();

                Camera cam = cameraData.camera;

                bool volumeAllowsReprojection = ((int)fog.denoisingMode.value & (int)VolumetricFogHDRP.FogDenoisingMode.Reprojection) != 0;
                passData.tiledLighting = false;
                bool enableAnisotropy = false;
                bool optimal = currParams.voxelSize == 8;
                passData.volumetricLightingCS = m_VolumetricLightingCS;
                passData.volumetricLightingFilteringCS = m_VolumetricLightingFilteringCS;
                passData.volumetricLightingCS.shaderKeywords = null;
                passData.volumetricLightingFilteringCS.shaderKeywords = null;
                passData.nearPlane = cameraData.camera.nearClipPlane;
                passData.enableReprojection = postProcessData.IsVolumetricReprojectionEnabled() && volumeAllowsReprojection;
                passData.prevCameraPos = postProcessData.prevPos;
                passData.prevVP = cam.previousViewProjectionMatrix;
           
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "LIGHTLOOP_DISABLE_TILE_AND_CLUSTER", !passData.tiledLighting);
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "ENABLE_REPROJECTION", passData.enableReprojection);
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "ENABLE_ANISOTROPY", enableAnisotropy);
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "VL_PRESET_OPTIMAL", optimal);
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "SUPPORT_LOCAL_LIGHTS", !fog.directionalLightsOnly.value);
                //CoreUtils.SetKeyword(passData.volumetricLightingCS, "_ADDITIONAL_LIGHT_SHADOWS", shadowData.additionalLightShadowsEnabled);
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "_RECEIVE_SHADOWS_OFF", false);
                //TODO:水下实时焦散
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "SUPPORT_WATER_ABSORPTION", false);

                passData.volumetricLightingKernel = passData.volumetricLightingCS.FindKernel("VolumetricLighting");
                passData.volumetricFilteringKernel = passData.volumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");

                var cvp = currParams.viewportSize;

                passData.resolution = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
                passData.viewCount = 1;
                passData.filterVolume = ((int)fog.denoisingMode.value & (int)VolumetricFogHDRP.FogDenoisingMode.Gaussian) != 0;
                passData.sliceCount = (int)(cvp.z);
                passData.filteringNeedsExtraBuffer = !(SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.LoadStore));

                passData.volumetricCB = VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB;
                passData.maxZBuffer = VolumetricFogHDRPRenderer.m_MaxZTexture;
                passData.densityBuffer = VolumetricFogHDRPRenderer.m_DensityTexture;
                builder.UseTexture(passData.densityBuffer, AccessFlags.Read);
                var s_CurrentVolumetricBufferSize = VolumetricFogHDRPRenderer.s_CurrentVolumetricBufferSize;
                passData.lightingBuffer = renderGraph.CreateTexture(new TextureDesc(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, false, false)
                {
                    slices = s_CurrentVolumetricBufferSize.z,
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    dimension = TextureDimension.Tex3D,
                    enableRandomWrite = true,
                    name = "VBufferLighting"
                });
                builder.UseTexture(passData.lightingBuffer, AccessFlags.Write);

                if (passData.filterVolume && passData.filteringNeedsExtraBuffer)
                {
                    passData.filteringOutputBuffer = renderGraph.CreateTexture(new TextureDesc(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, false, false)
                    {
                        slices = s_CurrentVolumetricBufferSize.z,
                        format = GraphicsFormat.R16G16B16A16_SFloat,
                        dimension = TextureDimension.Tex3D,
                        enableRandomWrite = true,
                        name = "VBufferLightingFiltered"
                    });
                    builder.UseTexture(passData.filteringOutputBuffer, AccessFlags.Write);

                    CoreUtils.SetKeyword(passData.volumetricLightingFilteringCS, "NEED_SEPARATE_OUTPUT", passData.filteringNeedsExtraBuffer);
                }

                if (passData.enableReprojection)
                {
                    passData.feedbackBuffer = renderGraph.ImportTexture(postProcessData.volumetricHistoryBuffers[currIdx]);
                    builder.UseTexture(passData.feedbackBuffer, AccessFlags.Write);

                    passData.historyBuffer = renderGraph.ImportTexture(postProcessData.volumetricHistoryBuffers[prevIdx]);
                    builder.UseTexture(passData.historyBuffer, AccessFlags.Read);
                }
                
                //TODO:间接光
                //passData.volumetricAmbientProbeBuffer = m_SkyManager.GetVolumetricAmbientProbeBuffer(hdCamera);
                builder.SetRenderFunc((VolumetricLightingPassData data, ComputeGraphContext context) => ExecutePass(data, context));

                if (passData.enableReprojection && postProcessData.volumetricValidFrames > 1)
                    postProcessData.volumetricHistoryIsValid = true; // For the next frame..
                else
                    postProcessData.volumetricValidFrames++;
                
                if (passData.filterVolume && passData.filteringNeedsExtraBuffer)
                {
                    VolumetricFogHDRPRenderer.m_LightingTexture = passData.filteringOutputBuffer;
                }
                else
                {
                    VolumetricFogHDRPRenderer.m_LightingTexture = passData.lightingBuffer;
                }
            }
        }

        static void ExecutePass(VolumetricLightingPassData data, ComputeGraphContext context)
        {
            int volumetricNearPlaneID = Shader.PropertyToID("_VolumetricNearPlane");
            int goupsizeID = Shader.PropertyToID("_LightingGroupSize");
            int threadX = PostProcessingUtils.DivRoundUp((int)data.resolution.x, 8);
            int threadY = PostProcessingUtils.DivRoundUp((int)data.resolution.y, 8);
            Vector4 groupSize = new Vector4(threadX, threadY, 0, 0);
            var computeShader = data.volumetricLightingCS;
            int kernel = data.volumetricLightingKernel;
            //context.cmd.SetComputeTextureParam(data.volumetricLightingCS, kernel, ShaderIDs._CameraDepthTexture, data.depthTexture);  // Read
            context.cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs._VBufferDensity, data.densityBuffer); // Read
            context.cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs._VBufferLighting, data.lightingBuffer); // Write
            context.cmd.SetComputeFloatParam(computeShader, volumetricNearPlaneID, data.nearPlane);
            context.cmd.SetComputeVectorParam(computeShader, goupsizeID, groupSize);

            context.cmd.SetComputeIntParam(computeShader, ShaderIDs._VBufferSliceCount, (int)VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferSliceCount);
            context.cmd.SetComputeVectorParam(computeShader, ShaderIDs._VBufferDistanceDecodingParams, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferDistanceDecodingParams);
            context.cmd.SetComputeFloatParam(computeShader, ShaderIDs._VBufferRcpSliceCount, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferRcpSliceCount);
            context.cmd.SetComputeVectorParam(computeShader, ShaderIDs._VBufferViewportSize, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferViewportSize);
            
            if (data.enableReprojection)
            {
                context.cmd.SetComputeVectorParam(computeShader, ShaderIDs._PrevCamPosRWS, data.prevCameraPos);
                context.cmd.SetComputeMatrixParam(computeShader, ShaderIDs._PreVPMatrix, data.prevVP);
                context.cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs._VBufferHistory, data.historyBuffer); // Read
                context.cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs._VBufferFeedback, data.feedbackBuffer); // Write
            }

            ConstantBuffer.Push(data.volumetricCB, computeShader, ShaderIDs._ShaderVariablesVolumetric);

            context.cmd.DispatchCompute(computeShader, kernel, threadX, threadY, data.viewCount);

            if (data.filterVolume)
            {
                computeShader = data.volumetricLightingFilteringCS;
                kernel = data.volumetricFilteringKernel;
                ConstantBuffer.Push(data.volumetricCB, computeShader, ShaderIDs._ShaderVariablesVolumetric);
                context.cmd.SetComputeVectorParam(computeShader, ShaderIDs._VBufferViewportSize, VolumetricFogHDRPRenderer.volumetricGlobalCB._VBufferViewportSize);
                context.cmd.SetComputeVectorParam(computeShader, ShaderIDs._PrevCamPosRWS, data.prevCameraPos);
                context.cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs._VBufferLighting, data.lightingBuffer);
                if (data.filteringNeedsExtraBuffer)
                {
                    context.cmd.SetComputeTextureParam(computeShader, kernel, ShaderIDs._VBufferLightingFiltered, data.filteringOutputBuffer);
                }

                context.cmd.DispatchCompute(computeShader, kernel, threadX, threadY, data.sliceCount);
            }
        }

    }
}