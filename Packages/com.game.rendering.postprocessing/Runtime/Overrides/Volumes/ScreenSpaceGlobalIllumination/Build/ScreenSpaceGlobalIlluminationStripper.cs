#if UNITY_EDITOR

using Game.Core.PostProcessing;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    public class ScreenSpaceGlobalIlluminationStripper : PostProcessStripper<ScreenSpaceGlobalIlluminationResources, ScreenSpaceGlobalIlluminationRenderer>
    {
        public override bool CanRemoveSettings(ScreenSpaceGlobalIlluminationResources resources)
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

            canRemove |= !PostProcessingUtils.HasPostProcessRenderer<ScreenSpaceGlobalIlluminationRenderer>();
            return canRemove;
        }
    }
}

#endif