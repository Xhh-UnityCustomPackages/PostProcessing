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

    public partial class PostProcessCamera
    {
        BufferedRTHandleSystem m_HistoryRTSystem = new BufferedRTHandleSystem();
        public BufferedRTHandleSystem historyRTSystem => m_HistoryRTSystem;

        public Camera camera;
        
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
        
        float m_ScreenSpaceAccumulationResolutionScale = 0.0f; // Use another scale if AO & SSR don't have the same resolution

        public PostProcessCamera(Camera camera)
        {
            this.camera = camera;
            m_DepthBufferMipChainInfo.Allocate();
        }

        public void Dispose()
        {
            if (m_HistoryRTSystem != null)
            {
                m_HistoryRTSystem.Dispose();
                // m_HistoryRTSystem = null;
            }
            CameraPreviousColorTextureRT?.Release();
            ColorPyramidHistoryMipCount = 1;
            // Exposure
            RTHandles.Release(m_EmptyExposureTexture);
            RTHandles.Release(m_DebugExposureData);
        }

        public void UpdateRenderTextures(ref RenderingData renderingData)
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
            
            m_Exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
            SetupExposureTextures();
        }
        
        public void UpdateRenderTextures(UniversalCameraData cameraData)
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
            m_Exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
            SetupExposureTextures();
        }

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