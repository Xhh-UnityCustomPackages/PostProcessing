#if UNITY_EDITOR

using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    public class BloomStripper : IRenderPipelineGraphicsSettingsStripper<BloomResources>
    {
        public bool active => true;

        public bool CanRemoveSettings(BloomResources resources)
        {
            bool canRemove = false;

            return canRemove;
        }
    }
}
#endif