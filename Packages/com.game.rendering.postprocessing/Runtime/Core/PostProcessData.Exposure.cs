using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class PostProcessData
    {
        private static RTHandle m_EmptyExposureTexture; // RGHalf
        private RTHandle m_DebugExposureData;
        private Exposure m_Exposure;

        public ComputeBuffer HistogramBuffer;

        private RTHandle m_WhiteTextureRTHandle;
        private RTHandle m_BlackTextureRTHandle;
        private RTHandle m_GrayTextureRTHandle;
        
        public RTHandle GetWhiteTextureRT()
        {
            if (m_WhiteTextureRTHandle == null)
            {
                m_WhiteTextureRTHandle = RTHandles.Alloc(Texture2D.whiteTexture);
            }
            return m_WhiteTextureRTHandle;
        }
        
        public RTHandle GetBlackTextureRT()
        {
            if (m_BlackTextureRTHandle == null)
            {
                m_BlackTextureRTHandle = RTHandles.Alloc(Texture2D.blackTexture);
            }
            return m_BlackTextureRTHandle;
        }
        
        public RTHandle GetGrayTextureRT()
        {
            if (m_GrayTextureRTHandle == null)
            {
                m_GrayTextureRTHandle = RTHandles.Alloc(Texture2D.grayTexture);
            }
            return m_GrayTextureRTHandle;
        }
        
        void InitExposure()
        {
            // Setup a default exposure textures and clear it to neutral values so that the exposure
            // multiplier is 1 and thus has no effect
            // Beware that 0 in EV100 maps to a multiplier of 0.833 so the EV100 value in this
            // neutral exposure texture isn't 0
            m_EmptyExposureTexture = RTHandles.Alloc(1, 1, colorFormat: ExposureFormat,
                enableRandomWrite: true, name: "Empty EV100 Exposure");

            m_DebugExposureData = RTHandles.Alloc(1, 1, colorFormat: ExposureFormat,
                enableRandomWrite: true, name: "Debug Exposure Info");
        }

        private static void SetExposureTextureToEmpty(RTHandle exposureTexture)
        {
            var tex = new Texture2D(1, 1, GraphicsFormat.R16G16_SFloat, TextureCreationFlags.None);
            tex.SetPixel(0, 0, new Color(1f, ColorUtils.ConvertExposureToEV100(1f), 0f, 0f));
            tex.Apply();
            Graphics.Blit(tex, exposureTexture);
            CoreUtils.Destroy(tex);
        }
        
        public void GrabExposureRequiredTextures(out RTHandle outPrevExposure, out RTHandle outNextExposure)
        {
            // One frame delay + history RTs being flipped at the beginning of the frame means we
            // have to grab the exposure marked as "previous"
            outPrevExposure = CurrentExposureTextures.Current;
            outNextExposure = CurrentExposureTextures.Previous;

            if (ResetPostProcessingHistory)
            {
                // For Dynamic Exposure, we need to undo the pre-exposure from the color buffer to calculate the correct one
                // When we reset history we must setup neutral value
                outPrevExposure = m_EmptyExposureTexture; // Use neutral texture
            }
        }
        
        public RTHandle GetExposureTexture()
        {
            // Note: GetExposureTexture(camera) must be call AFTER the call of DoFixedExposure to be correctly taken into account
            // When we use Dynamic Exposure and we reset history we can't use pre-exposure (as there is no information)
            // For this reasons we put neutral value at the beginning of the frame in Exposure textures and
            // apply processed exposure from color buffer at the end of the Frame, only for a single frame.
            // After that we re-use the pre-exposure system
            if (m_Exposure != null && ResetPostProcessingHistory && !IsExposureFixed())
                return m_EmptyExposureTexture;

            // 1x1 pixel, holds the current exposure multiplied in the red channel and EV100 value
            // in the green channel
            return GetExposureTextureHandle(CurrentExposureTextures.Current);
        }
        
        private RTHandle GetExposureTextureHandle(RTHandle rt)
        {
            return rt ?? m_EmptyExposureTexture;
        }

        public RTHandle GetExposureDebugData()
        {
            return m_DebugExposureData;
        }
        
        public RTHandle GetPreviousExposureTexture()
        {
            // If the history was reset in the previous frame, then the history buffers were actually rendered with a neutral EV100 exposure multiplier
            return DidResetPostProcessingHistoryInLastFrame && !IsExposureFixed() ?
                m_EmptyExposureTexture : GetExposureTextureHandle(CurrentExposureTextures.Previous);
        }
        
        public bool IsExposureFixed()
        {
            if (m_Exposure == null) return true;
            return m_Exposure.mode.value == Exposure.ExposureMode.Fixed;
            // || _automaticExposure.mode.value == ExposureMode.UsePhysicalCamera;
        }
        
        public bool CanRunFixedExposurePass() => IsExposureFixed()
                                                 && CurrentExposureTextures.Current != null;
        
        private struct ExposureTextures
        {
            public RTHandle Current;
            
            public RTHandle Previous;

            public void Clear()
            {
                Current = null;
                Previous = null;
            }
        }
        
        private const GraphicsFormat ExposureFormat = GraphicsFormat.R32G32_SFloat;
        
        private ExposureTextures m_ExposureTextures = new() {Current = null, Previous = null };

        private ExposureTextures CurrentExposureTextures => m_ExposureTextures;
        
        private void SetupExposureTextures()
        {
            var currentTexture = GetCurrentFrameRT((int)FrameHistoryType.Exposure);
            if (currentTexture == null)
            {
                RTHandle Allocator(string id, int frameIndex, RTHandleSystem rtHandleSystem)
                {
                    // r: multiplier, g: EV100
                    var rt = rtHandleSystem.Alloc(1, 1, colorFormat: ExposureFormat,
                        enableRandomWrite: true, name: $"{id} Exposure Texture {frameIndex}"
                    );
                    SetExposureTextureToEmpty(rt);
                    return rt;
                }

                currentTexture = AllocHistoryFrameRT((int)FrameHistoryType.Exposure, Allocator, 2);
            }

            // One frame delay + history RTs being flipped at the beginning of the frame means we
            // have to grab the exposure marked as "previous"
            m_ExposureTextures.Current = GetPreviousFrameRT((int)FrameHistoryType.Exposure);
            m_ExposureTextures.Previous = currentTexture;
        }
    }
}