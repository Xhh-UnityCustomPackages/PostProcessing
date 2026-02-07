using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Collections;

namespace Game.Core.PostProcessing
{
	[PostProcess("Volumetric Fog HDRP", PostProcessInjectionPoint.AfterRenderingSkybox)]
	public partial class VolumetricFogHDRPRenderer : PostProcessVolumeRenderer<VolumetricFogHDRP>
    {
	    internal static Vector3Int s_CurrentVolumetricBufferSize;
	    
	    internal static Vector2[] m_xySeq = new Vector2[7];
	    internal static float[] m_zSeq = { 7.0f / 14.0f, 3.0f / 14.0f, 11.0f / 14.0f, 5.0f / 14.0f, 9.0f / 14.0f, 1.0f / 14.0f, 13.0f / 14.0f };
	    
	    private ComputeMaxDepthPass m_ComputeMaxDephtPass;
	    ComputeHeightFogVoxel m_ComputeHeightFogVoxel;
	    ComputeLocalVolumetricFogVoxel m_ComputeLocalVolumetricFogVoxel;
	    DrawLocalVolumetricFog m_DrawLocalVolumetricFog;
	    ComputeVolumetricLighting m_ComputeVolumetricLighting;
	    RenderAtmosphereScattering m_RenderAtmosphereScattering;
	    
	    internal static VolumetricGlobalParams volumetricGlobalCB = new ();
	    internal static ShaderVariablesVolumetric m_ShaderVariablesVolumetricCB = new ();
	    
	    List<OrientedBBox> m_VisibleVolumeBounds = null;
	    List<LocalVolumetricFogEngineData> m_VisibleVolumeData = null;
	    List<int> m_GlobalVolumeIndices = null;
	    internal static List<LocalVolumetricFog> m_VisibleLocalVolumetricFogVolumes = null;
	    NativeArray<uint> m_VolumetricFogSortKeys;
	    NativeArray<uint> m_VolumetricFogSortKeysTemp;
	    
	    const int k_VolumetricFogPriorityMaxValue = 1048576; // 2^20 because there are 20 bits in the volumetric fog sort key
	    
	    internal static ComputeBuffer m_VisibleVolumeBoundsBuffer = null;
	    internal static GraphicsBuffer m_VisibleVolumeGlobalIndices = null;
	    
	    public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth;
	    public override bool renderToCamera => false;
	    // public override bool dontCareSourceTargetCopy => true;
	    
	    
	    static internal RTHandle m_MaxZHandle;
	    static internal RTHandle m_DensityBuffer;
	    static internal RTHandle m_LightingBuffer;
	    
