using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TonemappingMode = Game.Core.PostProcessing.Tonemapping.TonemappingMode;

namespace Game.Core.PostProcessing
{
    //https://zhuanlan.zhihu.com/p/662140618
    [Serializable, VolumeComponentMenu("Post-processing Custom/Tonemapping")]
    public class Tonemapping : VolumeSetting
    {
        public Tonemapping()
        {
            displayName = "Custom Tonemapping";
        }


        public enum TonemappingMode
        {
            None,
            Neutral, // Neutral tonemapper
            ACES,    // ACES Filmic reference tonemapper (custom approximation)
            GT,      // Gran Turismo
            CustomGT,  // Full Gran Turismo   
            NAES,
            Log2Tonmap,
        }


        public enum TonemappingCurve
        {
            UnrealFilmic,
            UnrealApprox,
        }

      
        public EnumParameter<TonemappingMode> mode = new(TonemappingMode.None);


        [Header("Unreal")]
        [Tooltip("使用UE版本")]
        public BoolParameter unreal = new (false);

        [Tooltip("蓝校正")]
        public ClampedFloatParameter blueCorrection = new (0.6f, 0f, 1f);

        [Tooltip("扩展色域")]
        public ClampedFloatParameter expandGamut = new (1f, 0f, 1f);

        [Tooltip("曲线 可调版/拟合版")]
        public EnumParameter<TonemappingCurve> curve = new(TonemappingCurve.UnrealFilmic);

        [Tooltip("斜面")]
        public ClampedFloatParameter filmSlope = new (0.88f, 0f, 1f);

        [Tooltip("趾部")]
        public ClampedFloatParameter filmToe = new (0.55f, 0f, 1f);

        [Tooltip("肩部")]
        public ClampedFloatParameter filmShoulder = new (0.26f, 0f, 1f);

        [Tooltip("黑色调")]
        public ClampedFloatParameter filmBlackClip = new (0.0f, 0f, 1f);

        [Tooltip("白色调")]
        public ClampedFloatParameter filmWhiteClip = new (0.04f, 0f, 1f);
        
        
        [Header("Custom GT")]
        public ClampedFloatParameter maximumBrightness = new (1f, 0f, 3f);
        public ClampedFloatParameter contrast = new (1f, 0.5f, 1.5f);
        public ClampedFloatParameter linearSectionStart = new (0.22f, 0f, 3f);
        public ClampedFloatParameter linearSectionLength = new (0.4f, 0f, 1f);
        public ClampedFloatParameter blackTightness = new (1.33f, 1f, 3f);
        public ClampedFloatParameter blackMin = new (0f, 0f, 3f);
        public override bool IsActive() => active && (mode.value != TonemappingMode.None);
    }


    static class TonemappingRenderer
    {
        public static class ShaderKeywordStrings
        {
            public static readonly string TonemapACES = "_TONEMAP_ACES";
            public static readonly string TonemapNeutral = "_TONEMAP_NEUTRAL";
            public static readonly string TonemapGT = "_TONEMAP_GT";
            public static readonly string TonemapGTCustom = "_TONEMAP_GT_CUSTOM";
            public static readonly string TonemapLog2 = "_TONEMAP_LOG2";
            public static readonly string TonemapNAES = "_TONEMAP_NAES";
        }

        public static class ShaderConstants
        {
            public static readonly int _BlueCorrection = Shader.PropertyToID("_BlueCorrection");
            public static readonly int _ExpandGamut = Shader.PropertyToID("_ExpandGamut");
            public static readonly int _FilmSlope = Shader.PropertyToID("_FilmSlope");
            public static readonly int _FilmToe = Shader.PropertyToID("_FilmToe");
            public static readonly int _FilmShoulder = Shader.PropertyToID("_FilmShoulder");
            public static readonly int _FilmBlackClip = Shader.PropertyToID("_FilmBlackClip");
            public static readonly int _FilmWhiteClip = Shader.PropertyToID("_FilmWhiteClip");
            
