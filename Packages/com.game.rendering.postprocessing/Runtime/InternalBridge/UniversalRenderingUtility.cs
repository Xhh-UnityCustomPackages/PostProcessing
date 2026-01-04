using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public static class UniversalRenderingUtility
    {
        private static FieldInfo m_NormalsTextureFieldInfo;
        
        public static RenderingMode GetRenderingMode(ScriptableRenderer renderer)
        {
            return ((UniversalRenderer)renderer).renderingModeRequested;
        }

        public static RTHandle GetDepthTexture(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            var depthBuffer = universalRenderer.m_DepthTexture;
            return depthBuffer;
        }
        
        /// <summary>
        /// Get UniversalRenderer normals texture.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetNormalTexture(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (m_NormalsTextureFieldInfo == null)
            {
                m_NormalsTextureFieldInfo = typeof(UniversalRenderer).GetField("m_NormalsTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (m_NormalsTextureFieldInfo!.GetValue(universalRenderer) is not RTHandle normalBuffer) 
                return null;
            return normalBuffer;
        }
    }
}