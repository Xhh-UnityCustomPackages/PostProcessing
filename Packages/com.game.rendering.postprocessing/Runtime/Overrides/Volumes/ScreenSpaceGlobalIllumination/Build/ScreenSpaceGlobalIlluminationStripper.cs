#if UNITY_EDITOR

using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    public class ScreenSpaceGlobalIlluminationStripper : IRenderPipelineGraphicsSettingsStripper<ScreenSpaceGlobalIlluminationResources>
    {
        public bool active => true;

        public bool CanRemoveSettings(ScreenSpaceGlobalIlluminationResources resources)
        {
            bool canRemove = false;

            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows || 
                EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
            {
                canRemove = false;
            }
            else
            {
                canRemove = true;
            }
            return canRemove;
        }
    }
}

#endif