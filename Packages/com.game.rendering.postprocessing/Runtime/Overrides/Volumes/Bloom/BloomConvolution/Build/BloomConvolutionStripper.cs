#if UNITY_EDITOR

using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    public class BloomConvolutionStripper : IRenderPipelineGraphicsSettingsStripper<BloomConvolutionResources>
    {
        public bool active => true;

        public bool CanRemoveSettings(BloomConvolutionResources resources)
        {
            bool canRemove = false;
            return canRemove;
        }
    }
}
#endif