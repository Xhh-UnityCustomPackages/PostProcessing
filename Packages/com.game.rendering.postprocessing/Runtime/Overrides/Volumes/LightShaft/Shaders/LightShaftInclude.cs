using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    public struct LightShaftInclude
    {
        public Vector4 _LightSource;
        public Vector4 _LightShaftParameters;
        public Vector4 _RadialBlurParameters;
        public float _ShaftsDensity;
        public float _ShaftsWeight;
        public float _ShaftsDecay;
        public float _ShaftsExposure;
        public Color _BloomTintAndThreshold;
        public float _ShaftsAtten;
    }
}
