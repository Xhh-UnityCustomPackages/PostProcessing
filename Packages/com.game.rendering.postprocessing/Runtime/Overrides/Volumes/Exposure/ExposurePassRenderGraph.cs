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
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();


            using (var builder = renderGraph.AddUnsafePass<DynamicExposureData>(profilingSampler.name, out var passData))
            {
                PrepareExposurePassData(passData, cameraData.camera);
                postProcessCamera.GrabExposureRequiredTextures(out var prevExposure, out var nextExposure);
                passData.exposureMode = settings.mode.value;

                var preExposure = renderGraph.ImportTexture(prevExposure);
                // var nextExposureHandle = renderGraph.ImportTexture(nextExposure);
     

                // builder.UseTexture(preExposure);
                // passData.prevExposure = preExposure;
                // builder.UseTexture(nextExposureHandle, AccessFlags.Write);
                // passData.nextExposure = nextExposureHandle;
                //
                //
                // builder.AllowGlobalStateModification(true);
                // // builder.SetGlobalTextureAfterPass(exposureHandleRG, "_AutoExposureLUT");
                // builder.SetRenderFunc(static (DynamicExposureData data, UnsafeGraphContext context) =>
                // {
                //     var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                //     
                //     if (data.exposureMode == Exposure.ExposureMode.Fixed)
                //     {
                //         using (new ProfilingScope(cmd, m_FixedExposureSampler))
                //         {
                //             DoFixedExposureRenderGraph(cmd, data);
                //         }
                //     }
                //     else
                //     {
                //         using (new ProfilingScope(cmd, m_DynamicExposureSampler))
                //         {
                //             DoHistogramBasedExposure(cmd, data);
                //         }
                //     }
                //     
                //     cmd.SetGlobalTexture("_AutoExposureLUT", data.nextExposure);
                // });
            }
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

            exposureData.exposureParams = exposureParams;
            exposureData.exposureParams2 = exposureParams2;
            
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams, exposureData.exposureParams);
            cmd.SetComputeVectorParam(cs, ExposureShaderIDs._ExposureParams2, exposureData.exposureParams2);

            cmd.SetComputeTextureParam(cs, kernel, ExposureShaderIDs._OutputTexture, exposureData.nextExposure);
            cmd.DispatchCompute(cs, kernel, 1, 1, 1);
        }
    }
}