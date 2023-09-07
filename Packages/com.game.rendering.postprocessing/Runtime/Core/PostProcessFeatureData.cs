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
            //公共部分
            public Shader BilateralBlur;

            public Shader screenSpaceOcclusionPS;
            public Shader volumetricLightPS;
            public Shader volumetricCloudPS;
            public Shader lightShaftPS;
        }

        [Serializable]
        public sealed class TextureResources
        {
            public Texture2D DitherTexture;
            public Texture3D WorlyNoise128RGBA;
        }

        public ShaderResources shaders;
        public TextureResources textures;
    }
}
