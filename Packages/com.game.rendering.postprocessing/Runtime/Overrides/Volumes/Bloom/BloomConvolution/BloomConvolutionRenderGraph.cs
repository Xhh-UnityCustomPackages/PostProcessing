using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Game.Core.PostProcessing
{
    public partial class BloomConvolutionRenderer : PostProcessVolumeRenderer<BloomConvolution>
    {
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
        }
    }
}