using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public class PostProcessingDebugDisplaySettings : DebugDisplaySettings<PostProcessingDebugDisplaySettings>
    {
        public DebugDisplaySettingsPostProcessing postProcessingSettings { get; private set; }
        
        public PostProcessingDebugDisplaySettings()
        {
            Reset();
        }

        public override void Reset()
        {
            base.Reset();
            
            postProcessingSettings = Add(new DebugDisplaySettingsPostProcessing());
        }
    }
}