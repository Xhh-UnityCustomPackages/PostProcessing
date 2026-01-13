using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/屏幕空间反射 (Screen Space Reflection)")]
    public class ScreenSpaceReflection : VolumeSetting
    {
        public ScreenSpaceReflection()
        {
            displayName = "屏幕空间反射 (Screen Space Reflection)";
        }
        
        public BoolParameter Enable = new BoolParameter(false);
        
        public override bool IsActive() => Enable.value;
        
        [Tooltip("模式")] 
        public EnumParameter<RaytraceModes> mode = new(RaytraceModes.LinearTracing);
        
        /// <summary>Screen Space Reflections Algorithm used.</summary>
        public EnumParameter<ScreenSpaceReflectionAlgorithm> usedAlgorithm = new (ScreenSpaceReflectionAlgorithm.Approximation);

        [Tooltip("是否开启光线多次弹射,开启后会使用上一帧反射后的屏幕颜色,实现时域上的多次反弹")]
        public BoolParameter enableMultiBounce = new (true);
        
        [Space(6)]
        [Tooltip("强度")]
        public ClampedFloatParameter intensity = new(1f, 0f, 5f);
        
        [Tooltip("Controls the smoothness value at which HDRP activates SSR and the smoothness-controlled fade out stops.")]
        public ClampedFloatParameter minSmoothness = new (0.9f, 0.0f, 1.0f);
        [Tooltip("Controls the smoothness value at which the smoothness-controlled fade out starts. The fade is in the range [Min Smoothness, Smoothness Fade Start]")]
        public ClampedFloatParameter smoothnessFadeStart = new (0.9f, 0.0f, 1.0f);
        
        // [Tooltip("值越大, 未追踪部分天空颜色会越多, 过度边界会越硬")]
        // public ClampedFloatParameter distanceFade = new(0.2f, 0f, 1f);
        
        [Tooltip("边缘渐变")]
        [InspectorName("Screen Edge Fade Distance")]
        public ClampedFloatParameter vignette = new(1f, 0f, 1f);
        
        [Header("Performance")]
        [Tooltip("分辨率")] 
        public EnumParameter<Resolution> resolution = new(Resolution.Full);
        
        [Tooltip("最大追踪次数")]
        public ClampedIntParameter maximumIterationCount = new(256, 1, 256);

        [Tooltip("追踪步长, 越大精度越低, 追踪范围越大, 越节省追踪次数")]
        public ClampedFloatParameter stepSize = new(0.1f, 0f, 1f);
        
        public ClampedFloatParameter thickness = new(0.1f, 0.05f, 1f);
        
        [Tooltip("最大追踪距离")]
        public MinFloatParameter maximumMarchDistance = new(100f, 0f);

        /// <summary>
        /// Controls the amount of accumulation (0 no accumulation, 1 just accumulate)
        /// </summary>
        [Header("Accumulation")]
        public ClampedFloatParameter accumulationFactor = new(0.75f, 0.0f, 1.0f);
        
        /// <summary>
        /// For PBR: Controls the bias of accumulation (0 no bias, 1 bias ssr)
        /// </summary>
        public ClampedFloatParameter biasFactor = new(0.5f, 0.0f, 1.0f);
        
        /// <summary>
        /// Controls the likelihood history will be rejected based on the previous frame motion vectors of both the surface and the hit object in world space.
        /// </summary>
        // If change this value, must change on ScreenSpaceReflections.compute on 'float speed = saturate((speedDst + speedSrc) * 128.0f / (...)'
        public ClampedFloatParameter speedRejectionParam = new(0.5f, 0.0f, 1.0f);

        /// <summary>
        /// Controls the upper range of speed. The faster the objects or camera are moving, the higher this number should be.
        /// </summary>
        // If change this value, must change on ScreenSpaceReflections.compute on 'float speed = saturate((speedDst + speedSrc) * 128.0f / (...)'
        public ClampedFloatParameter speedRejectionScalerFactor = new(0.2f, 0.001f, 1f);
        
        /// <summary>
        /// When enabled, world space speed from Motion vector is used to reject samples.
        /// </summary>
        public BoolParameter enableWorldSpeedRejection = new(false);
        
        
        [Header("Debug")]
        public EnumParameter<DebugMode> debugMode = new(DebugMode.Disabled);

        public ClampedFloatParameter split = new(0.5f, 0f, 1f);
        
        
        public enum RaytraceModes
        {
            LinearTracing = 0,
            HiZTracing = 1
        }
        
        public enum Resolution
        {
            Half,
            Full,
            Double
        }
        
        /// <summary>
        /// Screen Space Reflection Algorithm
        /// </summary>
        public enum ScreenSpaceReflectionAlgorithm
        {
            /// <summary>Legacy SSR approximation.</summary>
            Approximation,
            /// <summary>Screen Space Reflection, Physically Based with Accumulation through multiple frame.</summary>
            PBRAccumulation
        }

        public enum DebugMode
        {
            Disabled,
            SSROnly,
            Split,
        }

        public enum Preset
        {
            Fast = 10,
            Medium = 20,
            High = 30,
            Superb = 35,
            Ultra = 40
        }

        public void ApplyPreset(Preset preset)
        {
            switch (preset)
            {
                case Preset.Fast:
                    resolution.value = Resolution.Half;
                    stepSize.value = 1.0f;
                    maximumIterationCount.value = 16;
                    break;
                case Preset.Medium:
                    resolution.value = Resolution.Half;
                    stepSize.value = 2.5f;
                    maximumIterationCount.value = 32;
                    break;
                case Preset.High:
                    resolution.value = Resolution.Full;
                    stepSize.value = 3f;
                    maximumIterationCount.value = 64;
                    break;
                case Preset.Superb:
                    resolution.value = Resolution.Double;
                    stepSize.value = 6f;
                    maximumIterationCount.value = 128;
                    break;
                case Preset.Ultra:
                    resolution.value = Resolution.Double;
                    stepSize.value = 4f;
                    maximumIterationCount.value = 256;
                    break;
            }
        }
    }
}