using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public static class MemberNameHelpers
    {
        public static ShaderTagId ShaderTagId([CallerMemberName] string name = null)
        {
            return new ShaderTagId(name);
        }

        public static int ShaderPropertyID([CallerMemberName] string name = null)
        {
            return Shader.PropertyToID(name);
        }

        public static string String([CallerMemberName] string name = null)
        {
            return name;
        }
    }
}