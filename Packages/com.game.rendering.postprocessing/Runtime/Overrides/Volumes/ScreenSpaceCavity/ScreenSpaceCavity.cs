using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    //https://github.com/malyawka/URP-ScreenSpaceCavity
    [Serializable, VolumeComponentMenu("Post-processing Custom/(Screen Space Cavity)")]
    public class ScreenSpaceCavity : VolumeSetting
    {
        public ScreenSpaceCavity()
        {
            displayName = "(Screen Space Cavity)";
        }

        public enum CavityType
        {
            Both = 0,
            Curvature = 1,
            Cavity = 2
        }

        [Serializable]
        public sealed class CavityTypeParameter : VolumeParameter<CavityType>
        {
            public CavityTypeParameter(CavityType value, bool overrideState = false) : base(value, overrideState) { }
        }

        public CavityTypeParameter cavityType = new CavityTypeParameter(CavityType.Both);


        [Header("Curvature")]
        public ClampedFloatParameter curvatureScale = new ClampedFloatParameter(1.0f, 0f, 5f);
        public ClampedFloatParameter curvatureRidge = new ClampedFloatParameter(0.25f, 0f, 2f);
        public ClampedFloatParameter curvatureValley = new ClampedFloatParameter(0.25f, 0f, 2f);

        [Header("Cavity")]
        public ClampedFloatParameter cavityDistance = new ClampedFloatParameter(0.25f, 0f, 1f);
        public ClampedFloatParameter cavityAttenuation = new ClampedFloatParameter(0.015625f, 0f, 1f);
        public ClampedFloatParameter cavityRidge = new ClampedFloatParameter(1.25f, 0f, 2.5f);
        public ClampedFloatParameter cavityValley = new ClampedFloatParameter(1.25f, 0f, 2.5f);
        public ClampedIntParameter cavitySamples = new ClampedIntParameter(4, 1, 12);

        [Header("Debug")]
        public BoolParameter debug = new BoolParameter(false);

        public override bool IsActive() => curvatureScale.value > 0;
    }


    [PostProcess("ScreenSpaceCavity", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class ScreenSpaceCavityRenderer : PostProcessVolumeRenderer<ScreenSpaceCavity>
    {
        static class ShaderConstants
        {
            internal static readonly int CurvatureParamsID = Shader.PropertyToID("_CurvatureParams");
            internal static readonly int CavityParamsID = Shader.PropertyToID("_CavityParams");
            internal static readonly int CavityTexture = Shader.PropertyToID("_CavityTexture");
            internal static readonly int SourceSize = Shader.PropertyToID("_SourceSize");
            //  internal static readonly string ScreenSpaceCavity = "_SCREEN_SPACE_CAVITY";
        }

        private const string k_OrthographicCameraKeyword = "_ORTHOGRAPHIC";
        private const string k_TypeCurvatureKeyword = "_TYPE_CURVATURE";
        private const string k_TypeCavityKeyword = "_TYPE_CAVITY";

        private Material m_Material;
        private RTHandle m_CavityRT;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.ScreenSpaceCavityPS);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            var desc = cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.colorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.RHalf;
            RenderingUtils.ReAllocateIfNeeded(ref m_CavityRT, desc, FilterMode.Bilinear, name: "_ScreenSpaceCavityRT");
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            if (m_Material == null)
                return;

            SetupMaterials(ref renderingData, m_Material);

            Blit(cmd, source, destination, m_Material, 0);

        }


        private void SetupMaterials(ref RenderingData renderingData, Material material)
        {
            Vector4 curvatureParams = new Vector4(
                settings.curvatureScale.value,
                settings.cavitySamples.value,
                0.5f / Mathf.Max(Mathf.Sqrt(settings.curvatureRidge.value), 1e-4f),
                0.7f / Mathf.Max(Mathf.Sqrt(settings.curvatureValley.value), 1e-4f));
            material.SetVector(ShaderConstants.CurvatureParamsID, curvatureParams);

            Vector4 cavityParams = new Vector4(
                settings.cavityDistance.value,
                settings.cavityAttenuation.value,
                settings.cavityRidge.value,
                settings.cavityValley.value);
            material.SetVector(ShaderConstants.CavityParamsID, cavityParams);

            CoreUtils.SetKeyword(material, k_OrthographicCameraKeyword, renderingData.cameraData.camera.orthographic);

            switch (settings.cavityType.value)
            {
                case ScreenSpaceCavity.CavityType.Both:
                    CoreUtils.SetKeyword(material, k_TypeCurvatureKeyword, true);
                    CoreUtils.SetKeyword(material, k_TypeCavityKeyword, true);
                    break;
                case ScreenSpaceCavity.CavityType.Curvature:
                    CoreUtils.SetKeyword(material, k_TypeCurvatureKeyword, true);
                    CoreUtils.SetKeyword(material, k_TypeCavityKeyword, false);
                    break;
                case ScreenSpaceCavity.CavityType.Cavity:
                    CoreUtils.SetKeyword(material, k_TypeCurvatureKeyword, false);
                    CoreUtils.SetKeyword(material, k_TypeCavityKeyword, true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
        }
    }
}
