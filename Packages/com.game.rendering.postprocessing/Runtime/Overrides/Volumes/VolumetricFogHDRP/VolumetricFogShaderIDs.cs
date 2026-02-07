using UnityEngine;

namespace Game.Core.PostProcessing
{
    public static class VolumetricFogShaderIDs
    {
        //VbufferID
        public static readonly int _VBufferDensity = Shader.PropertyToID("_VBufferDensity");
        public static readonly int _VBufferLighting = Shader.PropertyToID("_VBufferLighting");
        public static readonly int _VBufferLightingFiltered = Shader.PropertyToID("_VBufferLightingFiltered");
        public static readonly int _VBufferHistory = Shader.PropertyToID("_VBufferHistory");
        public static readonly int _VBufferFeedback = Shader.PropertyToID("_VBufferFeedback");
        public static readonly int _VolumeBounds = Shader.PropertyToID("_VolumeBounds");
        public static readonly int _VolumeData = Shader.PropertyToID("_VolumeData");
        public static readonly int _VolumeAmbientProbeBuffer = Shader.PropertyToID("_VolumetricAmbientProbeBuffer");
        public static readonly int _PrevCamPosRWS = Shader.PropertyToID("_PrevCamPosRWS");

        public static readonly int _PreVPMatrix = Shader.PropertyToID("UNITY_MATRIX_PREV_VP");
        public static readonly int _PixelCoordToViewDirWSID = Shader.PropertyToID("_PixelCoordToViewDirWS");

        //Volumetric Material
        public static readonly int _VolumetricFogGlobalIndex = Shader.PropertyToID("_VolumetricFogGlobalIndex");
        public static readonly int _VolumetricMask = Shader.PropertyToID("_Mask");
        public static readonly int _VolumetricMaterialDataCBuffer = Shader.PropertyToID("VolumetricMaterialDataCBuffer");
        public static readonly int _VolumetricViewCount = Shader.PropertyToID("_ViewCount");
        public static readonly int _VolumetricTiling = Shader.PropertyToID("_Tiling");
        public static readonly int _VolumetricScrollSpeed = Shader.PropertyToID("_ScrollSpeed");
        public static readonly int _VolumetricMaterialData = Shader.PropertyToID("_VolumetricMaterialData");
        public static readonly int _VolumeCount = Shader.PropertyToID("_VolumeCount");
        public static readonly int _MaxSliceCount = Shader.PropertyToID("_MaxSliceCount");
        public static readonly int _VolumetricGlobalIndirectArgsBuffer = Shader.PropertyToID("_VolumetricGlobalIndirectArgsBuffer");
        public static readonly int _VolumetricGlobalIndirectionBuffer = Shader.PropertyToID("_VolumetricGlobalIndirectionBuffer");
        public static readonly int _VolumetricVisibleGlobalIndicesBuffer = Shader.PropertyToID("_VolumetricVisibleGlobalIndicesBuffer");
        public static readonly int _ShaderVariablesVolumetric = Shader.PropertyToID("ShaderVariablesVolumetric");
        public static readonly int _VBufferSampleOffset = Shader.PropertyToID("_VBufferSampleOffset");
        public static readonly int _VolumetricGlobalParams = Shader.PropertyToID("VolumetricGlobalParams");
        public static readonly int _EnableVolumetricFog = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _VBufferSliceCount = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _HeightFogBaseScattering = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _HeightFogExponents = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _HeightFogBaseHeight = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _HeightFogBaseExtinction = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _VBufferDistanceDecodingParams = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _VBufferRcpSliceCount = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _VBufferViewportSize = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _VBufferDistanceEncodingParams = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _VBufferLightingViewportLimit = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _VBufferLightingViewportScale = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _MaxFogDistance = MemberNameHelpers.ShaderPropertyID();

        //Volumetric Voxel
        public static readonly int _VolumetricMaterialObbRight = Shader.PropertyToID("_VolumetricMaterialObbRight");
        public static readonly int _VolumetricMaterialObbUp = Shader.PropertyToID("_VolumetricMaterialObbUp");
        public static readonly int _VolumetricMaterialObbExtents = Shader.PropertyToID("_VolumetricMaterialObbExtents");
        public static readonly int _VolumetricMaterialObbCenter = Shader.PropertyToID("_VolumetricMaterialObbCenter");
        public static readonly int _VolumetricMaterialRcpPosFaceFade = Shader.PropertyToID("_VolumetricMaterialRcpPosFaceFade");
        public static readonly int _VolumetricMaterialRcpNegFaceFade = Shader.PropertyToID("_VolumetricMaterialRcpNegFaceFade");
        public static readonly int _VolumetricMaterialInvertFade = Shader.PropertyToID("_VolumetricMaterialInvertFade");
        public static readonly int _VolumetricMaterialRcpDistFadeLen = Shader.PropertyToID("_VolumetricMaterialRcpDistFadeLen");
        public static readonly int _VolumetricMaterialEndTimesRcpDistFadeLen = Shader.PropertyToID("_VolumetricMaterialEndTimesRcpDistFadeLen");
        public static readonly int _VolumetricMaterialFalloffMode = Shader.PropertyToID("_VolumetricMaterialFalloffMode");
        public static readonly int _VolumetricLighting = Shader.PropertyToID("_VolumetricLightingBuffer");

        // 3D Txture
        public static readonly int _Dst3DTexture = Shader.PropertyToID("_Dst3DTexture");
        public static readonly int _Src3DTexture = Shader.PropertyToID("_Src3DTexture");
        public static readonly int _AlphaOnlyTexture = Shader.PropertyToID("_AlphaOnlyTexture");
        public static readonly int _SrcSize = Shader.PropertyToID("_SrcSize");
        public static readonly int _SrcMip = Shader.PropertyToID("_SrcMip");
        public static readonly int _SrcScale = Shader.PropertyToID("_SrcScale");
        public static readonly int _SrcOffset = Shader.PropertyToID("_SrcOffset");

        //Common
        public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        public static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");
        public static readonly int _OutputTexture = Shader.PropertyToID("_OutputTexture");
        public static readonly int _SrcOffsetAndLimit = Shader.PropertyToID("_SrcOffsetAndLimit");
        public static readonly int _DilationWidth = Shader.PropertyToID("_DilationWidth");
    }
}