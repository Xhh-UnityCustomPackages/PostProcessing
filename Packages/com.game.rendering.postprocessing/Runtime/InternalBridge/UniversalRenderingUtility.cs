using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public static class UniversalRenderingUtility
    {
        private static FieldInfo m_CopyDepthModeFieldInfo;
        private static FieldInfo m_OpaqueColorFieldInfo;
        private static FieldInfo m_NormalsTextureFieldInfo;
        private static FieldInfo m_MotionVectorColorFieldInfo;
        
        public static RenderingMode GetRenderingMode(ScriptableRenderer renderer)
        {
            return ((UniversalRenderer)renderer).renderingModeRequested;
        }

        public static CopyDepthMode GetCopyDepthMode(ScriptableRenderer renderer)
        {
            if (renderer is not UniversalRenderer universalRenderer)
                return CopyDepthMode.AfterOpaques;
    
            m_CopyDepthModeFieldInfo ??= typeof(UniversalRenderer).GetField("m_CopyDepthMode", 
                BindingFlags.NonPublic | BindingFlags.Instance);
    
            return (CopyDepthMode)(m_CopyDepthModeFieldInfo?.GetValue(universalRenderer) ?? CopyDepthMode.AfterOpaques);
        }
        
        /// <summary>
        /// Get UniversalRenderer m_OpaqueColor texture.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetOpaqueTexture(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (m_OpaqueColorFieldInfo == null)
            {
                m_OpaqueColorFieldInfo = typeof(UniversalRenderer).GetField("m_OpaqueColor",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            return m_OpaqueColorFieldInfo!.GetValue(universalRenderer) as RTHandle;
        }

        public static RTHandle GetDepthTexture(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            var depthBuffer = universalRenderer.m_DepthTexture;
            return depthBuffer;
        }

        public static RTHandle[] GetGBuffer(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (universalRenderer.deferredLights == null) return null;
            return universalRenderer.deferredLights.GbufferAttachments;
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
        
        /// <summary>
        /// Get UniversalRenderer motion vector color.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetMotionVectorColor(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (m_MotionVectorColorFieldInfo == null)
            {
                m_MotionVectorColorFieldInfo = typeof(UniversalRenderer).GetField("m_MotionVectorColor", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (m_MotionVectorColorFieldInfo!.GetValue(universalRenderer) is not RTHandle motionVectorColor) 
                return null;
            return motionVectorColor;
        }

        /// <summary>
        /// Get UniversalRenderer motion vector depth.
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        public static RTHandle GetMotionVectorDepth(ScriptableRenderer sr)
        {
            if (sr is not UniversalRenderer universalRenderer) return null;
            if (m_MotionVectorColorFieldInfo == null)
            {
                m_MotionVectorColorFieldInfo = typeof(UniversalRenderer).GetField("m_MotionVectorDepth", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (m_MotionVectorColorFieldInfo!.GetValue(universalRenderer) is not RTHandle motionVectorDepth) 
                return null;
            return motionVectorDepth;
        }
    }
}