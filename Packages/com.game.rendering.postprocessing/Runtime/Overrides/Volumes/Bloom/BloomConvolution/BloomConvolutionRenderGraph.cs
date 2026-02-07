using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class BloomConvolutionRenderer : PostProcessVolumeRenderer<BloomConvolution>
    {
        private class BrightMaskPassData
        {
            internal Material BrightMaskMaterial;
            internal Vector2 FFTExtend;
            internal float Threshold;
            internal float ThresholdKnee;
            internal float MaxClamp;
            internal Vector4 TexelSize;
            internal TextureHandle Source;
        }
        
        private class ConvolutionPassData
        {
            internal FFTKernel FFTKernel;
            internal TextureHandle Target;
            internal TextureHandle Filter;
            internal bool HighQuality;
            internal bool DisableDispatchMergeOptimization;
            internal bool DisableReadWriteOptimization;
            internal Vector2Int Size;
        }

        private class BloomBlendPassData
        {
            internal Material BloomBlendMaterial;
            internal Vector2 FFTExtend;
            internal float Intensity;
            internal TextureHandle Source;
        }
        
        private class OTFUpdatePassData
        {
            internal Material PsfRemapMaterial;
            internal Material PsfGeneratorMaterial;
            internal bool HighQuality;
            internal FFTKernel FFTKernel;
            internal TextureHandle OtfTextureHandle;
            internal TextureHandle ImagePsfTexture;
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            // UpdateRenderTextureSize(bloomParams);
        }
    }
}