            public static readonly int _GTToneParam1 = Shader.PropertyToID("_GTToneParam1");
            public static readonly int _GTToneParam2 = Shader.PropertyToID("_GTToneParam2");
            
            public static string GetModeKeyword(TonemappingMode type)
            {
                switch (type)
                {
                    case TonemappingMode.ACES:
                        return ShaderKeywordStrings.TonemapACES;
                    case TonemappingMode.GT:
                        return ShaderKeywordStrings.TonemapGT;
                    case TonemappingMode.CustomGT:
                        return ShaderKeywordStrings.TonemapGTCustom;
                    case TonemappingMode.Log2Tonmap:
                        return ShaderKeywordStrings.TonemapLog2;
                    case TonemappingMode.NAES:
                        return ShaderKeywordStrings.TonemapNAES;
                    default:
                        return "_";
                }
            }
        }

        static public void ExecutePass(CommandBuffer cmd, Material material, Tonemapping tonemapping)
        {
            var mode = tonemapping.mode;

            material.DisableKeyword(ShaderKeywordStrings.TonemapNeutral);
            material.DisableKeyword(ShaderKeywordStrings.TonemapACES);
            material.DisableKeyword(ShaderKeywordStrings.TonemapGT);
            material.DisableKeyword(ShaderKeywordStrings.TonemapGTCustom);
            material.DisableKeyword(ShaderKeywordStrings.TonemapLog2);
            material.DisableKeyword(ShaderKeywordStrings.TonemapNAES);


            if (tonemapping.mode.value == TonemappingMode.ACES && tonemapping.unreal.value)
            {
                material.SetFloat(ShaderConstants._BlueCorrection, tonemapping.blueCorrection.value);
                material.SetFloat(ShaderConstants._ExpandGamut, tonemapping.expandGamut.value);
                material.SetFloat(ShaderConstants._FilmSlope, tonemapping.filmSlope.value);
                material.SetFloat(ShaderConstants._FilmToe, tonemapping.filmToe.value);
                material.SetFloat(ShaderConstants._FilmShoulder, tonemapping.filmShoulder.value);
                material.SetFloat(ShaderConstants._FilmBlackClip, tonemapping.filmBlackClip.value);
                material.SetFloat(ShaderConstants._FilmWhiteClip, tonemapping.filmWhiteClip.value);
            }
            else if (tonemapping.mode.value == TonemappingMode.CustomGT)
            {
                material.SetVector(ShaderConstants._GTToneParam1, new Vector4(
                    tonemapping.maximumBrightness.value,
                    tonemapping.contrast.value,
                    tonemapping.linearSectionStart.value, 
                    tonemapping.linearSectionLength.value));
                material.SetVector(ShaderConstants._GTToneParam2, new Vector4(
                    tonemapping.blackTightness.value,
                    tonemapping.blackMin.value,
                    0,
                    0));
            }

            switch (mode.value)
            {
                case TonemappingMode.Neutral: 
                    material.EnableKeyword(ShaderKeywordStrings.TonemapNeutral); break;
                case TonemappingMode.ACES:
                    material.EnableKeyword(ShaderKeywordStrings.TonemapACES);
                    CoreUtils.SetKeyword(material, "_UNREAL", tonemapping.unreal.value);
                    CoreUtils.SetKeyword(material, "_UNREALAPPROX", tonemapping.curve.value == Tonemapping.TonemappingCurve.UnrealApprox);
                    break;
                case TonemappingMode.GT: 
                    material.EnableKeyword(ShaderKeywordStrings.TonemapGT); 
                    break;
                case TonemappingMode.CustomGT: 
                    material.EnableKeyword(ShaderKeywordStrings.TonemapGTCustom); 
                    break;
                case TonemappingMode.Log2Tonmap: 
                    material.EnableKeyword(ShaderKeywordStrings.TonemapLog2); 
                    break;
                case TonemappingMode.NAES: 
                    material.EnableKeyword(ShaderKeywordStrings.TonemapNAES); 
                    break;
                default: 
                    break; // None
            }
        }
    }
}
