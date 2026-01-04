// using UnityEngine;
// using UnityEngine.Experimental.Rendering;
// using System;
// using UnityEngine.Rendering;
//
// namespace Game.Core.PostProcessing
// {
//     public class VolumetricCameraParams
//     {
//         public float time;
//         public Camera camera;
//         public Frustum frustum;
//         public Matrix4x4 projMatrix;
//         public Matrix4x4 gpuPMatrix;
//         public Matrix4x4 invViewProjMatrix;
//         public Matrix4x4 viewMatrix;
//         public Vector4 screenSize;
//         public Matrix4x4 pixelCoordToViewDirWS;
//     
//         public Vector3 prevCameraPos;
//         public Matrix4x4 prevCameraVP;
//
//
//         public bool volumetricHistoryIsValid = false;
//         internal int volumetricValidFrames = 0;
//         public int m_NumVolumetricBuffersAllocated = 0;
//
//         internal uint cameraFrameCount = 0;
//         internal uint viewcount;
//
//         internal Rect prevFinalViewport;
//         internal Rect finalViewport = new Rect(Vector2.zero, -1.0f * Vector2.one);
//
//         public int actualWidth { get; private set; }
//         public int actualHeight { get; private set; }
//         internal int viewCount { get => Math.Max(1, xr.viewCount); }
//
//         internal VBufferParameters[] vBufferParams;
//         public RTHandle[] volumetricHistoryBuffers;
//
//
//         internal XRPass xr { get; private set; }
//         public void Reset()
//         { 
//             cameraFrameCount = 0;
//             m_NumVolumetricBuffersAllocated = 0;
//             volumetricValidFrames = 0;
//             frustum = new Frustum();
//             frustum.planes = new Plane[6];
//             frustum.corners = new Vector3[8];
//         }
//
//         internal bool EnableVolumetric()
//         { 
//             bool a = Fog.IsVolumetricFogEnabled(this);
//             return a;
//         }
//
//         internal bool IsVolumetricReprojectionEnabled()
//         {
//             bool a = Fog.IsVolumetricFogEnabled(this);
//             // We only enable volumetric re projection if we are processing the game view or a scene view with animated materials on
//             bool b = camera.cameraType == CameraType.Game || (camera.cameraType == CameraType.SceneView && CoreUtils.AreAnimatedMaterialsEnabled(camera));
//             
//             bool d = Fog.IsVolumetricReprojectionEnabled();
//             return a && b && d;
//         }
//
//
//         internal uint GetCameraFrameCount()
//         {
//             return cameraFrameCount;
//         }
//
//         public void UpdateRefreshCamera(Camera nowCam)
//         {
//             camera = nowCam;
//             projMatrix = camera.projectionMatrix;
//             viewMatrix = camera.worldToCameraMatrix;
//             //处理opengl与dx平台坐标
//             gpuPMatrix = GL.GetGPUProjectionMatrix(projMatrix, true);
//             Matrix4x4 gpuVPMatrix = gpuPMatrix * viewMatrix;
//             invViewProjMatrix = gpuVPMatrix.inverse;
//             if (cameraFrameCount == 0)
//             {
//                 prevCameraPos = camera.transform.position;
//                 prevCameraVP = gpuVPMatrix;
//             }
//
//             VolumetricFogRenderFeature.perCameraDatas[VolumetricFogRenderFeature.nowCameraIndex].frameCount++;
//         }
//
//         public void UpdatePrevVolumetricCameraPos(Camera cam)
//         {
//             if (cameraFrameCount == 0)
//             {
//                 return;
//             }
//
//             else
//             {
//                 prevCameraPos = camera.transform.position;
//                 prevCameraVP = gpuPMatrix * viewMatrix;
//             }
//         }
//
//
//         internal void GetPixelCoordToViewDirWS(Vector4 resolution, float aspect, ref Matrix4x4[] transforms)
//         { 
//             transforms[0] = ComputePixelCoordToWorldSpaceViewDirectionMatrix(resolution, aspect);
//         }
//
//         Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Vector4 resolution, float aspect = -1)
//         {
//             // In XR mode, or if explicitely required, use a more generic matrix to account for asymmetry in the projection
//             var useGenericMatrix = xr.enabled;
//
//             // Asymmetry is also possible from a user-provided projection, so we must check for it too.
//             // Note however, that in case of physical camera, the lens shift term is the only source of
//             // asymmetry, and this is accounted for in the optimized path below. Additionally, Unity C++ will
//             // automatically disable physical camera when the projection is overridden by user.
//             //useGenericMatrix |= HDUtils.IsProjectionMatrixAsymmetric(viewConstants.projMatrix) && !camera.usePhysicalProperties;
//
//             if (useGenericMatrix)
//             {
//                 var viewSpaceRasterTransform = new Matrix4x4(
//                     new Vector4(2.0f * resolution.z, 0.0f, 0.0f, -1.0f),
//                     new Vector4(0.0f, -2.0f * resolution.w, 0.0f, 1.0f),
//                     new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
//                     new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
//
//                 var transformT = invViewProjMatrix.transpose * Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f));
//                 return viewSpaceRasterTransform * transformT;
//             }
//
//             float verticalFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
//             if (!camera.usePhysicalProperties)
//             {
//                 verticalFoV = Mathf.Atan(-1.0f / projMatrix[1, 1]) * 2;
//             }
//             Vector2 lensShift = camera.GetGateFittedLensShift();
//
//             return VolumetricNormalFunctions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(verticalFoV, lensShift, resolution, viewMatrix, false, aspect, camera.orthographic);
//         }
//
//         internal void Update(XRPass xrpass)
//         {
//             xr = xrpass;
//
//             bool isVolumetricHistoryRequired = IsVolumetricReprojectionEnabled();
//
//             float newTime, deltaTime;
// #if UNITY_EDITOR
//             newTime = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
//             deltaTime = Application.isPlaying ? Time.deltaTime : 0.033f;
// #else
//             newTime = Time.time;
//             deltaTime = Time.deltaTime;
// #endif
//             time = newTime;
//
//
//             
//
//             //Debug.Log(camera.name + cameraFrameCount);
//
//             {
//                 prevFinalViewport = finalViewport;
//
//                 if (xr.enabled)
//                 {
//                     finalViewport = xr.GetViewport();
//                 }
//                 else
//                 {
//                     finalViewport = GetPixelRect();
//                 }
//
//                 actualWidth = Math.Max((int)finalViewport.size.x, 1);
//                 actualHeight = Math.Max((int)finalViewport.size.y, 1);
//
//                 if (camera.cameraType == CameraType.Game)
//                 {
//                     Vector2Int orignalSize = new Vector2Int(actualWidth, actualHeight);
//                     Vector2Int scaledSize = DynamicResolutionHandler.instance.GetScaledSize(orignalSize);
//                     actualWidth = scaledSize.x;
//                     actualHeight = scaledSize.y;
//                 }
//                 var screenWidth = actualWidth;
//                 var screenHeight = actualHeight;
//                 screenSize = new Vector4(screenWidth, screenHeight, 1.0f / screenWidth, 1.0f / screenHeight);
//                 var gpuprojAspect = VolumetricNormalFunctions.ProjectionMatrixAspect(gpuPMatrix);
//                 pixelCoordToViewDirWS = ComputePixelCoordToWorldSpaceViewDirectionMatrix(screenSize, gpuprojAspect);
//             }
//
// /*            int numVolumetricBuffersRequired = isVolumetricHistoryRequired ? 2 : 0;
//             if (m_NumVolumetricBuffersAllocated != numVolumetricBuffersRequired)
//             {
//                 VolumetricFogRenderFeature.DestroyVolumetricHistoryBuffers(this);
//                 if (numVolumetricBuffersRequired != 0)
//                 {
//                     Debug.Log(camera.name + "创建当前History");
//                     VolumetricFogRenderFeature.CreateVolumetricHistoryBuffers(this, cameraData ,numVolumetricBuffersRequired);
//                 }
//                     
//                 // Mark as init.
//                 m_NumVolumetricBuffersAllocated = numVolumetricBuffersRequired;
//             }*/
//         }
//
//         Rect? m_OverridePixelRect = null;
//
//         Rect GetPixelRect()
//         {
//             if (m_OverridePixelRect != null)
//                 return m_OverridePixelRect.Value;
//             else
//                 return new Rect(camera.pixelRect.x, camera.pixelRect.y, camera.pixelWidth, camera.pixelHeight);
//         }
//         
//     }
// }
//
