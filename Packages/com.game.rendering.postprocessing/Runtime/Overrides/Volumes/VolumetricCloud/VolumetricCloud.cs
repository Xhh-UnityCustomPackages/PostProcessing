using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/Volumetric Cloud")]
    public class VolumetricCloud : VolumeSetting
    {
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);

        public Vector3Parameter boundsMin = new Vector3Parameter(new Vector3(-1, -1, -1));
        public Vector3Parameter boundsMax = new Vector3Parameter(new Vector3(1, 1, 1));
        public MinFloatParameter step = new MinFloatParameter(0.1f, 0.05f);

        public override bool IsActive() => intensity.value > 0;
    }

    [PostProcess("Volumetric Cloud", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class VolumetricCloudRenderer : PostProcessVolumeRenderer<VolumetricCloud>
    {
        static class ShaderConstants
        {
            internal static readonly int BoundsMin = Shader.PropertyToID("_BoundsMin");
            internal static readonly int BoundsMax = Shader.PropertyToID("_BoundsMax");
            internal static readonly int Step = Shader.PropertyToID("_Step");
        }


        Material m_Material;

        public override void Setup()
        {
            m_Material = GetMaterial(postProcessFeatureData.shaders.volumetricCloudPS);
        }

        private void SetupMaterials(ref RenderingData renderingData)
        {
            m_Material.SetVector(ShaderConstants.BoundsMin, settings.boundsMin.value);
            m_Material.SetVector(ShaderConstants.BoundsMax, settings.boundsMax.value);
            m_Material.SetFloat(ShaderConstants.Step, settings.step.value);
        }


        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(ref renderingData);


            Blit(cmd, source, target, m_Material, 0);
        }
    }
}
