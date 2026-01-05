#if UNITY_EDITOR

using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    public class ScreenSpaceReflectionStripper : IRenderPipelineGraphicsSettingsStripper<ScreenSpaceGlobalIlluminationResources>
    {
        public bool active => true;

        public bool CanRemoveSettings(ScreenSpaceGlobalIlluminationResources resources)
        {
            bool canRemove = false;

            return canRemove;
        }
    }
}

#endif