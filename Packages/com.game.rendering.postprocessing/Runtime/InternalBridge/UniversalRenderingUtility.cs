using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public static class UniversalRenderingUtility
    {
        public static RenderingMode GetRenderingMode(ScriptableRenderer renderer)
        {
            return ((UniversalRenderer)renderer).renderingModeRequested;
        }
    }
}