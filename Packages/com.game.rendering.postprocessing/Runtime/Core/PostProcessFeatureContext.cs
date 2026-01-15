using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    // HDRP 有一个HDCamera 可以方便的把各个View的信息配置在里面 实现RT 数据的分离
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
        
        private GPUCopy m_GPUCopy;
        public GPUCopy GPUCopy => m_GPUCopy;
        private MipGenerator m_MipGenerator;
        public MipGenerator MipGenerator => m_MipGenerator;
        
        
        private ShaderVariablesGlobal m_ShaderVariablesGlobal;
        
        public bool RequireHistoryColor { get; internal set; }
       
        
        private readonly Dictionary<CameraType, PostProcessCamera> m_CameraDataMap = new();
        public void Setup(Camera camera)
        {
            m_GPUCopy ??= new GPUCopy();
            m_MipGenerator ??= new MipGenerator();

            if (!m_CameraDataMap.ContainsKey(camera.cameraType))
            {
                PostProcessCamera data = new(camera);
                m_CameraDataMap.Add(camera.cameraType, data);
            }
        }

        public PostProcessCamera GetPostProcessCamera(Camera camera)
        {
            if (m_CameraDataMap.TryGetValue(camera.cameraType, out var processCamera))
                return processCamera;
            
            return null;
        }

        public void UpdateFrame(ref RenderingData renderingData)
        {
            m_FrameCount++;
            if (m_FrameCount >= uint.MaxValue)
            {
                m_FrameCount = 0;
            }

            foreach (var data in m_CameraDataMap.Values)
            {
                data.UpdateFrame(ref renderingData);
            }
        }

        public void Dispose()
        {
            m_FrameCount = 0;

            foreach (var data in m_CameraDataMap.Values)
            {
                data.Dispose();
            }
            m_CameraDataMap.Clear();
            m_MipGenerator?.Release();
       
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
            var historyRTSystem = GetPostProcessCamera(renderingData.cameraData.camera).historyRTSystem;
            m_ShaderVariablesGlobal.ColorPyramidUvScaleAndLimitPrevFrame
                = PostProcessingUtils.ComputeViewportScaleAndLimit(historyRTSystem.rtHandleProperties.previousViewportSize,
                    historyRTSystem.rtHandleProperties.previousRenderTargetSize);
        }


        #endregion
    }
}