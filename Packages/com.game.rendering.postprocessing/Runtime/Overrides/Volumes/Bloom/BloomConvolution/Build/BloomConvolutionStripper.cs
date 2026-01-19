#if UNITY_EDITOR

using Game.Core.PostProcessing;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    public class BloomConvolutionStripper : PostProcessStripper<BloomConvolutionResources, BloomConvolutionRenderer>
    {
    }
}
#endif