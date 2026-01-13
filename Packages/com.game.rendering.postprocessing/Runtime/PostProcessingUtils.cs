using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //全局变量&关键字
    static class PipelineShaderIDs
    {
        public static readonly int _DepthMipChain = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _DepthPyramid = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _DepthPyramidMipLevelOffsets = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _ColorPyramidTexture = MemberNameHelpers.ShaderPropertyID();
        
        
        public static readonly int ShaderVariablesGlobal = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _ColorPyramidUvScaleAndLimitPrevFrame = MemberNameHelpers.ShaderPropertyID();
    }

    public static class PostProcessingRenderPassEvent
    {
        public const RenderPassEvent SetGlobalVariablesPass = RenderPassEvent.AfterRenderingPrePasses + 0;
        // ================================= Depth Prepass ================================================ //
        // Screen space effect need ignore transparent post depth since normal is not matched with depth.
        public const RenderPassEvent DepthPyramidPass = RenderPassEvent.AfterRenderingPrePasses + 1;
        // ==================================== Transparency =============================================== //

        public const RenderPassEvent ColorPyramidPass = RenderPassEvent.AfterRenderingTransparents + 4;
    }

    public class PostProcessingUtils
    {
        public static readonly string packagePath = "Packages/com.game.rendering.postprocessing";

        internal static void CheckRTCreated(RenderTexture rt)
        {
            // In some cases when loading a project for the first time in the editor, the internal resource is destroyed.
            // When used as render target, the C++ code will re-create the resource automatically. Since here it's used directly as an UAV, we need to check manually
            if (!rt.IsCreated())
                rt.Create();
        }
        
        public static float ComputeViewportScale(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Scale by (vp_dim / buf_dim).
            return viewportSize * rcpBufferSize;
        }

        public static float ComputeViewportLimit(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Clamp to (vp_dim - 0.5) / buf_dim.
            return (viewportSize - 0.5f) * rcpBufferSize;
        }
        
        public static Vector4 ComputeViewportScaleAndLimit(Vector2Int viewportSize, Vector2Int bufferSize)
        {
            return new Vector4(ComputeViewportScale(viewportSize.x, bufferSize.x),  // Scale(x)
                ComputeViewportScale(viewportSize.y, bufferSize.y),                 // Scale(y)
                ComputeViewportLimit(viewportSize.x, bufferSize.x),                 // Limit(x)
                ComputeViewportLimit(viewportSize.y, bufferSize.y));                // Limit(y)
        }
        
        public static Matrix4x4 CalculateNonJitterViewProjMatrix(ref CameraData cameraData)
        {
            Matrix4x4 viewMat = cameraData.GetViewMatrix();
            Matrix4x4 projMat = cameraData.GetGPUProjectionMatrixNoJitter();
            return math.mul(projMat, viewMat);
        }
        
        public static float4x4 CalculateViewProjMatrix(ref UniversalCameraData cameraData, RTHandle color)
        {
            float4x4 viewMat = cameraData.GetViewMatrix();
            float4x4 projMat = GetGPUProjectionMatrix(ref cameraData, color);
            return math.mul(projMat, viewMat);
        }

        private static Matrix4x4 GetGPUProjectionMatrix(ref UniversalCameraData cameraData, RTHandle color, int viewIndex = 0)
        {
            TemporalAA.JitterFunc jitterFunc = cameraData.IsSTPEnabled() ? StpUtils.s_JitterFunc : TemporalAA.s_JitterFunc;
            Matrix4x4 jitterMat = TemporalAA.CalculateJitterMatrix(cameraData, jitterFunc);
            // GetGPUProjectionMatrix takes a projection matrix and returns a GfxAPI adjusted version, does not set or get any state.
            return jitterMat * GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrixNoJitter(viewIndex), cameraData.IsRenderTargetProjectionMatrixFlipped(color));
        }
                
        public static float4x4 CalculateViewProjMatrix(ref CameraData cameraData)
        {
            float4x4 viewMat = cameraData.GetViewMatrix();
            float4x4 projMat = cameraData.GetGPUProjectionMatrix();
            return math.mul(projMat, viewMat);
        }
        
        #region Math
        public static float Exp2(float x)
        {
            return Mathf.Exp(x * 0.69314718055994530941723212145818f);
        }


        internal static float InterpolateOrientation(float fromValue, float toValue, float t)
        {
            // Compute the direct distance
            float directDistance = Mathf.Abs(toValue - fromValue);
            float outputValue = 0.0f;

            // Handle the two cases
            if (fromValue < toValue)
            {
                float upperRange = 360.0f - toValue;
                float lowerRange = fromValue;
                float alternativeDistance = upperRange + lowerRange;
                if (alternativeDistance < directDistance)
                {
                    float targetValue = toValue - 360.0f;
                    outputValue = fromValue + (targetValue - fromValue) * t;
                    if (outputValue < 0.0f)
                        outputValue += 360.0f;
                }
                else
                {
                    outputValue = fromValue + (toValue - fromValue) * t;
                }
            }
            else
            {
                float upperRange = 360.0f - fromValue;
                float lowerRange = toValue;
                float alternativeDistance = upperRange + lowerRange;
                if (alternativeDistance < directDistance)
                {
                    float targetValue = toValue + 360.0f;
                    outputValue = fromValue + (targetValue - fromValue) * t;
                    if (outputValue > 360.0f)
                        outputValue -= 360.0f;
                }
                else
                {
                    outputValue = fromValue + (toValue - fromValue) * t;
                }
            }

            return outputValue;
        }

        #endregion Math
    }
}
