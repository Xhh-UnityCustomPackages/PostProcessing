using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public static class GraphicsUtility
    {
        static Mesh mesh
        {
            get
            {
                if (m_mesh != null)
                    return m_mesh;
                m_mesh = new Mesh();
                m_mesh.vertices = new Vector3[]
                {
                    new Vector3(-1,-1,0.5f),
                    new Vector3(-1,1,0.5f),
                    new Vector3(1,1,0.5f),
                    new Vector3(1,-1,0.5f)
                };
                m_mesh.uv = new Vector2[]
                {
                    new Vector2(0,1),
                    new Vector2(0,0),
                    new Vector2(1,0),
                    new Vector2(1,1)
                };

                m_mesh.SetIndices(new int[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);
                return m_mesh;
            }
        }

        static Mesh m_mesh;


        public static void BlitMRT(this CommandBuffer buffer, RenderTargetIdentifier[] colorIdentifier, RenderTargetIdentifier depthIdentifier, Material mat, int pass)
        {
            buffer.SetRenderTarget(colorIdentifier, depthIdentifier);
            buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
        }
        
        public static void ValidateComputeBuffer(ref ComputeBuffer cb, int size, int stride, ComputeBufferType type = ComputeBufferType.Default)
        {
            if (cb == null || cb.count < size)
            {
                CoreUtils.SafeRelease(cb);
                cb = new ComputeBuffer(size, stride, type);
            }
        }
        
        internal static Vector4 GetMouseCoordinates(ref CameraData camera)
        {
            // We request the mouse post based on the type of the camera
            Vector2 mousePixelCoord = MousePositionDebug.instance.GetMousePosition(camera.pixelHeight, camera.cameraType == CameraType.SceneView);
            return new Vector4(mousePixelCoord.x, mousePixelCoord.y, RTHandles.rtHandleProperties.rtHandleScale.x * mousePixelCoord.x / camera.pixelWidth, RTHandles.rtHandleProperties.rtHandleScale.y * mousePixelCoord.y / camera.pixelHeight);
        }
    }
}
