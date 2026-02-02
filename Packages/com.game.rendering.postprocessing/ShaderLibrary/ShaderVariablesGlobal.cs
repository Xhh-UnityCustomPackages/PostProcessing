using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    
    // Global Constant Buffers - b registers. Unity supports a maximum of 16 global constant buffers.
    enum ConstantRegister
    {
        Global = 0,
        XR = 1,
        PBRSky = 2,
        RayTracing = 3,
        RayTracingLightLoop = 4,
        WorldEnvLightReflectionData = 5,
        APV = APVConstantBufferRegister.GlobalRegister,
    }
    
    [GenerateHLSL(needAccessors = false, generateCBuffer = true, constantRegister = (int)ConstantRegister.Global)]
    public struct ShaderVariablesGlobal
    {
        // ================================
        //     PER VIEW CONSTANTS
        // ================================
        // TODO: all affine matrices should be 3x4.
        // public Matrix4x4 _ViewMatrix;
        // public Matrix4x4 _InvViewMatrix;
        // public Matrix4x4 _ProjMatrix;
        // public Matrix4x4 _ViewProjMatrix;
        // public Matrix4x4 _InvViewProjMatrix;
        // public Matrix4x4 _PrevViewProjMatrix; // non-jittered
        // public Matrix4x4 _PrevInvViewProjMatrix; // non-jittered
        
        // TAA Frame Index ranges from 0 to 1023.
        public Vector4 _TaaFrameInfo;               // { taaSharpenStrength, unused, taaFrameIndex, taaEnabled ? 1 : 0 }
        
        public Vector4 _ColorPyramidUvScaleAndLimitPrevFrame;
    }
}