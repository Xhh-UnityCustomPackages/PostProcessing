using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    /// <summary>
    /// Debug mode for displaying intermediate render targets.
    /// </summary>
    [GenerateHLSL]
    public enum DebugFullScreenMode
    {
        None = 0,
        HiZ = 1
    }
}

