using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
     //目前没有太好的位置
    public enum FrameHistoryType
    {
        /// <summary>
        /// Color buffer mip chain.
        /// </summary>
        ColorBufferMipChain,
        /// <summary>
        /// Exposure buffer.
        /// </summary>
        Exposure,
        /// <summary>
        /// Screen Space Reflection Accumulation.
        /// </summary>
        ScreenSpaceReflectionAccumulation,
        /// <summary>
        /// Depth buffer for temporal effects.
        /// </summary>
        Depth,
        /// <summary>
        /// Normal buffer for temporal effects.
        /// </summary>
        Normal,
        /// <summary>
        /// Screen Space Global Illumination history buffer for temporal denoising.
        /// </summary>
        ScreenSpaceGlobalIllumination,
        /// <summary>
        /// Screen Space Global Illumination second history buffer for second denoiser pass.
        /// </summary>
        ScreenSpaceGlobalIllumination2
    }

    public partial class PostProcessData : IDisposable
    {
        public bool PreferComputeShader = false;
        public bool SampleProbeVolumes = false;
        
        private uint m_FrameCount = 0;
        private int m_TaaFrameIndex;
        public uint FrameCount => m_FrameCount;
        
        public struct ViewConstants
        {
            public Matrix4x4 viewMatrix;
            public Matrix4x4 viewProjMatrix;
            public Matrix4x4 invViewProjMatrix;
            public Matrix4x4 prevInvViewProjMatrix;
            public Matrix4x4 projMatrix;
        }

        private ShaderVariablesGlobal m_ShaderVariablesGlobal;
        public ViewConstants mainViewConstants;
        
        public GPUCopy GPUCopy { get; private set; }
        public MipGenerator MipGenerator { get; private set; }

        BufferedRTHandleSystem m_HistoryRTSystem = new ();

        public Camera camera { get; private set; }
        
        /// <summary>
        /// Color texture before post-processing of previous frame
        /// </summary>
        public RTHandle CameraPreviousColorTextureRT;
        
        /// <summary>
        /// Depth pyramid of current frame
        /// </summary>
        public RTHandle DepthPyramidRT;
        
        ComputeBuffer m_DepthPyramidMipLevelOffsetsBuffer = null;
        public ComputeBuffer DepthPyramidMipLevelOffsetsBuffer
        {
            get
            {
                if (m_DepthPyramidMipLevelOffsetsBuffer == null)
                {
                    m_DepthPyramidMipLevelOffsetsBuffer = new ComputeBuffer(15, sizeof(int) * 2);
                }

                return m_DepthPyramidMipLevelOffsetsBuffer;
            }
        }

        private PackedMipChainInfo m_DepthBufferMipChainInfo = new();
        public PackedMipChainInfo DepthMipChainInfo => m_DepthBufferMipChainInfo;

        public int ColorPyramidHistoryMipCount { get; internal set; }
        public bool ResetPostProcessingHistory { get; internal set; } = false;
        public bool DidResetPostProcessingHistoryInLastFrame { get; internal set; }
        public bool RequireHistoryColor { get; internal set; }
        
        float m_ScreenSpaceAccumulationResolutionScale = 0.0f; // Use another scale if AO & SSR don't have the same resolution

        /// <summary>Width actually used for rendering after dynamic resolution and XR is applied.</summary>
        public int actualWidth { get; private set; }
        /// <summary>Height actually used for rendering after dynamic resolution and XR is applied.</summary>
        public int actualHeight { get; private set; }
        
        internal Rect finalViewport = new Rect(Vector2.zero, -1.0f * Vector2.one); // This will have the correct viewport position and the size will be full resolution (ie : not taking dynamic rez into account)
        internal Rect prevFinalViewport;
        
        private BlueNoise m_BlueNoise;
        public PostProcessData()
        {
            var runtimeTexture = GraphicsSettings.GetRenderPipelineSettings<PostProcessFeatureRuntimeTextures>();
            m_BlueNoise = new BlueNoise(runtimeTexture);
            GPUCopy = new GPUCopy();
            MipGenerator = new MipGenerator();
            m_DepthBufferMipChainInfo.Allocate();
            InitExposure();
            Reset();
        }

        public void Reset()
        {
            volumetricHistoryIsValid = false;
        }

        public void Dispose()
        {
            m_FrameCount = 0;
            MipGenerator.Release();
            m_HistoryRTSystem.Dispose();
            CameraPreviousColorTextureRT?.Release();
            ColorPyramidHistoryMipCount = 1;
            
            m_BlueNoise.Cleanup();
            
            DepthPyramidRT?.Release();
            CoreUtils.SafeRelease(m_DepthPyramidMipLevelOffsetsBuffer);
            
            // Exposure
            RTHandles.Release(m_EmptyExposureTexture);
            m_EmptyExposureTexture = null;
            RTHandles.Release(m_DebugExposureData);
            m_DebugExposureData = null;
            CoreUtils.SafeRelease(HistogramBuffer);
            
            // Release default texture RTHandle wrappers
            RTHandles.Release(m_WhiteTextureRTHandle);
            RTHandles.Release(m_BlackTextureRTHandle);
            RTHandles.Release(m_GrayTextureRTHandle);
            m_WhiteTextureRTHandle = null;
            m_BlackTextureRTHandle = null;
            m_GrayTextureRTHandle = null;
        }

        #region Update
        
        public void Update(ref RenderingData renderingData)
        {
            m_FrameCount++;
            if (m_FrameCount >= uint.MaxValue)
            {
                m_FrameCount = 0;
            }
            
            UpdateCameraData(renderingData.cameraData);
            UpdateRenderTextures(ref renderingData);
            UpdateVolumeParameters();
            
            // Update viewport
            {
                prevFinalViewport = finalViewport;

                finalViewport = GetPixelRect();

                actualWidth = Math.Max((int)finalViewport.size.x, 1);
                actualHeight = Math.Max((int)finalViewport.size.y, 1);
            }
        }

        private void UpdateRenderTextures(ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            m_HistoryRTSystem.SwapAndSetReferenceSize(descriptor.width, descriptor.height);
            
            // Since we do not use RTHandleScale, ensure render texture size correct
            if (m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.x > descriptor.width
                || m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.y > descriptor.height)
            {
                m_HistoryRTSystem.ResetReferenceSize(descriptor.width, descriptor.height);
                m_ExposureTextures.Clear();
            }
            
            // var viewportSize = new Vector2Int(renderingData.cameraData.camera.pixelWidth, renderingData.cameraData.camera.pixelHeight);
            var viewportSize = new Vector2Int(descriptor.width, descriptor.height);
            m_DepthBufferMipChainInfo.ComputePackedMipChainInfo(viewportSize, 0);
            
            SetupExposureTextures();
        }

        private void UpdateCameraData(CameraData cameraData)
        {
            camera = cameraData.camera;
        }


        public void Update(UniversalCameraData cameraData)
        {
            m_FrameCount++;
            if (m_FrameCount >= uint.MaxValue)
            {
                m_FrameCount = 0;
            }
            UpdateCameraData(cameraData);
            UpdateRenderTextures(cameraData);
            UpdateVolumeParameters();
            
            // Update viewport
            {
                prevFinalViewport = finalViewport;

                finalViewport = GetPixelRect();

                actualWidth = Math.Max((int)finalViewport.size.x, 1);
                actualHeight = Math.Max((int)finalViewport.size.y, 1);
            }
        }
        
        private void UpdateRenderTextures(UniversalCameraData cameraData)
        {
            var descriptor = cameraData.cameraTargetDescriptor;
            
            m_HistoryRTSystem.SwapAndSetReferenceSize(descriptor.width, descriptor.height);

            // Since we do not use RTHandleScale, ensure render texture size correct
            if (m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.x > descriptor.width
                || m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.y > descriptor.height)
            {
                m_HistoryRTSystem.ResetReferenceSize(descriptor.width, descriptor.height);
                m_ExposureTextures.Clear();
            }

            // var viewportSize = new Vector2Int(cameraData.camera.pixelWidth, cameraData.camera.pixelHeight);
            var viewportSize = new Vector2Int(descriptor.width, descriptor.height);
            m_DepthBufferMipChainInfo.ComputePackedMipChainInfo(viewportSize, 0);
           
            SetupExposureTextures();
        }
        
        private void UpdateCameraData(UniversalCameraData cameraData)
        {
            camera = cameraData.camera;
        }

        private void UpdateVolumeParameters()
        {
            m_Exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
        }
        #endregion

        #region GlobalVariables
        
        
        /// <summary>
        /// Push global constant buffers to gpu
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="renderingData"></param>
        internal void PushGlobalBuffers(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // PushShadowData(cmd);
            UpdateViewConstants(ref mainViewConstants, ref renderingData);
            UpdateShaderVariablesGlobalCB(ref m_ShaderVariablesGlobal, renderingData.cameraData.IsTemporalAAEnabled());
            ConstantBuffer.PushGlobal(cmd, m_ShaderVariablesGlobal, PipelineShaderIDs.ShaderVariablesGlobal);
            cmd.SetGlobalVector(PipelineShaderIDs._TaaFrameInfo, m_ShaderVariablesGlobal._TaaFrameInfo);
            cmd.SetGlobalVector(PipelineShaderIDs._ColorPyramidUvScaleAndLimitPrevFrame, m_ShaderVariablesGlobal._ColorPyramidUvScaleAndLimitPrevFrame);
        }

        private void UpdateViewConstants(ref ViewConstants viewConstants, ref RenderingData renderingData)
        {
            // Match HDRP View Projection Matrix, pre-handle reverse z.
            viewConstants.viewMatrix = renderingData.cameraData.camera.worldToCameraMatrix;
            viewConstants.projMatrix = renderingData.cameraData.camera.projectionMatrix;
            viewConstants.viewProjMatrix = PostProcessingUtils.CalculateViewProjMatrix(ref renderingData.cameraData);
        }

        void UpdateShaderVariablesGlobalCB(ref ShaderVariablesGlobal cb, bool useTAA)
        {
            // cb._ViewMatrix = mainViewConstants.viewMatrix;
            // cb._CameraViewMatrix = mainViewConstants.viewMatrix;
            // cb._InvViewMatrix = mainViewConstants.invViewMatrix;
            // cb._ProjMatrix = mainViewConstants.projMatrix;
            // cb._InvProjMatrix = mainViewConstants.invProjMatrix;
            // cb._ViewProjMatrix = mainViewConstants.viewProjMatrix;
            // cb._CameraViewProjMatrix = mainViewConstants.viewProjMatrix;
            // cb._InvViewProjMatrix = mainViewConstants.invViewProjMatrix;
            // cb._NonJitteredViewProjMatrix = mainViewConstants.nonJitteredViewProjMatrix;
            // cb._NonJitteredInvViewProjMatrix = mainViewConstants.nonJitteredInvViewProjMatrix;
            // cb._PrevViewProjMatrix = mainViewConstants.prevViewProjMatrix;
            // cb._PrevInvViewProjMatrix = mainViewConstants.prevInvViewProjMatrix;
            
            
            // var lastInvViewProjMatrix = cb._InvViewProjMatrix;
            // cb._InvViewProjMatrix = cb._ViewProjMatrix.inverse;
            // cb._PrevInvViewProjMatrix = FrameCount > 1 ? cb._InvViewProjMatrix : lastInvViewProjMatrix;

            const int kMaxSampleCount = 8;
            if (++m_TaaFrameIndex >= kMaxSampleCount)
                m_TaaFrameIndex = 0;
            cb._TaaFrameInfo = new Vector4(0, m_TaaFrameIndex, FrameCount, useTAA ? 1 : 0);

            cb._ColorPyramidUvScaleAndLimitPrevFrame
                = PostProcessingUtils.ComputeViewportScaleAndLimit(m_HistoryRTSystem.rtHandleProperties.previousViewportSize,
                    m_HistoryRTSystem.rtHandleProperties.previousRenderTargetSize);
        }

        #region RenderGraph

        internal void PushGlobalBuffers(CommandBuffer cmd, UniversalCameraData cameraData, UniversalLightData lightData)
        {
            UpdateViewConstants(ref mainViewConstants, cameraData, false);
            UpdateShaderVariablesGlobalCB(ref m_ShaderVariablesGlobal, cameraData.IsTemporalAAEnabled());
            ConstantBuffer.PushGlobal(cmd, m_ShaderVariablesGlobal, PipelineShaderIDs.ShaderVariablesGlobal);
            cmd.SetGlobalVector(PipelineShaderIDs._TaaFrameInfo, m_ShaderVariablesGlobal._TaaFrameInfo);
            cmd.SetGlobalVector(PipelineShaderIDs._ColorPyramidUvScaleAndLimitPrevFrame, m_ShaderVariablesGlobal._ColorPyramidUvScaleAndLimitPrevFrame);
        }

        private void UpdateViewConstants(ref ViewConstants viewConstants,UniversalCameraData cameraData, bool yFlip)
        {
            // Match HDRP View Projection Matrix, pre-handle reverse z.
            viewConstants.viewMatrix = cameraData.camera.worldToCameraMatrix;
            viewConstants.projMatrix = cameraData.camera.projectionMatrix;
            viewConstants.viewProjMatrix = PostProcessingUtils.CalculateViewProjMatrix(cameraData, yFlip);
        }

        #endregion

        #endregion
        
       
        
        public Vector4 EvaluateRayTracingHistorySizeAndScale(RTHandle buffer)
        {
            return new Vector4(m_HistoryRTSystem.rtHandleProperties.previousViewportSize.x,
                m_HistoryRTSystem.rtHandleProperties.previousViewportSize.y,
                (float)m_HistoryRTSystem.rtHandleProperties.previousViewportSize.x / buffer.rt.width,
                (float)m_HistoryRTSystem.rtHandleProperties.previousViewportSize.y / buffer.rt.height);
        }
        
        Rect? m_OverridePixelRect = null;

        Rect GetPixelRect()
        {
            if (m_OverridePixelRect != null)
                return m_OverridePixelRect.Value;
            else
                return new Rect(camera.pixelRect.x, camera.pixelRect.y, camera.pixelWidth, camera.pixelHeight);
        }
    }
}