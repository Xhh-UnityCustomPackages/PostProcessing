using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Game.Core.PostProcessing
{
    public partial class ContactShadowRenderer : PostProcessVolumeRenderer<ContactShadow>
    {
        public class ContactShadowFrameData : ContextItem
        {
            public TextureHandle occlusionFinalTexture;

            public override void Reset()
            {
                occlusionFinalTexture = TextureHandle.nullHandle;
            }
        }
    }
}
