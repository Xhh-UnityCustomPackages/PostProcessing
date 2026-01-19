#if UNITY_EDITOR
using Game.Core.PostProcessing;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    public abstract class PostProcessStripper<T, E> : IRenderPipelineGraphicsSettingsStripper<T>  
        where T : IRenderPipelineGraphicsSettings 
        where E : PostProcessRenderer
    {
        public virtual bool active => true;

        public virtual bool CanRemoveSettings(T resources)
        {
            bool canRemove = false;
            canRemove |= !PostProcessingUtils.HasPostProcessRenderer<E>();
            return canRemove;
        }
    }
}
#endif