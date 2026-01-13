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
    }


    public class PostProcessFeatureContext
    {
        private struct ShaderVariablesGlobal
        {
            public Matrix4x4 ViewMatrix;
            public Matrix4x4 ViewProjMatrix;
            public Matrix4x4 InvViewProjMatrix;
            public Matrix4x4 PrevInvViewProjMatrix;
            
            public Vector4 ColorPyramidUvScaleAndLimitPrevFrame;
        }
        
        private uint m_FrameCount = 0;
        public uint FrameCount => m_FrameCount;
        
        private Camera m_Camera;
        private UniversalAdditionalCameraData m_AdditionalCameraData;
        private GPUCopy m_GPUCopy;
        public GPUCopy GPUCopy => m_GPUCopy;
        private MipGenerator m_MipGenerator;
        public  MipGenerator MipGenerator => m_MipGenerator;
        
        private PackedMipChainInfo m_MipChainInfo;
        public PackedMipChainInfo DepthMipChainInfo => m_MipChainInfo;
        public int ColorPyramidHistoryMipCount { get; internal set; }
        
        private BufferedRTHandleSystem m_HistoryRTSystem = new();
        private ShaderVariablesGlobal m_ShaderVariablesGlobal;
        
        public bool RequireHistoryColor { get; internal set; }
        public RTHandle CameraPreviousColorTextureRT;
        
        private bool m_Init = false;

        public void Setup(Camera camera)
        {
            if (m_Init)
            {
                return;
            }

            m_Init = true;

            m_Camera = camera;

            if (m_AdditionalCameraData == null)
            {
                m_Camera.TryGetComponent(out m_AdditionalCameraData);
            }
            
            m_GPUCopy ??= new GPUCopy();
            m_MipGenerator ??= new MipGenerator();
            m_MipChainInfo = new();
            m_MipChainInfo.Allocate();
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
            m_MipChainInfo.ComputePackedMipChainInfo(viewportSize, 0);
        }

        public void Dispose()
        {
            m_FrameCount = 0;
            m_HistoryRTSystem?.ReleaseAll();
            m_MipGenerator?.Release();
            CameraPreviousColorTextureRT?.Release();
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
        /// Get previous frame color buffer if possible
        /// </summary>
        /// <param name="cameraData"></param>
        /// <param name="isNewFrame"></param>
        /// <returns></returns>
        public RTHandle GetPreviousFrameColorRT(CameraData cameraData, out bool isNewFrame)
        {
            // Using history color
            isNewFrame = true;
            if (RequireHistoryColor)
            {
                return CameraPreviousColorTextureRT;
            }
            
            // Using color pyramid
            // if (cameraData.cameraType == CameraType.Game)
            {
                var previewsColorRT = GetCurrentFrameRT((int)FrameHistoryType.ColorBufferMipChain);
                if (previewsColorRT != null)
                {
                    isNewFrame = true;
                    return previewsColorRT;
                }
            }
            
           
           
            
            // Fallback to opaque texture if exist.
            return cameraData.renderer.cameraColorTargetHandle;
            return UniversalRenderingUtility.GetOpaqueTexture(cameraData.renderer);
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