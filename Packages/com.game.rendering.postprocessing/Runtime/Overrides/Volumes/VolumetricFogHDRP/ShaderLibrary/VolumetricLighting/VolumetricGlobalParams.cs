using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct VolumetricGlobalParams
    {
        // Volumetric lighting / Fog.
        public Vector4 _PlanetCenterRadius;
        public Vector4 _PlanetUpAltitude;
        public int _FogEnabled;
        public int _PBRFogEnabled;
        public int _EnableVolumetricFog;
        public float _MaxFogDistance;
        public Vector4 _FogColor; // color in rgb
        public float _FogColorMode;
        // public float _GlobalMipBias;
        public float _GlobalMipBiasPow2;
        public uint _RayTracingCheckerIndex;
        public Vector4 _MipFogParameters;
        public Vector4 _HeightFogBaseScattering;
        public float _HeightFogBaseExtinction;
        public float _HeightFogBaseHeight;
        public float _GlobalFogAnisotropy;
        public int _VolumetricFilteringEnabled;
        public Vector2 _HeightFogExponents; // { 1/H, H }
        public int _FogDirectionalOnly;
        public float _FogGIDimmer;

        // VBuffer
        public Vector4 _VBufferViewportSize;           // { w, h, 1/w, 1/h }
        public Vector4 _VBufferLightingViewportScale;  // Necessary us to work with sub-allocation (resource aliasing) in the RTHandle system
        public Vector4 _VBufferLightingViewportLimit;  // Necessary us to work with sub-allocation (resource aliasing) in the RTHandle system
        public Vector4 _VBufferDistanceEncodingParams; // See the call site for description
        public Vector4 _VBufferDistanceDecodingParams; // See the call site for description
        public uint _VBufferSliceCount;
        public float _VBufferRcpSliceCount;
        public float _VBufferRcpInstancedViewCount;  // Used to remap VBuffer coordinates for XR
        public float _VBufferLastSliceDist;          // The distance to the middle of the last slice

        public Vector4 _ShadowAtlasSize;
        public Vector4 _CascadeShadowAtlasSize;
        public Vector4 _AreaShadowAtlasSize;
        public Vector4 _CachedShadowAtlasSize;
        public Vector4 _CachedAreaShadowAtlasSize;
    }
}
