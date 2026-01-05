#if UNITY_EDITOR
using UnityEngine.Rendering;


namespace UnityEditor.Rendering
{
    public class ExposureResourceStripper : IRenderPipelineGraphicsSettingsStripper<ExposureResources>
    {
        public bool active => true;

        public bool CanRemoveSettings(ExposureResources resources)
        {
            bool canRemove = false;

            return canRemove;
        }
    }
}

#endif