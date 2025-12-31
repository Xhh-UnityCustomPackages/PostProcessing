using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public class PerCameraVolumetricFogData
    {
        public RTHandle[] volumetricHistoryBuffers = new RTHandle[2];
        public uint frameCount = 0;
        // internal VBufferParameters[] vBufferParams;
        public Vector3 prevPos = Vector3.zero;
    }
}