using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Post-processing Custom/ScreenSpaceReflection", typeof(UniversalRenderPipeline))]
    public class ScreenSpaceReflection : VolumeSetting
    {
        public enum Resolution
        {
            Half,
            Full,
            Double
        }

        public enum DebugMode
        {
            Disabled,
            SSROnly,
            IndirectSpecular,
        }

        [Serializable]
        public class ResolutionParameter : VolumeParameter<Resolution> { }

        [Serializable]
        public class DebugModeParameter : VolumeParameter<DebugMode> { }

        [Tooltip("分辨率")]
        public ResolutionParameter resolution = new ResolutionParameter { value = Resolution.Double };

        [Tooltip("最大追踪次数, 移动端会被固定到10次")]
        public ClampedIntParameter maximumIterationCount = new ClampedIntParameter(256, 1, 256);

        [Tooltip("模糊迭代次数")]
        public ClampedIntParameter blurIterations = new ClampedIntParameter(3, 1, 4);

        [Space(6)]
        [Tooltip("强度")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 5f);

        [Tooltip("实际上是追踪步长, 越大精度越低, 追踪范围越大, 越节省追踪次数")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(8f, 1f, 64f);

        [Tooltip("最大追踪距离")]
        public MinFloatParameter maximumMarchDistance = new MinFloatParameter(100f, 0f);

        [Tooltip("值越大, 未追踪部分天空颜色会越多, 过度边界会越硬")]
        public ClampedFloatParameter distanceFade = new ClampedFloatParameter(0.02f, 0f, 1f);

        [Tooltip("渐变")]
        public ClampedFloatParameter vignette = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("减少闪烁问题, 需要MotionVector, SceneView未处理")]
        public BoolParameter antiFlicker = new BoolParameter(true);

        [Tooltip("Unity老版本算法")]
        public BoolParameter oldMethod = new BoolParameter(false);


        public DebugModeParameter debugMode = new DebugModeParameter { value = DebugMode.Disabled };
        public override bool IsActive() => intensity.value > 0;
    }

    [PostProcess("ScreenSpaceReflection", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class ScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceReflection>
    {
        static class ShaderConstants
        {
            internal static readonly int ResolveTex = Shader.PropertyToID("_ResolveTex");
            internal static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
            internal static readonly int TestTex = Shader.PropertyToID("_TestTex");
            internal static readonly int HistoryTex = Shader.PropertyToID("_HistoryTex");

            internal static readonly int ViewMatrix = Shader.PropertyToID("_ViewMatrixSSR");
            internal static readonly int InverseViewMatrix = Shader.PropertyToID("_InverseViewMatrixSSR");
            internal static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrixSSR");
            internal static readonly int ScreenSpaceProjectionMatrix = Shader.PropertyToID("_ScreenSpaceProjectionMatrixSSR");

            internal static readonly int Params1 = Shader.PropertyToID("_Params1");
            internal static readonly int Params2 = Shader.PropertyToID("_Params2");
            internal static readonly int Offset = Shader.PropertyToID("_Offset");

            public static int[] _BlurMipUp;
            public static int[] _BlurMipDown;

            public static string GetDebugKeyword(ScreenSpaceReflection.DebugMode debugMode)
            {
                switch (debugMode)
                {
                    case ScreenSpaceReflection.DebugMode.SSROnly:
                        return "DEBUG_SCREEN_SPACE_REFLECTION";
                    case ScreenSpaceReflection.DebugMode.IndirectSpecular:
                        return "DEBUG_INDIRECT_SPECULAR";
                    case ScreenSpaceReflection.DebugMode.Disabled:
                    default:
                        return "_";
                }
            }
        }


        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {

        }
    }
}
