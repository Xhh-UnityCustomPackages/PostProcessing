using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TonemappingMode = Game.Core.PostProcessing.Tonemapping.TonemappingMode;

namespace Game.Core.PostProcessing
{
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
        }


        public enum TonemappingCurve
        {
            UnrealFilmic,
            UnrealApprox,
        }

      
        public EnumParameter<TonemappingMode> mode = new(TonemappingMode.None);


        [Header("Unreal")]
        [Tooltip("使用UE版本")]
        public BoolParameter unreal = new BoolParameter(false);

        [Tooltip("蓝校正")]
        public ClampedFloatParameter blueCorrection = new ClampedFloatParameter(0.6f, 0f, 1f);

        [Tooltip("扩展色域")]
        public ClampedFloatParameter expandGamut = new ClampedFloatParameter(1f, 0f, 1f);

        [Tooltip("曲线 可调版/拟合版")]
        public EnumParameter<TonemappingCurve> curve = new(TonemappingCurve.UnrealFilmic);

        [Tooltip("斜面")]
        public ClampedFloatParameter filmSlope = new ClampedFloatParameter(0.88f, 0f, 1f);

        [Tooltip("趾部")]
        public ClampedFloatParameter filmToe = new ClampedFloatParameter(0.55f, 0f, 1f);

        [Tooltip("肩部")]
        public ClampedFloatParameter filmShoulder = new ClampedFloatParameter(0.26f, 0f, 1f);

        [Tooltip("黑色调")]
        public ClampedFloatParameter filmBlackClip = new ClampedFloatParameter(0.0f, 0f, 1f);

        [Tooltip("白色调")]
        public ClampedFloatParameter filmWhiteClip = new ClampedFloatParameter(0.04f, 0f, 1f);

        public override bool IsActive() => active && (mode.value != TonemappingMode.None);
    }


    static class TonemappingRenderer
    {
        public static class ShaderKeywordStrings
        {
            public static readonly string TonemapACES = "_TONEMAP_ACES";
            public static readonly string TonemapNeutral = "_TONEMAP_NEUTRAL";
            public static readonly string TonemapGT = "_TONEMAP_GT";
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
        }

        static public void ExecutePass(CommandBuffer cmd, Material material, Tonemapping tonemapping)
        {
            var mode = tonemapping.mode;

            material.DisableKeyword(ShaderKeywordStrings.TonemapNeutral);
            material.DisableKeyword(ShaderKeywordStrings.TonemapACES);
            material.DisableKeyword(ShaderKeywordStrings.TonemapGT);


            if (tonemapping.unreal.value)
            {
                material.SetFloat(ShaderConstants._BlueCorrection, tonemapping.blueCorrection.value);
                material.SetFloat(ShaderConstants._ExpandGamut, tonemapping.expandGamut.value);
                material.SetFloat(ShaderConstants._FilmSlope, tonemapping.filmSlope.value);
                material.SetFloat(ShaderConstants._FilmToe, tonemapping.filmToe.value);
                material.SetFloat(ShaderConstants._FilmShoulder, tonemapping.filmShoulder.value);
                material.SetFloat(ShaderConstants._FilmBlackClip, tonemapping.filmBlackClip.value);
                material.SetFloat(ShaderConstants._FilmWhiteClip, tonemapping.filmWhiteClip.value);
            }

            switch (mode.value)
            {
                case TonemappingMode.Neutral: material.EnableKeyword(ShaderKeywordStrings.TonemapNeutral); break;
                case TonemappingMode.ACES:
                    material.EnableKeyword(ShaderKeywordStrings.TonemapACES);
                    CoreUtils.SetKeyword(material, "_UNREAL", tonemapping.unreal.value);
                    CoreUtils.SetKeyword(material, "_UNREALAPPROX", tonemapping.curve.value == Tonemapping.TonemappingCurve.UnrealApprox);
                    break;
                case TonemappingMode.GT: material.EnableKeyword(ShaderKeywordStrings.TonemapGT); break;
                default: break; // None
            }
        }
    }
}
