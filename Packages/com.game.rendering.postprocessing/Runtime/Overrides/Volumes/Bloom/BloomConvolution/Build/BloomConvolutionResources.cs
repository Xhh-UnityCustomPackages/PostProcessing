using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: BloomConvolutionResources", Order = 2001), HideInInspector]
    public class BloomConvolutionResources : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Bloom/BloomConvolution/Shaders/FFTRadixN.compute")]
        private ComputeShader m_FastFourierTransformCS;

        public ComputeShader fastFourierTransformCS
        {
            get => m_FastFourierTransformCS;
            set => this.SetValueAndNotify(ref m_FastFourierTransformCS, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Bloom/BloomConvolution/Shaders/Convolve.compute")]
        private ComputeShader m_FastFourierConvolveCS;

        public ComputeShader fastFourierConvolveCS
        {
            get => m_FastFourierConvolveCS;
            set => this.SetValueAndNotify(ref m_FastFourierConvolveCS, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Bloom/BloomConvolution/Shaders/BrightMask.shader")]
        private Shader m_ConvolutionBloomBrightMask;

        public Shader ConvolutionBloomBrightMask
        {
            get => m_ConvolutionBloomBrightMask;
            set => this.SetValueAndNotify(ref m_ConvolutionBloomBrightMask, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Bloom/BloomConvolution/Shaders/BloomBlend.shader")]
        private Shader m_ConvolutionBloomBlend;

        public Shader ConvolutionBloomBlend
        {
            get => m_ConvolutionBloomBlend;
            set => this.SetValueAndNotify(ref m_ConvolutionBloomBlend, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Bloom/BloomConvolution/Shaders/PsfRemap.shader")]
        private Shader m_ConvolutionBloomPsfRemap;

        public Shader ConvolutionBloomPsfRemap
        {
            get => m_ConvolutionBloomPsfRemap;
            set => this.SetValueAndNotify(ref m_ConvolutionBloomPsfRemap, value);
        }

        [SerializeField, ResourcePath("Runtime/Overrides/Volumes/Bloom/BloomConvolution/Shaders/PsfGenerator.shader")]
        private Shader m_ConvolutionBloomPsfGenerator;

        public Shader ConvolutionBloomPsfGenerator
        {
            get => m_ConvolutionBloomPsfGenerator;
            set => this.SetValueAndNotify(ref m_ConvolutionBloomPsfGenerator, value);
        }
    }
}