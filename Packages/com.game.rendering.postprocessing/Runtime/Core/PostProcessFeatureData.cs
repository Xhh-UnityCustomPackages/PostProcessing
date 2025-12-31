using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
#endif

namespace Game.Core.PostProcessing
{
    public class PostProcessFeatureData : ScriptableObject
    {
        [Space(10)] 
        public ComputeShaderResources computeShaders;

        [Space(10)] 
        public MaterialResources materials;
        public ShaderResources shaders;

        [Space(10)] 
        public TextureResources textures;


        [Serializable]
        public sealed class ShaderResources
        {
            public Shader screenSpaceOcclusionPS;
            public Shader volumetricLightPS;
            public Shader volumetricCloudPS;
            public Shader lightShaftPS;
            public Shader screenSpaceReflectionPS;
            public Shader stochasticScreenSpaceReflectionPS;
            public Shader screenSpaceRaytracedReflectionPS;
            public Shader atmosphericHeightFogPS;
            public Shader ScreenSpaceCavityPS;
            public Shader CRTScreenPS;
            public Shader MoebiusPS;
            public Shader EightColorPS;
            public Shader bloomPS;
            [Space(5)]
            [Header("ConvolutionBloom")]
            public Shader ConvolutionBloomBrightMask;
            public Shader ConvolutionBloomBlend;
            public Shader ConvolutionBloomPsfRemap;
            public Shader ConvolutionBloomPsfGenerator;
        }

        [Serializable]
        public sealed class ComputeShaderResources
        {
            [Space(5)]
            public ComputeShader pyramidDepthGeneratorCS;
            [Space(5)]
            [Header("ConvolutionBloom")]
            public ComputeShader fastFourierTransformCS;
            public ComputeShader fastFourierConvolveCS;
        }

        [Serializable]
        [ReloadGroup]
        public sealed class TextureResources
        {
            public Texture2D DitherTexture;
            public Texture3D WorlyNoise128RGBA;
            public Texture2D blueNoiseTex;

            [Reload("Textures/BlueNoise16/RGB/LDR_RGB1_{0}.png", 0, 32)]
            public Texture2D[] blueNoise16RGBTex;
        }

        [Serializable]
        public sealed class MaterialResources
        {
            public Material UberPost;
            public Material BilateralBlur;
            public Material DualBlur;
        }

#if UNITY_EDITOR
        [SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreatePostProcessDataAsset : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var instance = CreateInstance<PostProcessFeatureData>();
                AssetDatabase.CreateAsset(instance, pathName);
                ResourceReloader.ReloadAllNullIn(instance, PostProcessingUtils.packagePath); //这一行可以强制执行Reload
                Selection.activeObject = instance;
            }
        }

        [MenuItem("Assets/Create/Rendering/Custom Post-process Data", priority = CoreUtils.Sections.section5 + CoreUtils.Priorities.assetsCreateRenderingMenuPriority)]
        private static void CreatePostProcessData()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreatePostProcessDataAsset>(), "PostProcessFeatureData.asset", null, null);
        }
#endif
    }
}