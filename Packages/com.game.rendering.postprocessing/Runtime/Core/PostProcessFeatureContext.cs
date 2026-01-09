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
        /// 历史帧颜色
        /// </summary>
        ColorBuffer,
        /// <summary>
        /// Exposure buffer.
        /// </summary>
        Exposure,
        /// <summary>
        /// Screen Space Reflection Accumulation.
        /// </summary>
        ScreenSpaceReflectionAccumulation,
    }


    public class PostProcessFeatureContext
    {
        private uint m_FrameCount = 0;
        public uint FrameCount => m_FrameCount;
        
        private Camera m_Camera;
        private UniversalAdditionalCameraData m_AdditionalCameraData;


        private BufferedRTHandleSystem m_HistoryRTSystem = new();

        public void Setup(Camera camera)
        {
            m_Camera = camera;

            if (m_AdditionalCameraData == null)
            {
                m_Camera.TryGetComponent(out m_AdditionalCameraData);
            }
        }
        
        public void UpdateFrame(ref RenderingData renderingData)
        {
            m_FrameCount++;
            if (m_FrameCount >= uint.MaxValue)
            {
                m_FrameCount = 0;
            }
            
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            var viewportSize = new Vector2Int(descriptor.width, descriptor.height);
            m_HistoryRTSystem.SwapAndSetReferenceSize(descriptor.width, descriptor.height);
        }

        public void Dispose()
        {
            m_HistoryRTSystem?.ReleaseAll();
            
            if (m_HistoryRTSystem != null)
            {
                m_HistoryRTSystem.Dispose();
                m_HistoryRTSystem = null;
            }
        }


        #region Histroy
        
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
        
        /// <summary>
        /// Allocates a history RTHandle with the unique identifier id.
        /// </summary>
        /// <param name="id">Unique id for this history buffer.</param>
        /// <param name="allocator">Allocator function for the history RTHandle.</param>
        /// <param name="bufferCount">Number of buffer that should be allocated.</param>
        /// <returns>A new RTHandle.</returns>
        public RTHandle AllocHistoryFrameRT(int id, Func<string, int, RTHandleSystem, RTHandle> allocator, int bufferCount)
        {
            m_HistoryRTSystem.AllocBuffer(id, (rts, i) => allocator(m_Camera.name, i, rts), bufferCount);
            return m_HistoryRTSystem.GetFrameRT(id, 0);
        }
        
        /// <summary>
        /// Returns the id RTHandle from the previous frame.
        /// </summary>
        /// <param name="id">Id of the history RTHandle.</param>
        /// <returns>The RTHandle from previous frame.</returns>
        public RTHandle GetPreviousFrameRT(int id)
        {
            return m_HistoryRTSystem.GetFrameRT(id, 1);
        }

        /// <summary>
        /// Returns the id RTHandle of the current frame.
        /// </summary>
        /// <param name="id">Id of the history RTHandle.</param>
        /// <returns>The RTHandle of the current frame.</returns>
        public RTHandle GetCurrentFrameRT(int id)
        {
            return m_HistoryRTSystem.GetFrameRT(id, 0);
        }
        
        /// <summary>
        /// Release a buffer.
        /// </summary>
        /// <param name="id"></param>
        internal void ReleaseHistoryFrameRT(int id)
        {
            m_HistoryRTSystem.ReleaseBuffer(id);
        }
        
        /// <summary>
        /// Returns the number of frames for a particular id RTHandle.
        /// </summary>
        /// <param name="id">Id of the history RTHandle.</param>
        /// <returns>The frame count.</returns>
        public int GetHistoryFrameCount(int id)
        {
            return m_HistoryRTSystem.GetNumFramesAllocated(id);
        }

        #endregion
    }
}