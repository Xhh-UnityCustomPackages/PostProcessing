#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public class PostProcessFeatureData : ScriptableObject
    {

#if UNITY_EDITOR
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreatePostProcessDataAsset : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var instance = CreateInstance<PostProcessFeatureData>();
                AssetDatabase.CreateAsset(instance, pathName);
                ResourceReloader.ReloadAllNullIn(instance, PostProcessingUtils.packagePath);//这一行可以强制执行Reload
                Selection.activeObject = instance;
            }
        }

        [MenuItem("Assets/Create/Rendering/Custom Post-process Data", priority = CoreUtils.Sections.section5 + CoreUtils.Priorities.assetsCreateRenderingMenuPriority)]
        static void CreatePostProcessData()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreatePostProcessDataAsset>(), "PostProcessFeatureData.asset", null, null);
        }
#endif


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
            public Shader ScreenSpaceGlobalIlluminationPS;
            public Shader atmosphericHeightFogPS;
            public Shader ScreenSpaceCavityPS;
            public Shader CRTScreenPS;
            public Shader MoebiusPS;
        }

        [Serializable]
        public sealed class ComputeShaderResources
        {
            public ComputeShader autoExposureCS;
            public ComputeShader LogHistogramCS;
            public ComputeShader pyramidDepthGeneratorCS;
            public ComputeShader contractShadowCS;
        }

        [Serializable, ReloadGroup]
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

        public ShaderResources shaders;

        [Space(10)]
        public ComputeShaderResources computeShaders;

        [Space(10)]
        public TextureResources textures;

        [Space(10)]
        public MaterialResources materials;
    }
}
