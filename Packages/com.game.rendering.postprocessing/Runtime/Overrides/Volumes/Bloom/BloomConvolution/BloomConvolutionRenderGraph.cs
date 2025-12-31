using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class BloomConvolutionRenderer : PostProcessVolumeRenderer<BloomConvolution>
    {
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            float threshold = settings.threshold.value;
            float thresholdKnee = settings.scatter.value;
            float clampMax = settings.clamp.value;
            float intensity = settings.intensity.value;
            var fftExtend = settings.fftExtend.value;
            bool highQuality = settings.quality.value == BloomConvolution.ConvolutionBloomQuality.High;
            
            UpdateRenderTextureSize();
            
            var targetX = cameraData.camera.pixelWidth;
            var targetY = cameraData.camera.pixelHeight;
            // if (settings.IsParamUpdated())
            // {
            //     OpticalTransferFunctionUpdate(cmd, settings, new Vector2Int(targetX, targetY), highQuality);
            // }
            
        }
    }
}