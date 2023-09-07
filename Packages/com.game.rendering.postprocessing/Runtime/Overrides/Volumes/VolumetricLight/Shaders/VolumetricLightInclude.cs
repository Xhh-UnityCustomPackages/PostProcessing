using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    public struct VolumetricLightInclude
    {
        public float _MaxRayLength;
        public int _SampleCount;
        public float _Intensity;

        public float _SkyboxExtinction;
        public float _ScatteringCoef;
        public float _ExtinctionCoef;
        public float _MieG;

        public Vector3 _LightDirection;
        public Color _LightColor;

        // Noise
        public float _NoiseIntensity;
        public float _NoiseScale;
        public Vector2 _NoiseOffset;
        public Vector3 _NoiseVelocity;
    }
}
