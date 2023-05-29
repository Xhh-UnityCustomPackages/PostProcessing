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
            public Shader temporalAntialiasingLitePS;
        }

        [Serializable]
        public sealed class TextureResources
        {

        }

        public ShaderResources shaders;
        public TextureResources textures;
    }
}
