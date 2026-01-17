using System;

namespace Game.Core.PostProcessing
{
    [Serializable]
    public class StencilDebugSettings
    {
        public bool enableStencilDebug { get; set; } = false;
        public float stencilDebugScale { get; set; } = 10;
        public float stencilDebugMargin { get; set; } = 0.25f;
    }
}