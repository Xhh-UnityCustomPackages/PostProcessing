using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public partial class PostProcessData
    {
        public RTHandleProperties historyRTHandleProperties { get { return m_HistoryRTSystem.rtHandleProperties; } }
        
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

    }
}