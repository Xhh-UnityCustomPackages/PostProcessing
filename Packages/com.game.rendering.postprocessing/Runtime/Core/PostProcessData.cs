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
    }

    public partial class PostProcessData : IDisposable
    {
        private uint m_FrameCount = 0;
        public uint FrameCount => m_FrameCount;
        
        private struct ShaderVariablesGlobal
        {
            public Matrix4x4 ViewMatrix;
            public Matrix4x4 ViewProjMatrix;
            public Matrix4x4 InvViewProjMatrix;
            public Matrix4x4 PrevInvViewProjMatrix;
            
            public Vector4 ColorPyramidUvScaleAndLimitPrevFrame;
        }
        
        private ShaderVariablesGlobal m_ShaderVariablesGlobal;
        
        public GPUCopy GPUCopy { get; private set; }
        public MipGenerator MipGenerator { get; private set; }

        BufferedRTHandleSystem m_HistoryRTSystem = new ();

        private Camera camera;
        
        /// <summary>
        /// Color texture before post-processing of previous frame
        /// </summary>
        public RTHandle CameraPreviousColorTextureRT;
        
        /// <summary>
        /// Depth pyramid of current frame
        /// </summary>
        public RTHandle DepthPyramidRT;
        
        private PackedMipChainInfo m_DepthBufferMipChainInfo = new();
        public PackedMipChainInfo DepthMipChainInfo => m_DepthBufferMipChainInfo;

        public int ColorPyramidHistoryMipCount { get; internal set; }
        public bool ResetPostProcessingHistory { get; internal set; } = false;
        public bool DidResetPostProcessingHistoryInLastFrame { get; internal set; }
        public bool RequireHistoryColor { get; internal set; }
        
        float m_ScreenSpaceAccumulationResolutionScale = 0.0f; // Use another scale if AO & SSR don't have the same resolution

        public PostProcessData()
        {
            GPUCopy = new GPUCopy();
            MipGenerator = new MipGenerator();
            m_DepthBufferMipChainInfo.Allocate();
            InitExposure();
        }

        public void Dispose()
        {
            m_FrameCount = 0;
            MipGenerator.Release();
            m_HistoryRTSystem.Dispose();
            CameraPreviousColorTextureRT?.Release();
            ColorPyramidHistoryMipCount = 1;
            // Exposure
            RTHandles.Release(m_EmptyExposureTexture);
            m_EmptyExposureTexture = null;
            RTHandles.Release(m_DebugExposureData);
            m_DebugExposureData = null;
            
            // Release default texture RTHandle wrappers
            RTHandles.Release(m_WhiteTextureRTHandle);
            RTHandles.Release(m_BlackTextureRTHandle);
            RTHandles.Release(m_GrayTextureRTHandle);
            m_WhiteTextureRTHandle = null;
            m_BlackTextureRTHandle = null;
            m_GrayTextureRTHandle = null;
        }

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
        }

        private void UpdateRenderTextures(ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            var viewportSize = new Vector2Int(descriptor.width, descriptor.height);
            m_HistoryRTSystem.SwapAndSetReferenceSize(descriptor.width, descriptor.height);
            
            // Since we do not use RTHandleScale, ensure render texture size correct
            if (m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.x > descriptor.width
                || m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.y > descriptor.height)
            {
                m_HistoryRTSystem.ResetReferenceSize(descriptor.width, descriptor.height);
                m_ExposureTextures.Clear();
            }
            
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
        }
        
        private void UpdateRenderTextures(UniversalCameraData cameraData)
        {
            var descriptor = cameraData.cameraTargetDescriptor;
            var viewportSize = new Vector2Int(descriptor.width, descriptor.height);
            m_HistoryRTSystem.SwapAndSetReferenceSize(descriptor.width, descriptor.height);

            // Since we do not use RTHandleScale, ensure render texture size correct
            if (m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.x > descriptor.width
                || m_HistoryRTSystem.rtHandleProperties.currentRenderTargetSize.y > descriptor.height)
            {
                m_HistoryRTSystem.ResetReferenceSize(descriptor.width, descriptor.height);
                m_ExposureTextures.Clear();
            }

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


        #region GlobalVariables
        
        
        /// <summary>
        /// Push global constant buffers to gpu
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="renderingData"></param>
        internal void PushGlobalBuffers(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // PushShadowData(cmd);
            PushGlobalVariables(cmd, ref renderingData);
        }
        
        private void PushGlobalVariables(CommandBuffer cmd, ref RenderingData renderingData)
        {
            PrepareGlobalVariables(ref renderingData);
            ConstantBuffer.PushGlobal(cmd, m_ShaderVariablesGlobal, PipelineShaderIDs.ShaderVariablesGlobal);
            cmd.SetGlobalVector(PipelineShaderIDs._ColorPyramidUvScaleAndLimitPrevFrame, m_ShaderVariablesGlobal.ColorPyramidUvScaleAndLimitPrevFrame);
        }

        private void PrepareGlobalVariables(ref RenderingData renderingData, RTHandle rtHandle = null)
        {
            // Match HDRP View Projection Matrix, pre-handle reverse z.
            m_ShaderVariablesGlobal.ViewMatrix = renderingData.cameraData.camera.worldToCameraMatrix;
            
            m_ShaderVariablesGlobal.ViewProjMatrix = PostProcessingUtils.CalculateViewProjMatrix(ref renderingData.cameraData);
            
            var lastInvViewProjMatrix = m_ShaderVariablesGlobal.InvViewProjMatrix;
            m_ShaderVariablesGlobal.InvViewProjMatrix = m_ShaderVariablesGlobal.ViewProjMatrix.inverse;
            m_ShaderVariablesGlobal.PrevInvViewProjMatrix = FrameCount > 1 ? m_ShaderVariablesGlobal.InvViewProjMatrix : lastInvViewProjMatrix;
            m_ShaderVariablesGlobal.ColorPyramidUvScaleAndLimitPrevFrame
                = PostProcessingUtils.ComputeViewportScaleAndLimit(m_HistoryRTSystem.rtHandleProperties.previousViewportSize,
                    m_HistoryRTSystem.rtHandleProperties.previousRenderTargetSize);
        }


        #endregion
        
        #region History

        internal struct CustomHistoryAllocator
        {
            Vector2 scaleFactor;
            GraphicsFormat format;
            string name;

            public CustomHistoryAllocator(Vector2 scaleFactor, GraphicsFormat format, string name)
            {
                this.scaleFactor = scaleFactor;
                this.format = format;
                this.name = name;
            }

            public RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                return rtHandleSystem.Alloc(Vector2.one * scaleFactor,
                    // TextureXR.slices, 
                    filterMode: FilterMode.Point,
                    colorFormat: format,
                    // dimension: TextureXR.dimension, 
                    // useDynamicScale: true, 
                    enableRandomWrite: true,
                    name: $"{id}_{name}_{frameIndex}");
            }
        }


        public void AllocateScreenSpaceAccumulationHistoryBuffer(float scaleFactor)
        {
            if (scaleFactor != m_ScreenSpaceAccumulationResolutionScale || GetCurrentFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation) == null)
            {
                ReleaseHistoryFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation);

                var ssrAlloc = new CustomHistoryAllocator(new Vector2(scaleFactor, scaleFactor), GraphicsFormat.R16G16B16A16_SFloat, "SSR_Accum Packed history");
                AllocHistoryFrameRT((int)FrameHistoryType.ScreenSpaceReflectionAccumulation, ssrAlloc.Allocator, 2);

                m_ScreenSpaceAccumulationResolutionScale = scaleFactor;
            }
        }

        public RTHandle AllocHistoryFrameRT(int id, Func<string, int, RTHandleSystem, RTHandle> allocator, int bufferCount)
        {
            m_HistoryRTSystem.AllocBuffer(id, (rts, i) => allocator(camera.ToString(), i, rts), bufferCount);
            return m_HistoryRTSystem.GetFrameRT(id, 0);
        }
        public RTHandle GetPreviousFrameRT(int id)
        {
            return m_HistoryRTSystem.GetFrameRT(id, 1);
        }
        
        public RTHandle GetCurrentFrameRT(int id)
        {
            return m_HistoryRTSystem.GetFrameRT(id, 0);
        }
        internal void ReleaseHistoryFrameRT(int id)
        {
            m_HistoryRTSystem.ReleaseBuffer(id);
        }

        public int GetHistoryFrameCount(int id)
        {
            return m_HistoryRTSystem.GetNumFramesAllocated(id);
        }

        public RTHandle AllocHistoryFrameRT(CameraType cameraType, int id, Func<string, int, RTHandleSystem, RTHandle> allocator, int bufferCount)
        {
            m_HistoryRTSystem.AllocBuffer(id, (rts, i) => allocator(cameraType.ToString(), i, rts), bufferCount);
            return m_HistoryRTSystem.GetFrameRT(id, 0);
        }
        
        #endregion
    }
}