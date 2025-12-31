using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/卷积泛光 (Convolution Bloom)")]
    public class BloomConvolution : VolumeSetting
    {
        public BloomConvolution()
        {
            displayName = "卷积泛光 (Convolution Bloom)";
        }
        
        internal enum ConvolutionBloomQuality
        {
            Medium,
            High
        }
        
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);
        internal EnumParameter<ConvolutionBloomQuality> quality = new(ConvolutionBloomQuality.Medium);
        [Tooltip("Filters out pixels under this level of brightness. Value is in gamma-space.")]
        public MinFloatParameter threshold = new(0.8f, 0.0f);

        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new(1.0f, 0.0f);

        /// <summary>
        /// Controls the extent of the veiling effect.
        /// </summary>
        [Tooltip("Set the radius of the bloom effect.")]
        public ClampedFloatParameter scatter = new(0.7f, 0f, 1f);

        [Tooltip("Set the maximum intensity that Unity uses to calculate Bloom. If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.")]
        public MinFloatParameter clamp = new(65472f, 0f);

        [Header("Performance")]
        [AdditionalProperty]
        public BoolParameter disableDispatchMergeOptimization = new(false);

        [AdditionalProperty]
        public BoolParameter disableReadWriteOptimization = new(false);

        [Header("FFT")]
        public Vector2Parameter fftExtend = new(new Vector2(0.1f, 0.1f));
        
        [HideInInspector]
        public BoolParameter updateOTF = new(true);
        public BoolParameter generatePSF = new(true);
        public TextureParameter imagePSF = new(null);
        public FloatParameter imagePSFScale = new(1.0f);
        public FloatParameter imagePSFMinClamp = new(0.0f);
        public FloatParameter imagePSFMaxClamp = new(65472f);
        public FloatParameter imagePSFPow = new(1f);
        

        public override bool IsActive() => enable.value;
        
        public bool IsParamUpdated()
        {
            return updateOTF.value;
        }
    }

    [PostProcess("BloomConvolution", PostProcessInjectionPoint.AfterRenderingPostProcessing)]
    public partial class BloomConvolutionRenderer : PostProcessVolumeRenderer<BloomConvolution>
    {
        private FFTKernel _fftKernel;

        private FFTKernel.FFTSize _convolutionSizeX = FFTKernel.FFTSize.Size512;
        private FFTKernel.FFTSize _convolutionSizeY = FFTKernel.FFTSize.Size256;

        private RenderTexture _fftTarget;
        private RenderTexture _psf;
        private RenderTexture _otf;

        private Material _brightMaskMaterial;
        private Material _bloomBlendMaterial;
        private Material _psfRemapMaterial;
        private Material _psfGeneratorMaterial;

        private static class ShaderProperties
        {
            public static readonly int FFTExtend = Shader.PropertyToID("_FFT_EXTEND");
            public static readonly int Threshold = Shader.PropertyToID("_Threshlod");
            public static readonly int ThresholdKnee = Shader.PropertyToID("_ThresholdKnee");
            public static readonly int TexelSize = Shader.PropertyToID("_TexelSize");
            public static readonly int MaxClamp = Shader.PropertyToID("_MaxClamp");
            public static readonly int MinClamp = Shader.PropertyToID("_MinClamp");
            public static readonly int KernelPow = Shader.PropertyToID("_Power");
            public static readonly int KernelScaler = Shader.PropertyToID("_Scaler");
            public static readonly int ScreenX = Shader.PropertyToID("_ScreenX");
            public static readonly int ScreenY = Shader.PropertyToID("_ScreenY");
            public static readonly int EnableRemap = Shader.PropertyToID("_EnableRemap");
            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
        }

        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Color;

        public override void Setup()
        {
            var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<BloomConvolutionResources>();
            _fftKernel = new FFTKernel(runtimeResources.fastFourierTransformCS, runtimeResources.fastFourierConvolveCS);
            profilingSampler = new ProfilingSampler("Convolution Bloom");
        }

        public override void Dispose(bool disposing)
        {
            if (_psf) _psf.Release();
            if (_otf) _otf.Release();
            if (_fftTarget) _fftTarget.Release();
            if (_brightMaskMaterial) CoreUtils.Destroy(_brightMaskMaterial);
            if (_bloomBlendMaterial) CoreUtils.Destroy(_bloomBlendMaterial);
            if (_psfRemapMaterial) CoreUtils.Destroy(_psfRemapMaterial);
            if (_psfGeneratorMaterial) CoreUtils.Destroy(_psfGeneratorMaterial);
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var runtimeResources = GraphicsSettings.GetRenderPipelineSettings<BloomConvolutionResources>();
            
            if (!_brightMaskMaterial) _brightMaskMaterial = CoreUtils.CreateEngineMaterial(runtimeResources.ConvolutionBloomBrightMask);
            if (!_bloomBlendMaterial) _bloomBlendMaterial = CoreUtils.CreateEngineMaterial(runtimeResources.ConvolutionBloomBlend);
            if (!_psfRemapMaterial) _psfRemapMaterial = CoreUtils.CreateEngineMaterial(runtimeResources.ConvolutionBloomPsfRemap);
            if (!_psfGeneratorMaterial) _psfGeneratorMaterial = CoreUtils.CreateEngineMaterial(runtimeResources.ConvolutionBloomPsfGenerator);
        }

        private void UpdateRenderTextureSize()
        {
            FFTKernel.FFTSize sizeX;
            FFTKernel.FFTSize sizeY;
            if (settings.quality.value == BloomConvolution.ConvolutionBloomQuality.High)
            {
                sizeX = FFTKernel.FFTSize.Size1024;
                sizeY = FFTKernel.FFTSize.Size512;
            }
            else
            {
                sizeX = FFTKernel.FFTSize.Size512;
                sizeY = FFTKernel.FFTSize.Size256;
            }
            
            int width = (int)sizeX;
            int height = (int)sizeY;
            
            const RenderTextureFormat format = RenderTextureFormat.ARGBHalf;
            int verticalPadding = Mathf.FloorToInt(height * settings.fftExtend.value.y);
            int targetTexHeight = settings.disableReadWriteOptimization.value ? height : height - 2 * verticalPadding;
            if (!_otf || !_fftTarget || !_psf || _convolutionSizeX != sizeX
                || _convolutionSizeY != sizeY || _fftTarget.height != targetTexHeight
                || _fftTarget.format != format)
            {
                _convolutionSizeX = sizeX;
                _convolutionSizeY = sizeY;

                if (!_otf || _otf.width != width || _otf.height != height || _otf.format != format)
                {
                    if (_otf) _otf.Release();
                    _otf = new RenderTexture(width, height, 0,
                        format, RenderTextureReadWrite.Linear)
                    {
                        depthStencilFormat = GraphicsFormat.None,
                        enableRandomWrite = true
                    };
                    _otf.Create();
                }

                if (!_fftTarget || _fftTarget.width != width || _fftTarget.height != targetTexHeight ||
                    _fftTarget.format != format)
                {
                    if (_fftTarget) _fftTarget.Release();
                    _fftTarget = new RenderTexture(width, targetTexHeight, 0,
                        format, RenderTextureReadWrite.Linear)
                    {
                        depthStencilFormat = GraphicsFormat.None,
                        wrapMode = TextureWrapMode.Clamp,
                        enableRandomWrite = true
                    };
                    _fftTarget.Create();
                }

                if (!_psf || _psf.width != width || _psf.height != height || _psf.format != format)
                {
                    if (_psf) _psf.Release();
                    _psf = new RenderTexture(width, height, 0,
                        format, RenderTextureReadWrite.Linear)
                    {
                        depthStencilFormat = GraphicsFormat.None,
                        enableRandomWrite = true
                    };
                    _psf.Create();
                }
            }
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            float threshold = settings.threshold.value;
            float thresholdKnee = settings.scatter.value;
            float clampMax = settings.clamp.value;
            float intensity = settings.intensity.value;
            var fftExtend = settings.fftExtend.value;
            bool highQuality = settings.quality.value == BloomConvolution.ConvolutionBloomQuality.High;

            UpdateRenderTextureSize();

            var targetX = renderingData.cameraData.camera.pixelWidth;
            var targetY = renderingData.cameraData.camera.pixelHeight;
            if (settings.IsParamUpdated())
            {
                OpticalTransferFunctionUpdate(cmd, settings, new Vector2Int(targetX, targetY), highQuality);
            }

            var colorTargetHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (colorTargetHandle.rt == null) return;

            if (!settings.disableReadWriteOptimization.value) fftExtend.y = 0;
            _brightMaskMaterial.SetVector(ShaderProperties.FFTExtend, fftExtend);
            _brightMaskMaterial.SetFloat(ShaderProperties.Threshold, threshold);
            _brightMaskMaterial.SetFloat(ShaderProperties.ThresholdKnee, thresholdKnee);
            _brightMaskMaterial.SetFloat(ShaderProperties.MaxClamp, clampMax);
            _brightMaskMaterial.SetVector(ShaderProperties.TexelSize, new Vector4(1f / targetX, 1f / targetY, 0, 0));
            cmd.Blit(colorTargetHandle, _fftTarget, _brightMaskMaterial);

            Vector2Int size = new Vector2Int((int)_convolutionSizeX, (int)_convolutionSizeY);
            Vector2Int horizontalRange = Vector2Int.zero;
            Vector2Int verticalRange = Vector2Int.zero;
            Vector2Int offset = Vector2Int.zero;

            if (!settings.disableReadWriteOptimization.value)
            {
                int paddingY = (size.y - _fftTarget.height) / 2;
                verticalRange = new Vector2Int(0, _fftTarget.height);
                offset = new Vector2Int(0, -paddingY);
            }

            if (settings.disableDispatchMergeOptimization.value)
            {
                _fftKernel.Convolve(cmd, _fftTarget, _otf, highQuality);
            }
            else
            {
                _fftKernel.ConvolveOpt(cmd, _fftTarget, _otf,
                    size,
                    horizontalRange,
                    verticalRange,
                    offset);
            }

            _bloomBlendMaterial.SetVector(ShaderProperties.FFTExtend, fftExtend);
            _bloomBlendMaterial.SetFloat(ShaderProperties.Intensity, intensity);
            cmd.Blit(_fftTarget, colorTargetHandle, _bloomBlendMaterial);
        }
        
        private void OpticalTransferFunctionUpdate(CommandBuffer cmd, BloomConvolution param, Vector2Int size, bool highQuality)
        {
            _psfRemapMaterial.SetFloat(ShaderProperties.MaxClamp, param.imagePSFMaxClamp.value);
            _psfRemapMaterial.SetFloat(ShaderProperties.MinClamp, param.imagePSFMinClamp.value);
            _psfRemapMaterial.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
            _psfRemapMaterial.SetFloat(ShaderProperties.KernelPow, param.imagePSFPow.value);
            _psfRemapMaterial.SetFloat(ShaderProperties.KernelScaler, param.imagePSFScale.value);
            _psfRemapMaterial.SetInt(ShaderProperties.ScreenX, size.x);
            _psfRemapMaterial.SetInt(ShaderProperties.ScreenY, size.y);
            if (param.generatePSF.value)
            {
                _psfGeneratorMaterial.SetVector(ShaderProperties.FFTExtend, param.fftExtend.value);
                _psfGeneratorMaterial.SetInt(ShaderProperties.ScreenX, size.x);
                _psfGeneratorMaterial.SetInt(ShaderProperties.ScreenY, size.y);
                _psfGeneratorMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                cmd.Blit(_otf, _otf, _psfGeneratorMaterial);
            }
            else
            {
                _psfRemapMaterial.SetInt(ShaderProperties.EnableRemap, 1);
                cmd.Blit(param.imagePSF.value, _otf, _psfRemapMaterial);
            }

            _fftKernel.FFT(_otf, cmd, highQuality);
        }
        
    }
}
