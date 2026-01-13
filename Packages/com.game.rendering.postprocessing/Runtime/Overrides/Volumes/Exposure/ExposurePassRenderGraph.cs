using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class ExposureRenderer : PostProcessVolumeRenderer<Exposure>
    {
        private static readonly ProfilingSampler m_FixedExposureSampler = new ("Fixed Exposure");
        private static readonly ProfilingSampler m_DynamicExposureSampler = new("Dynamic Exposure");
        
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            var desc = cameraData.cameraTargetDescriptor;
            desc.width = 1;
            desc.height = 1;
            desc.colorFormat = RenderTextureFormat.RFloat;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;
            m_RenderDescriptor = desc;
            m_ExposureTexturesInfo = GetOrCreateExposureInfoFromCurCamera(cameraData.cameraType);
            
            using (var builder = renderGraph.AddUnsafePass<DynamicExposureData>(profilingSampler.name, out var passData))
            {
                PrepareExposurePassData(passData, cameraData.camera);
                passData.exposureMode = settings.mode.value;

                passData.nextExposure = m_ExposureTexturesInfo.current;
                
                RTHandle exposureRT = m_ExposureTexturesInfo.current;
                RTHandle prevExposureRT = m_ExposureTexturesInfo.previous;
                
                TextureHandle exposureHandleRG = renderGraph.ImportTexture(exposureRT);
                TextureHandle prevExposureHandleRG = renderGraph.ImportTexture(prevExposureRT);
                
                builder.UseTexture(exposureHandleRG, AccessFlags.Write);
                builder.UseTexture(prevExposureHandleRG);
                
                // builder.SetGlobalTextureAfterPass(exposureHandleRG, "_AutoExposureLUT");
                builder.SetRenderFunc(static (DynamicExposureData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    
                    if (data.exposureMode == Exposure.ExposureMode.Fixed)
                    {
                        using (new ProfilingScope(cmd, m_FixedExposureSampler))
                        {
                            DoFixedExposureRenderGraph(cmd, data);
                        }
                    }
                    else
                    {
                        using (new ProfilingScope(cmd, m_DynamicExposureSampler))
                        {
                            DoHistogramBasedExposure(cmd, data);
                        }
                    }
                    
                    cmd.SetGlobalTexture("_AutoExposureLUT", data.nextExposure);
                });
            }
            
            UpdateCurFrameExposureRT(m_ExposureTexturesInfo);
        }

        static void DoFixedExposureRenderGraph(CommandBuffer cmd, DynamicExposureData exposureData)
        {
            ComputeShader cs = exposureData.exposureCS;
            int kernel = 0;
            float m_DebugExposureCompensation = 0;
            Vector4 exposureParams;
            Vector4 exposureParams2 = new Vector4(0.0f, 0.0f, ColorUtils.lensImperfectionExposureScale, ColorUtils.s_LightMeterCalibrationConstant);
            
            VolumeStack stack = VolumeManager.instance.stack;
            Exposure settings = stack.GetComponent<Exposure>();
            
            // if (settings.mode.value == Exposure.ExposureMode.Fixed)
            {
                kernel = cs.FindKernel("KFixedExposure");
                exposureParams = new Vector4(settings.compensation.value + m_DebugExposureCompensation, settings.fixedExposure.value, 0f, 0f);
            }
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, exposureParams2);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, exposureData.nextExposure);
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }
    }
}