	    protected override void Setup()
	    {
		    profilingSampler = new ProfilingSampler("Volumetric Fog");
		    
		    m_ComputeMaxDephtPass = new (postProcessData);
		    m_ComputeMaxDephtPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
		    m_ComputeHeightFogVoxel = new ComputeHeightFogVoxel(postProcessData);
		    m_ComputeHeightFogVoxel.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
		    m_ComputeLocalVolumetricFogVoxel = new ComputeLocalVolumetricFogVoxel();
		    m_ComputeLocalVolumetricFogVoxel.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
		    m_DrawLocalVolumetricFog = new DrawLocalVolumetricFog(postProcessData);
		    m_DrawLocalVolumetricFog.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
		    m_ComputeVolumetricLighting = new ComputeVolumetricLighting(postProcessData);
		    m_ComputeVolumetricLighting.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
		    m_RenderAtmosphereScattering = new RenderAtmosphereScattering(postProcessData);
		    m_RenderAtmosphereScattering.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
		    
		    
		    int maxLocalVolumetricFogs = 256;
		    LocalVolumetricFogManager.manager.InitializeGraphicsBuffers(maxLocalVolumetricFogs);
		    m_VisibleVolumeBounds = new List<OrientedBBox>();
		    m_VisibleVolumeData = new List<LocalVolumetricFogEngineData>();
		    m_GlobalVolumeIndices = new List<int>(maxLocalVolumetricFogs);
		    m_VisibleLocalVolumetricFogVolumes = new List<LocalVolumetricFog>();
		    m_VisibleVolumeBoundsBuffer = new ComputeBuffer(maxLocalVolumetricFogs, Marshal.SizeOf(typeof(OrientedBBox)));
		    m_VisibleVolumeGlobalIndices = new GraphicsBuffer(GraphicsBuffer.Target.Raw, maxLocalVolumetricFogs, Marshal.SizeOf(typeof(uint)));

		    m_VolumetricFogSortKeys = new NativeArray<uint>(maxLocalVolumetricFogs, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
		    m_VolumetricFogSortKeysTemp = new NativeArray<uint>(maxLocalVolumetricFogs, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
	    }

	    public override void Dispose(bool disposing)
	    {
		    m_ComputeMaxDephtPass?.Dispose();
		    m_ComputeHeightFogVoxel?.Dispose();
		    m_DrawLocalVolumetricFog?.Dispose();
		    m_ComputeVolumetricLighting?.Dispose();
		    m_RenderAtmosphereScattering?.Dispose();
		    
		    m_DensityBuffer?.Release();
		    m_DensityBuffer = null;
		    
		    if (m_VolumetricFogSortKeys.IsCreated)
			    m_VolumetricFogSortKeys.Dispose();
		    if (m_VolumetricFogSortKeysTemp.IsCreated)
			    m_VolumetricFogSortKeysTemp.Dispose();
		    
		    CoreUtils.SafeRelease(m_VisibleVolumeBoundsBuffer);
		    CoreUtils.SafeRelease(m_VisibleVolumeGlobalIndices);
	    }

	    public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
	    {
		    // ConstantBuffer.PushGlobal(volumetricGlobalCB, VolumetricFogShaderIDs._VolumetricGlobalParams);
		    
		    //初始化VBuffer数据
		    ReinitializeVolumetricBufferParams(postProcessData);
		    //更新分割相机的VBuffer数据并写入VolumetricCameraParams用于后续计算
		    UpdateVolumetricBufferParams(settings, postProcessData);
		    
		    //准备用于计算重投影数据
		    ResizeVolumetricHistoryBuffers(postProcessData);
		    VolumetricFogHDRP.UpdateShaderVariablesGlobalCB(ref volumetricGlobalCB);
		    //更新GPU全局变量
		    UpdateShaderVariablesGlobalVolumetrics(ref volumetricGlobalCB, postProcessData);
		    //写入LocalVolumetricFog数据
		    PrepareVisibleLocalVolumetricFogList(postProcessData);
		    
		    int frameIndex = (int)VolumetricFrameIndex(postProcessData);
		    var currIdx = (frameIndex + 0) & 1;
		    var currParams = postProcessData.vBufferParams[currIdx];
		    
		    var cvp = currParams.viewportSize;
		    var res = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
                 
		    ComputeVolumetricFogSliceCountAndScreenFraction(settings, out var maxSliceCount, out _);
		    UpdateShaderVariableslVolumetrics(ref m_ShaderVariablesVolumetricCB, postProcessData, res, maxSliceCount, true);
	    }

	    public override void AddRenderPasses(ref RenderingData renderingData)
	    {
		    if (!settings.enableVolumetricFog.value)
			    return;
		    var renderer = renderingData.cameraData.renderer;
		    //计算当前最大深度用于后续体素计算
		    renderer.EnqueuePass(m_ComputeMaxDephtPass);
		    //计算高度雾体积
		    renderer.EnqueuePass(m_ComputeHeightFogVoxel);
		    //计算LocalFog体积
		    renderer.EnqueuePass(m_ComputeLocalVolumetricFogVoxel);
		    //将localfog渲染至densityBuffer
		    renderer.EnqueuePass(m_DrawLocalVolumetricFog);
		    //光照计算
		    renderer.EnqueuePass(m_ComputeVolumetricLighting);
		    //大气散射
		    renderer.EnqueuePass(m_RenderAtmosphereScattering);
		    
		    
		    postProcessData.prevPos = renderingData.cameraData.camera.transform.position;
	    }

	    internal void ReinitializeVolumetricBufferParams(PostProcessData hdCamera)
	    {
		    bool fog = settings.enableVolumetricFog.value;
		    bool init = hdCamera.vBufferParams != null;

		    if (fog ^ init)
		    {
			    if (init)
			    {
				    // Deinitialize.
				    hdCamera.vBufferParams = null;
			    }
			    else
			    {
				    // Initialize.
				    // Start with the same parameters for both frames. Then update them one by one every frame.
				    var parameters = ComputeVolumetricBufferParameters(settings, hdCamera);
				    hdCamera.vBufferParams = new VBufferParameters[2];
				    hdCamera.vBufferParams[0] = parameters;
				    hdCamera.vBufferParams[1] = parameters;
			    }
		    }
	    }

	    static internal VBufferParameters ComputeVolumetricBufferParameters(VolumetricFogHDRP settings, PostProcessData hdCamera)
	    {
		    float voxelSize = 0;
		    Vector3Int viewportSize = ComputeVolumetricViewportSize(settings, hdCamera, ref voxelSize);
		    
		    return new VBufferParameters(viewportSize, settings.depthExtent.value,
			    hdCamera.camera.nearClipPlane,
			    hdCamera.camera.farClipPlane,
			    hdCamera.camera.fieldOfView,
			    settings.sliceDistributionUniformity.value,
			    voxelSize);
	    }

	    static internal Vector3Int ComputeVolumetricViewportSize(VolumetricFogHDRP settings, PostProcessData hdCamera, ref float voxelSize)
	    {
		    int viewportWidth = hdCamera.actualWidth;
		    int viewportHeight = hdCamera.actualHeight;
		    ComputeVolumetricFogSliceCountAndScreenFraction(settings, out var sliceCount, out var screenFraction);
		    if (settings.fogControlMode == VolumetricFogHDRP.FogControl.Balance)
		    {
			    // Evaluate the voxel size
			    voxelSize = 1.0f / screenFraction;
		    }
		    else
		    {
			    if (settings.screenResolutionPercentage.value == VolumetricFogHDRP.optimalFogScreenResolutionPercentage)
				    voxelSize = 8;
			    else
				    voxelSize = 1.0f / screenFraction; // Does not account for rounding (same function, above)
		    }

		    int w = Mathf.RoundToInt(viewportWidth * screenFraction);
		    int h = Mathf.RoundToInt(viewportHeight * screenFraction);

		    // Round to nearest multiple of viewCount so that each views have the exact same number of slices (important for XR)
		    int d = sliceCount;

		    return new Vector3Int(w, h, d);
	    }

	    static internal void ComputeVolumetricFogSliceCountAndScreenFraction(VolumetricFogHDRP settings, out int sliceCount, out float screenFraction)
	    {
		    if (settings.fogControlMode == VolumetricFogHDRP.FogControl.Balance)
		    {
			    // Evaluate the ssFraction and sliceCount based on the control parameters
			    float maxScreenSpaceFraction = (1.0f - settings.resolutionDepthRatio) * (VolumetricFogHDRP.maxFogScreenResolutionPercentage - VolumetricFogHDRP.minFogScreenResolutionPercentage) + VolumetricFogHDRP.minFogScreenResolutionPercentage;
			    screenFraction = Mathf.Lerp(VolumetricFogHDRP.minFogScreenResolutionPercentage, maxScreenSpaceFraction, settings.volumetricFogBudget) * 0.01f;
			    float maxSliceCount = Mathf.Max(1.0f, settings.resolutionDepthRatio * VolumetricFogHDRP.maxFogSliceCount);
			    sliceCount = (int)Mathf.Lerp(1.0f, maxSliceCount, settings.volumetricFogBudget);
			    //Debug.Log(sliceCount);
		    }
		    //以下根据hdrp逻辑永远不会执行，目前hdrp为TODO状态，后续跟进
		    else
		    {
			    screenFraction = settings.screenResolutionPercentage.value * 0.01f;
			    sliceCount = settings.volumeSliceCount.value;
		    }
	    }

	    static internal void UpdateVolumetricBufferParams(VolumetricFogHDRP settings, PostProcessData hdCamera)
	    {
		    if (!VolumetricFogHDRP.IsVolumetricFogEnabled(hdCamera.camera))
			    return;

		    Debug.Assert(hdCamera.vBufferParams != null);
		    Debug.Assert(hdCamera.vBufferParams.Length == 2);
		    
		    var currentParams = ComputeVolumetricBufferParameters(settings, hdCamera);

		    int frameIndex = (int)VolumetricFrameIndex(hdCamera);
		    var currIdx = (frameIndex + 0) & 1;
		    var prevIdx = (frameIndex + 1) & 1;

		    hdCamera.vBufferParams[currIdx] = currentParams;
		    
		    // Handle case of first frame. When we are on the first frame, we reuse the value of original frame.
		    if (hdCamera.vBufferParams[prevIdx].viewportSize.x == 0.0f && hdCamera.vBufferParams[prevIdx].viewportSize.y == 0.0f)
		    {
			    hdCamera.vBufferParams[prevIdx] = currentParams;
		    }

		    // Update size used to create volumetric buffers.
		    s_CurrentVolumetricBufferSize = new Vector3Int(Math.Max(s_CurrentVolumetricBufferSize.x, currentParams.viewportSize.x),
			    Math.Max(s_CurrentVolumetricBufferSize.y, currentParams.viewportSize.y),
			    Math.Max(s_CurrentVolumetricBufferSize.z, currentParams.viewportSize.z));
	    }

	    static internal uint VolumetricFrameIndex(PostProcessData hdCamera)
	    {
		    // Here we do modulo 14 because we need the enable to detect a change every frame, but the accumulation is done on 7 frames (7x2=14)
		    return hdCamera.FrameCount % 14;
	    }


	    static internal void CreateVolumetricHistoryBuffers(PostProcessData hdCamera, int bufferCount)
	    {
		    if (!VolumetricFogHDRP.IsVolumetricFogEnabled(hdCamera.camera))
			    return;
		    Debug.Assert(hdCamera.volumetricHistoryBuffers == null);

		    if (hdCamera.volumetricHistoryBuffers == null)
		    {
			    hdCamera.volumetricHistoryBuffers = new RTHandle[bufferCount];
		    }


            // Allocation happens early in the frame. So we shouldn't rely on 'hdCamera.vBufferParams'.
            // Allocate the smallest possible 3D texture.
            // We will perform rescaling manually, in a custom manner, based on volume parameters.
            const int minSize = 4;

            for (int i = 0; i < bufferCount; i++)
            {
	            hdCamera.volumetricHistoryBuffers[i] = RTHandles.Alloc(minSize, minSize, minSize, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, // 8888_sRGB is not precise enough
                    dimension: TextureDimension.Tex3D, enableRandomWrite: true, name: string.Format("VBufferHistory{0}", i));

            }

            hdCamera.volumetricHistoryIsValid = false;
        }

        static readonly string[] volumetricHistoryBufferNames = new string[2] { "VBufferHistory0", "VBufferHistory1" };

        static internal void DestroyVolumetricHistoryBuffers(PostProcessData hdCamera)
        {
            if (hdCamera.volumetricHistoryBuffers == null)
                return;

            int bufferCount = hdCamera.volumetricHistoryBuffers.Length;

            for (int i = 0; i < bufferCount; i++)
            {
                RTHandles.Release(hdCamera.volumetricHistoryBuffers[i]);
            }

            hdCamera.volumetricHistoryBuffers = null;
            hdCamera.volumetricHistoryIsValid = false;
        }
	    
	    //滤波：重投影
	    static internal void ResizeVolumetricHistoryBuffers(PostProcessData hdCamera)
	    {
		    if (!hdCamera.IsVolumetricReprojectionEnabled())
			    return;

		    Debug.Assert(hdCamera.vBufferParams != null);
		    Debug.Assert(hdCamera.vBufferParams.Length == 2);
		    Debug.Assert(hdCamera.volumetricHistoryBuffers != null);

		    int frameIndex = (int)VolumetricFrameIndex(hdCamera);
		    var currIdx = (frameIndex + 0) & 1;
		    var prevIdx = (frameIndex + 1) & 1;
		    
		    var currentParams = hdCamera.vBufferParams[currIdx];
		    
		    // Render texture contents can become "lost" on certain events, like loading a new level,
		    // system going to a screensaver mode, in and out of fullscreen and so on.
		    // https://docs.unity3d.com/ScriptReference/RenderTexture.html
		    if (hdCamera.volumetricHistoryBuffers[0] == null || hdCamera.volumetricHistoryBuffers[1] == null)
		    {
			    DestroyVolumetricHistoryBuffers(hdCamera);
			    CreateVolumetricHistoryBuffers(hdCamera, hdCamera.vBufferParams.Length); // Basically, assume it's 2
		    }

		    // We only resize the feedback buffer (#0), not the history buffer (#1).
		    // We must NOT resize the buffer from the previous frame (#1), as that would invalidate its contents.
		    ResizeVolumetricBuffer(ref hdCamera.volumetricHistoryBuffers[currIdx], volumetricHistoryBufferNames[currIdx], currentParams.viewportSize.x,
			    currentParams.viewportSize.y,
			    currentParams.viewportSize.z);
	    }
	    
	    static internal void ResizeVolumetricBuffer(ref RTHandle rt, string name, int viewportWidth, int viewportHeight, int viewportDepth)
	    {
		    Debug.Assert(rt != null);

		    int width = rt.rt.width;
		    int height = rt.rt.height;
		    int depth = rt.rt.volumeDepth;

		    bool realloc = (width < viewportWidth) || (height < viewportHeight) || (depth < viewportDepth);

		    if (realloc)
		    {
			    RTHandles.Release(rt);

			    width = Math.Max(width, viewportWidth);
			    height = Math.Max(height, viewportHeight);
			    depth = Math.Max(depth, viewportDepth);

			    rt = RTHandles.Alloc(width, height, depth, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, // 8888_sRGB is not precise enough
				    dimension: TextureDimension.Tex3D, enableRandomWrite: true, name: name);
		    }
	    }

	    unsafe static void UpdateShaderVariableslVolumetrics(ref ShaderVariablesVolumetric cb, PostProcessData hdCamera, in Vector4 resolution,
		    int maxSliceCount,
		    bool updateVoxelizationFields = false)
	    {
		    var fog = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();
		    var vFoV = hdCamera.camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
		    var gpuAspect = VolumetricNormalFunctions.ProjectionMatrixAspect(hdCamera.camera.projectionMatrix);
		    int frameIndex = (int)VolumetricFrameIndex(hdCamera);

		    hdCamera.GetPixelCoordToViewDirWS(resolution, gpuAspect);
		    for (int j = 0; j < 16; ++j)
			    cb._VBufferCoordToViewDirWS[j] = hdCamera.pixelCoordToViewDirWS[j];
		    cb._VBufferUnitDepthTexelSpacing = VolumetricNormalFunctions.ComputZPlaneTexelSpacing(1.0f, vFoV, resolution.y);
		    cb._NumVisibleLocalVolumetricFog = (uint)m_VisibleLocalVolumetricFogVolumes.Count;
		    cb._CornetteShanksConstant = CornetteShanksPhasePartConstant(fog.anisotropy.value);
		    cb._VBufferHistoryIsValid = hdCamera.IsVolumetricReprojectionEnabled() ? 1u : 0u;

		    GetHexagonalClosePackedSpheres7(m_xySeq);
		    int sampleIndex = frameIndex % 7;
		    Vector4 xySeqOffset = new Vector4();
		    // TODO: should we somehow reorder offsets in Z based on the offset in XY? S.t. the samples more evenly cover the domain.
		    // Currently, we assume that they are completely uncorrelated, but maybe we should correlate them somehow.
		    xySeqOffset.Set(m_xySeq[sampleIndex].x, m_xySeq[sampleIndex].y, m_zSeq[sampleIndex], frameIndex);
		    cb._VBufferSampleOffset = xySeqOffset;

		    var currIdx = (frameIndex + 0) & 1;
		    var prevIdx = (frameIndex + 1) & 1;

		    var currParams = hdCamera.vBufferParams[currIdx];
		    var prevParams = hdCamera.vBufferParams[prevIdx];

		    var pvp = prevParams.viewportSize;

		    Vector3Int historyBufferSize = Vector3Int.zero;

		    if (hdCamera.IsVolumetricReprojectionEnabled())
		    {
			    RTHandle historyRT = hdCamera.volumetricHistoryBuffers[prevIdx];
			    historyBufferSize = new Vector3Int(historyRT.rt.width, historyRT.rt.height, historyRT.rt.volumeDepth);
		    }

		    cb._VBufferVoxelSize = currParams.voxelSize;
		    cb._VBufferPrevViewportSize = new Vector4(pvp.x, pvp.y, 1.0f / pvp.x, 1.0f / pvp.y);
		    cb._VBufferHistoryViewportScale = prevParams.ComputeViewportScale(historyBufferSize);
		    cb._VBufferHistoryViewportLimit = prevParams.ComputeViewportLimit(historyBufferSize);
		    cb._VBufferPrevDistanceEncodingParams = prevParams.depthEncodingParams;
		    cb._VBufferPrevDistanceDecodingParams = prevParams.depthDecodingParams;
		    cb._NumTileBigTileX = 1;
		    cb._NumTileBigTileY = 1;

		    cb._MaxSliceCount = (uint)maxSliceCount;
		    cb._MaxVolumetricFogDistance = fog.depthExtent.value;
		    cb._VolumeCount = (uint)m_VisibleLocalVolumetricFogVolumes.Count;

		    if (updateVoxelizationFields)
		    {
			    bool obliqueMatrix = GeometryUtils.IsProjectionMatrixOblique(hdCamera.camera.projectionMatrix);
			    if (obliqueMatrix)
			    {
				    // Convert the non oblique projection matrix to its  GPU version
				    var gpuProjNonOblique = GL.GetGPUProjectionMatrix(GeometryUtils.CalculateProjectionMatrix(hdCamera.camera), true);
				    // Build the non oblique view projection matrix
				    var vpNonOblique = gpuProjNonOblique * hdCamera.mainViewConstants.viewMatrix;
				    cb._CameraInverseViewProjection_NO = vpNonOblique.inverse;
			    }

			    cb._IsObliqueProjectionMatrix = obliqueMatrix ? 1u : 0u;
			    cb._CameraRight = hdCamera.camera.transform.right;
		    }
	    }
	    
	    static void GetHexagonalClosePackedSpheres7(Vector2[] coords)
	    {
		    float r = 0.17054068870105443882f;
		    float d = 2 * r;
		    float s = r * Mathf.Sqrt(3);

		    // Try to keep the weighted average as close to the center (0.5) as possible.
		    //  (7)(5)    ( )( )    ( )( )    ( )( )    ( )( )    ( )(o)    ( )(x)    (o)(x)    (x)(x)
		    // (2)(1)(3) ( )(o)( ) (o)(x)( ) (x)(x)(o) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x)
		    //  (4)(6)    ( )( )    ( )( )    ( )( )    (o)( )    (x)( )    (x)(o)    (x)(x)    (x)(x)
		    coords[0] = new Vector2(0, 0);
		    coords[1] = new Vector2(-d, 0);
		    coords[2] = new Vector2(d, 0);
		    coords[3] = new Vector2(-r, -s);
		    coords[4] = new Vector2(r, s);
		    coords[5] = new Vector2(r, -s);
		    coords[6] = new Vector2(-r, s);

		    // Rotate the sampling pattern by 15 degrees.
		    const float cos15 = 0.96592582628906828675f;
		    const float sin15 = 0.25881904510252076235f;

		    for (int i = 0; i < 7; i++)
		    {
			    Vector2 coord = coords[i];

			    coords[i].x = coord.x * cos15 - coord.y * sin15;
			    coords[i].y = coord.x * sin15 + coord.y * cos15;
		    }
	    }
	    
	    static float CornetteShanksPhasePartConstant(float anisotropy)
	    {
		    float g = anisotropy;

		    return (3.0f / (8.0f * Mathf.PI)) * (1.0f - g * g) / (2.0f + g * g);
	    }
	    
	    
	    //将VBuffer数据递交GPU
	    void UpdateShaderVariablesGlobalVolumetrics(ref VolumetricGlobalParams cb, PostProcessData postProcessData)
	    {
		    if (!VolumetricFogHDRP.IsVolumetricFogEnabled(postProcessData.camera))
		    {
			    return;
		    }

		    var fog = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();
		    uint frameIndex = postProcessData.FrameCount;
		    uint currIdx = (frameIndex + 0) & 1;

		    var currParams = postProcessData.vBufferParams[currIdx];

		    // The lighting & density buffers are shared by all cameras.
		    // The history & feedback buffers are specific to the camera.
		    // These 2 types of buffers can have different sizes.
		    // Additionally, history buffers can have different sizes, since they are not resized at the same time.
		    var cvp = currParams.viewportSize;

		    // Adjust slices for XR rendering: VBuffer is shared for all single-pass views
		    uint sliceCount = (uint)(cvp.z);

		    cb._VBufferViewportSize = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
		    cb._VBufferSliceCount = sliceCount;
		    cb._FogGIDimmer = fog.globalLightProbeDimmer.value;
		    cb._VBufferRcpSliceCount = 1.0f / sliceCount;
		    cb._VBufferLightingViewportScale = currParams.ComputeViewportScale(s_CurrentVolumetricBufferSize);
		    cb._VBufferLightingViewportLimit = currParams.ComputeViewportLimit(s_CurrentVolumetricBufferSize);
		    cb._VBufferDistanceEncodingParams = currParams.depthEncodingParams;
		    cb._VBufferDistanceDecodingParams = currParams.depthDecodingParams;
		    cb._VBufferLastSliceDist = currParams.ComputeLastSliceDistance(sliceCount);
		    cb._VBufferRcpInstancedViewCount = 1.0f;
		    //Debug.Log(cb._VBufferDistanceEncodingParams);
	    }
	    
	    //写入场景里所有LocalVolumetricFog的信息
	    void PrepareVisibleLocalVolumetricFogList(PostProcessData hdCamera)
	    {
		    if (!VolumetricFogHDRP.IsVolumetricFogEnabled(postProcessData.camera))
		    {
			    return;
		    }
		    
		    Vector3 camPosition = hdCamera.camera.transform.position;
		    Vector3 camOffset = Vector3.zero;// todo:相对相机渲染

		    m_VisibleVolumeBounds.Clear();
		    m_VisibleVolumeData.Clear();
		    m_VisibleLocalVolumetricFogVolumes.Clear();
		    m_GlobalVolumeIndices.Clear();
		    
		    // Collect all visible finite volume data, and upload it to the GPU.
		    var volumes = LocalVolumetricFogManager.manager.PrepareLocalVolumetricFogData(hdCamera);
		    int maxLocalVolumetricFogOnScreen = 256;
		    
		    ulong cameraSceneCullingMask = VolumetricNormalFunctions.GetSceneCullingMaskFromCamera(hdCamera.camera);

		    foreach (var volume in volumes)
		    {
			    Vector3 center = volume.transform.position * 2;
			    
			    // Reject volumes that are completely fade out or outside of the volumetric fog using bounding sphere
			    float boundingSphereRadius = Vector3.Magnitude(volume.parameters.size * 2);
			    float minObbDistance = Vector3.Magnitude(center - camPosition) - hdCamera.camera.nearClipPlane - boundingSphereRadius;
			    if (minObbDistance > volume.parameters.distanceFadeEnd || minObbDistance > settings.depthExtent.value)
				    continue;
			    
#if UNITY_EDITOR
			    if ((volume.gameObject.sceneCullingMask & cameraSceneCullingMask) == 0)
				    continue;
#endif
			    // Handle camera-relative rendering.
			    center -= camOffset;
			    
			    var transform = volume.transform;
			    var bounds = GeometryUtils.OBBToAABB(transform.right, transform.up, transform.forward, volume.parameters.size * 2, center);

			    // Frustum cull on the CPU for now. TODO: do it on the GPU.
			    // TODO: account for custom near and far planes of the V-Buffer's frustum.
			    // It's typically much shorter (along the Z axis) than the camera's frustum.
			    // We use AABB instead of OBB because the culling has to match what is done on the C++ side

			    if (m_VisibleLocalVolumetricFogVolumes.Count >= maxLocalVolumetricFogOnScreen)
			    {
				    Debug.LogError($"The number of local volumetric fog in the view is above the limit: {m_VisibleLocalVolumetricFogVolumes.Count} instead of {maxLocalVolumetricFogOnScreen}. To fix this, please increase the maximum number of local volumetric fog in the view in the HDRP asset.");
				    break;
			    }
			    
			    // TODO: cache these?
			    var obb = new OrientedBBox(Matrix4x4.TRS(transform.position * 2 - camOffset, transform.rotation, volume.parameters.size * 200000));
			    m_VisibleVolumeBounds.Add(obb);
			    m_GlobalVolumeIndices.Add(volume.GetGlobalIndex());
			    var visibleData = volume.parameters.ConvertToEngineData();
			    m_VisibleVolumeData.Add(visibleData);

			    m_VisibleLocalVolumetricFogVolumes.Add(volume);
		    }
		    
		    for (int i = 0; i < m_VisibleLocalVolumetricFogVolumes.Count; i++)
			    m_VolumetricFogSortKeys[i] = PackFogVolumeSortKey(m_VisibleLocalVolumetricFogVolumes[i], i);

		    // Stable sort to avoid flickering
		    CoreUnsafeUtils.MergeSort(m_VolumetricFogSortKeys, m_VisibleLocalVolumetricFogVolumes.Count, ref m_VolumetricFogSortKeysTemp);

		    m_VisibleVolumeBoundsBuffer.SetData(m_VisibleVolumeBounds);
		    m_VisibleVolumeGlobalIndices.SetData(m_GlobalVolumeIndices);
	    }
	    
	    uint PackFogVolumeSortKey(LocalVolumetricFog fog, int index)
	    {
		    // 12 bit index, 20 bit priority
		    int halfMaxPriority = k_VolumetricFogPriorityMaxValue / 2;
		    int clampedPriority = Mathf.Clamp(fog.parameters.priority, -halfMaxPriority, halfMaxPriority) + halfMaxPriority;
		    uint priority = (uint)(clampedPriority & 0xFFFFF);
		    uint fogIndex = (uint)(index & 0xFFF);
		    return (priority << 12) | (fogIndex << 0);
	    }
    }
}