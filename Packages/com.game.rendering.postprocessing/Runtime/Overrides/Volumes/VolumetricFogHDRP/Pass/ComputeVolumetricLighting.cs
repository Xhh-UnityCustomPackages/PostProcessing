using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

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
            
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogResources>();
            m_VolumetricLightingCS = runtimeShaders.volumetricFogLighting;
            m_VolumetricLightingFilteringCS = runtimeShaders.volumetricLightingFilter;
        }
        
        public void Dispose()
        {
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
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

            
            // var maxZBuffer = m_MaxZHandle;
            // var densityBuffer = m_DensityBuffer;
            // builder.UseTexture(passData.densityBuffer, AccessFlags.Read);
            // passData.lightingBuffer = renderGraph.CreateTexture(new TextureDesc(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, false, false)
            //     { slices = s_CurrentVolumetricBufferSize.z, format = GraphicsFormat.R16G16B16A16_SFloat, dimension = TextureDimension.Tex3D, enableRandomWrite = true, name = "VBufferLighting" });
            // builder.UseTexture(passData.lightingBuffer, AccessFlags.Write);
            
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

                //Debug.Log("Curr" + currIdx);
                //Debug.Log("Prev" + prevIdx);

                var currParams = postProcessData.vBufferParams[currIdx];
                var fog = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();

                Camera cam = cameraData.camera;

                //Debug.Log(cam.name + "curr" + currIdx);

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
                //Debug.Log(hdCamera.camera.name);
                passData.prevVP = cam.previousViewProjectionMatrix;
                //Debug.Log(hdCamera.prevCameraVP);
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "LIGHTLOOP_DISABLE_TILE_AND_CLUSTER", !passData.tiledLighting);
                CoreUtils.SetKeyword(passData.volumetricLightingCS, "ENABLE_REPROJECTION", passData.enableReprojection);
                //Debug.Log(passData.enableReprojection); 
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
                
                // VolumetricFogRenderer.ComputeVolumetricFogSliceCountAndScreenFraction(fog, out var maxSliceCount, out _);
                // VolumetricFogRenderer.UpdateShaderVariableslVolumetrics(ref VolumetricFogRenderer.m_ShaderVariablesVolumetricCB, postProcessData, passData.resolution, maxSliceCount);
                passData.volumetricCB = VolumetricFogHDRPRenderer.m_ShaderVariablesVolumetricCB;
                // passData.maxZBuffer = m_MaxZHandle;
                // passData.densityBuffer = m_DensityBuffer;
                // builder.UseTexture(passData.densityBuffer, AccessFlags.Read);
                var s_CurrentVolumetricBufferSize = VolumetricFogHDRPRenderer.s_CurrentVolumetricBufferSize;
                passData.lightingBuffer = renderGraph.CreateTexture(new TextureDesc(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, false, false)
                    { slices = s_CurrentVolumetricBufferSize.z, format = GraphicsFormat.R16G16B16A16_SFloat, dimension = TextureDimension.Tex3D, enableRandomWrite = true, name = "VBufferLighting" });
                builder.UseTexture(passData.lightingBuffer, AccessFlags.Write);
                
                if (passData.filterVolume && passData.filteringNeedsExtraBuffer)
                {
                    passData.filteringOutputBuffer = renderGraph.CreateTexture(new TextureDesc(s_CurrentVolumetricBufferSize.x, s_CurrentVolumetricBufferSize.y, false, false)
                        { slices = s_CurrentVolumetricBufferSize.z, format = GraphicsFormat.R16G16B16A16_SFloat, dimension = TextureDimension.Tex3D, enableRandomWrite = true, name = "VBufferLightingFiltered" });
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
            }
        }

        static void ExecutePass(VolumetricLightingPassData data, ComputeGraphContext context)
        {
            int volumetricNearPlaneID = Shader.PropertyToID("_VolumetricNearPlane");
            int goupsizeID = Shader.PropertyToID("_LightingGroupSize");
            Vector4 groupSize = new Vector4(((int)data.resolution.x + 7) / 8, ((int)data.resolution.y + 7) / 8, 0, 0);
            //context.cmd.SetComputeTextureParam(data.volumetricLightingCS, data.volumetricLightingKernel, VolumetricFogShaderIDs._CameraDepthTexture, data.depthTexture);  // Read
            context.cmd.SetComputeTextureParam(data.volumetricLightingCS, data.volumetricLightingKernel, VolumetricFogShaderIDs._VBufferDensity, data.densityBuffer);  // Read
            context.cmd.SetComputeTextureParam(data.volumetricLightingCS, data.volumetricLightingKernel, VolumetricFogShaderIDs._VBufferLighting, data.lightingBuffer); // Write
            context.cmd.SetComputeFloatParam(data.volumetricLightingCS, volumetricNearPlaneID, data.nearPlane);
            context.cmd.SetComputeVectorParam(data.volumetricLightingCS, goupsizeID, groupSize);

            if (data.enableReprojection)
            {
                context.cmd.SetComputeVectorParam(data.volumetricLightingCS, VolumetricFogShaderIDs._PrevCamPosRWS, data.prevCameraPos);
                context.cmd.SetComputeMatrixParam(data.volumetricLightingCS, VolumetricFogShaderIDs._PreVPMatrix, data.prevVP);
                context.cmd.SetComputeTextureParam(data.volumetricLightingCS, data.volumetricLightingKernel, VolumetricFogShaderIDs._VBufferHistory, data.historyBuffer);  // Read
                context.cmd.SetComputeTextureParam(data.volumetricLightingCS, data.volumetricLightingKernel, VolumetricFogShaderIDs._VBufferFeedback, data.feedbackBuffer); // Write
            }
            
            ConstantBuffer.Push(data.volumetricCB, data.volumetricLightingCS, VolumetricFogShaderIDs._ShaderVariablesVolumetric);

            context.cmd.DispatchCompute(data.volumetricLightingCS, data.volumetricLightingKernel, ((int)data.resolution.x + 7) / 8, ((int)data.resolution.y + 7) / 8, data.viewCount);

            if (data.filterVolume)
            {
                ConstantBuffer.Push(data.volumetricCB, data.volumetricLightingFilteringCS, VolumetricFogShaderIDs._ShaderVariablesVolumetric);
                context.cmd.SetComputeTextureParam(data.volumetricLightingFilteringCS, data.volumetricFilteringKernel, VolumetricFogShaderIDs._VBufferLighting, data.lightingBuffer);
                if (data.filteringNeedsExtraBuffer)
                {
                    context.cmd.SetComputeTextureParam(data.volumetricLightingFilteringCS, data.volumetricFilteringKernel, VolumetricFogShaderIDs._VBufferLightingFiltered, data.filteringOutputBuffer);
                }

                context.cmd.DispatchCompute(data.volumetricLightingFilteringCS, data.volumetricFilteringKernel, PostProcessingUtils.DivRoundUp((int)data.resolution.x, 8),
                    PostProcessingUtils.DivRoundUp((int)data.resolution.y, 8),
                    data.sliceCount);
            }
        }

    }
}