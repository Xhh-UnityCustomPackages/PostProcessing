using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public partial class PostProcessData
    {
        public RTHandle[] volumetricHistoryBuffers = new RTHandle[2];
        internal VBufferParameters[] vBufferParams;
        
        public Matrix4x4 pixelCoordToViewDirWS;
        public Vector3 prevPos = Vector3.zero;
        
        public bool volumetricHistoryIsValid = false;
        internal int volumetricValidFrames = 0;
        
        internal bool IsVolumetricReprojectionEnabled()
        {
            bool a = VolumetricFogHDRP.IsVolumetricFogEnabled(camera);
            // We only enable volumetric re projection if we are processing the game view or a scene view with animated materials on
            bool b = camera.cameraType == CameraType.Game || (camera.cameraType == CameraType.SceneView && CoreUtils.AreAnimatedMaterialsEnabled(camera));
            // bool c = frameSettings.IsEnabled(FrameSettingsField.ReprojectionForVolumetrics);
            bool d = VolumetricFogHDRP.IsVolumetricReprojectionEnabled();
            return a && b && d;
        }
        
        internal void GetPixelCoordToViewDirWS(Vector4 resolution, float aspect, ref Matrix4x4[] transforms)
        { 
            transforms[0] = ComputePixelCoordToWorldSpaceViewDirectionMatrix(resolution, aspect);
        }

        Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Vector4 resolution, float aspect = -1)
        {
            // Asymmetry is also possible from a user-provided projection, so we must check for it too.
            // Note however, that in case of physical camera, the lens shift term is the only source of
            // asymmetry, and this is accounted for in the optimized path below. Additionally, Unity C++ will
            // automatically disable physical camera when the projection is overridden by user.
            //useGenericMatrix |= HDUtils.IsProjectionMatrixAsymmetric(viewConstants.projMatrix) && !camera.usePhysicalProperties;
             

            float verticalFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            if (!camera.usePhysicalProperties)
            {
                verticalFoV = Mathf.Atan(-1.0f / mainViewConstants.projMatrix[1, 1]) * 2;
            }
            Vector2 lensShift = camera.GetGateFittedLensShift();

            return VolumetricNormalFunctions.ComputePixelCoordToWorldSpaceViewDirectionMatrix(verticalFoV, lensShift, resolution, mainViewConstants.viewMatrix, false, aspect, camera.orthographic);
        }
    }
}