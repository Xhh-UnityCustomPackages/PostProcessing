using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    [GenerateHLSL]
    struct LocalVolumetricFogEngineData
    {
        public Vector3 scattering;    // [0, 1]
        public LocalVolumetricFogFalloffMode falloffMode;

        public Vector3 textureTiling;
        public int invertFade;    // bool...

        public Vector3 textureScroll;
        public float rcpDistFadeLen;

        public Vector3 rcpPosFaceFade;
        public float endTimesRcpDistFadeLen;

        public Vector3 rcpNegFaceFade;
        public LocalVolumetricFogBlendingMode blendingMode;


        public static LocalVolumetricFogEngineData GetNeutralValues()
        {
            LocalVolumetricFogEngineData data;

            data.scattering = Vector3.zero;
            data.textureTiling = Vector3.one;
            data.textureScroll = Vector3.zero;
            data.rcpPosFaceFade = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            data.rcpNegFaceFade = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            data.invertFade = 0;
            data.rcpDistFadeLen = 0;
            data.endTimesRcpDistFadeLen = 1;
            data.falloffMode = LocalVolumetricFogFalloffMode.Linear;
            data.blendingMode = LocalVolumetricFogBlendingMode.Additive;

            return data;
        }
    } // struct VolumeProperties
    
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct ShaderVariablesVolumetric
    {
        [HLSLArray(2, typeof(Matrix4x4))]
        public fixed float _VBufferCoordToViewDirWS[2 * 16];

        public float _VBufferUnitDepthTexelSpacing;
        public uint _NumVisibleLocalVolumetricFog;
        public float _CornetteShanksConstant;
        public uint _VBufferHistoryIsValid;

        public Vector4 _VBufferSampleOffset;

        public float _VBufferVoxelSize;
        public float _HaveToPad;
        public float _OtherwiseTheBuffer;
        public float _IsFilledWithGarbage;
        public Vector4 _VBufferPrevViewportSize;
        public Vector4 _VBufferHistoryViewportScale;
        public Vector4 _VBufferHistoryViewportLimit;
        public Vector4 _VBufferPrevDistanceEncodingParams;
        public Vector4 _VBufferPrevDistanceDecodingParams;

        // TODO: Remove if equals to the ones in global CB?
        public uint _NumTileBigTileX;
        public uint _NumTileBigTileY;
        public uint _MaxSliceCount;
        public float _MaxVolumetricFogDistance;

        // Voxelization data
        public Vector4 _CameraRight;

        public Matrix4x4 _CameraInverseViewProjection_NO;

        public uint _VolumeCount;
        public uint _IsObliqueProjectionMatrix;
        public uint _Padding1;
        public uint _Padding2;
    }
    
    /// <summary>Falloff mode for the local volumetric fog blend distance.</summary>
    [GenerateHLSL]
    public enum LocalVolumetricFogFalloffMode
    {
        /// <summary>Fade using a linear function.</summary>
        Linear,
        /// <summary>Fade using an exponential function.</summary>
        Exponential,
    }
    
    /// <summary>Select which mask mode to use for the local volumetric fog.</summary>
    public enum LocalVolumetricFogMaskMode
    {
        /// <summary>Use a 3D texture as mask.</summary>
        Texture,
        /// <summary>Use a material as mask. The material must use the "Fog Volume" material type in Shader Graph.</summary>
        Material,
    }
    
    /// <summary>Local volumetric fog blending mode.</summary>
    [GenerateHLSL]
    public enum LocalVolumetricFogBlendingMode
    {
        /// <summary>Replace the current fog, it is similar to disabling the blending.</summary>
        Overwrite = 0,
        /// <summary>Additively blend fog volumes. This is the default behavior.</summary>
        Additive = 1,
        /// <summary>Multiply the fog values when doing the blending. This is useful to make the fog density relative to other fog volumes.</summary>
        Multiply = 2,
        /// <summary>Performs a minimum operation when blending the volumes.</summary>
        Min = 3,
        /// <summary>Performs a maximum operation when blending the volumes.</summary>
        Max = 4,
    }
    
    class VolumeRenderingUtils
    {
        public static float MeanFreePathFromExtinction(float extinction)
        {
            return 1.0f / extinction;
        }

        public static float ExtinctionFromMeanFreePath(float meanFreePath)
        {
            return 1.0f / meanFreePath;
        }

        public static Vector3 AbsorptionFromExtinctionAndScattering(float extinction, Vector3 scattering)
        {
            return new Vector3(extinction, extinction, extinction) - scattering;
        }

        public static Vector3 ScatteringFromExtinctionAndAlbedo(float extinction, Vector3 albedo)
        {
            return extinction * albedo;
        }

        public static Vector3 AlbedoFromMeanFreePathAndScattering(float meanFreePath, Vector3 scattering)
        {
            return meanFreePath * scattering;
        }

        
    }
    
    public struct VBufferParameters
    {
        public Vector3Int viewportSize;
        public float voxelSize;
        public Vector4 depthEncodingParams;
        public Vector4 depthDecodingParams;
        
        public VBufferParameters(Vector3Int viewportSize, float depthExtent, float camNear, float camFar, float camVFoV,
            float sliceDistributionUniformity, float voxelSize)
        {
            this.viewportSize = viewportSize;
            this.voxelSize = voxelSize;

            // The V-Buffer is sphere-capped, while the camera frustum is not.
            // We always start from the near plane of the camera.

            float aspectRatio = viewportSize.x / (float)viewportSize.y;
            float farPlaneHeight = 2.0f * Mathf.Tan(0.5f * camVFoV) * camFar;
            float farPlaneWidth = farPlaneHeight * aspectRatio;
            float farPlaneMaxDim = Mathf.Max(farPlaneWidth, farPlaneHeight);
            float farPlaneDist = Mathf.Sqrt(camFar * camFar + 0.25f * farPlaneMaxDim * farPlaneMaxDim);

            float nearDist = camNear;
            float farDist = Math.Min(nearDist + depthExtent, farPlaneDist);

            float c = 2 - 2 * sliceDistributionUniformity; // remap [0, 1] -> [2, 0]
            c = Mathf.Max(c, 0.001f);                // Avoid NaNs

            depthEncodingParams = ComputeLogarithmicDepthEncodingParams(nearDist, farDist, c);
            depthDecodingParams = ComputeLogarithmicDepthDecodingParams(nearDist, farDist, c);
        }
        
        internal Vector3 ComputeViewportScale(Vector3Int bufferSize)
        {
            return new Vector3(VolumetricNormalFunctions.ComputeViewportScale(viewportSize.x, bufferSize.x),
                VolumetricNormalFunctions.ComputeViewportScale(viewportSize.y, bufferSize.y),
                VolumetricNormalFunctions.ComputeViewportScale(viewportSize.z, bufferSize.z));
        }

        internal Vector3 ComputeViewportLimit(Vector3Int bufferSize)
        {
            return new Vector3(VolumetricNormalFunctions.ComputeViewportLimit(viewportSize.x, bufferSize.x),
                VolumetricNormalFunctions.ComputeViewportLimit(viewportSize.y, bufferSize.y),
                VolumetricNormalFunctions.ComputeViewportLimit(viewportSize.z, bufferSize.z));
        }
        
        // See EncodeLogarithmicDepthGeneralized().
        static Vector4 ComputeLogarithmicDepthEncodingParams(float nearPlane, float farPlane, float c)
        {
            Vector4 depthParams = new Vector4();

            float n = nearPlane;
            float f = farPlane;

            depthParams.y = 1.0f / Mathf.Log(c * (f - n) + 1, 2);
            depthParams.x = Mathf.Log(c, 2) * depthParams.y;
            depthParams.z = n - 1.0f / c; // Same
            depthParams.w = 0.0f;

            return depthParams;
        }
        
        // See DecodeLogarithmicDepthGeneralized().
        static Vector4 ComputeLogarithmicDepthDecodingParams(float nearPlane, float farPlane, float c)
        {
            Vector4 depthParams = new Vector4();

            float n = nearPlane;
            float f = farPlane;

            depthParams.x = 1.0f / c;
            depthParams.y = Mathf.Log(c * (f - n) + 1, 2);
            depthParams.z = n - 1.0f / c; // Same
            depthParams.w = 0.0f;

            return depthParams;
        }
        
        internal float ComputeLastSliceDistance(uint sliceCount)
        {
            float d = 1.0f - 0.5f / sliceCount;
            float ln2 = 0.69314718f;

            // DecodeLogarithmicDepthGeneralized(1 - 0.5 / sliceCount)
            return depthDecodingParams.x * Mathf.Exp(ln2 * d * depthDecodingParams.y) + depthDecodingParams.z;
        }
    }
}