#if UNITY_EDITOR

using Game.Core.PostProcessing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering
{
    public class ScreenSpaceReflectionStripper : PostProcessStripper<ScreenSpaceReflectionResources, ScreenSpaceReflectionRenderer> 
    {
    }
}

#endif