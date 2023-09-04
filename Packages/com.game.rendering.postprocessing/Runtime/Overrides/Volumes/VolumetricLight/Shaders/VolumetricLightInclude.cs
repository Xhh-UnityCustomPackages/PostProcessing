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

        public float _Density;
        public Vector2 _RandomNumber;
        public Vector4 _MieG;// x: 1 - g^2, y: 1 + g^2, z: 2*g, w: 1/4pi
        public Vector2 _JitterOffset;
    }
}
