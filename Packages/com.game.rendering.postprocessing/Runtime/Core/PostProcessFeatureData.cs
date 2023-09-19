using UnityEngine;
using System;

namespace Game.Core.PostProcessing
{
    [CreateAssetMenu(menuName = "Rendering/PostProcessFeatureData", fileName = "PostProcessFeatureData")]
    public class PostProcessFeatureData : ScriptableObject
    {
        [Serializable]
        public sealed class ShaderResources
        {
            public Shader screenSpaceOcclusionPS;
            public Shader volumetricLightPS;
            public Shader volumetricCloudPS;
            public Shader lightShaftPS;
            public Shader screenSpaceReflectionPS;
            public Shader ScreenSpaceGlobalIlluminationPS;
            public Shader atmosphericHeightFogPS;
        }

        [Serializable]
        public sealed class ComputeShaderResources
        {

        }

        [Serializable]
        public sealed class TextureResources
        {
            public Texture2D DitherTexture;
            public Texture3D WorlyNoise128RGBA;
            public Texture2D blueNoiseTex;
        }

        [Serializable]
        public sealed class MaterialResources
        {
            public Material UberPost;
            public Material BilateralBlur;
            public Material DualBlur;
        }

        public ShaderResources shaders;

        [Space(10)]
        public ComputeShaderResources computeShaders;

        [Space(10)]
        public TextureResources textures;

        [Space(10)]
        public MaterialResources materials;
    }
}
