using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/接触阴影 (Contact Shadow)")]
    public class ContactShadow : VolumeSetting
    {
        public ContactShadow()
        {
            displayName = "接触阴影 (Contact Shadow)";
        }

        /// <summary>
        /// When enabled, HDRP processes Contact Shadows for this Volume.
        /// </summary>
        public BoolParameter enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);
        /// <summary>
        /// Controls the length of the rays HDRP uses to calculate Contact Shadows. It is in meters, but it gets scaled by a factor depending on Distance Scale Factor
        /// and the depth of the point from where the contact shadow ray is traced.
        /// </summary>
        public ClampedFloatParameter length = new ClampedFloatParameter(0.15f, 0.0f, 1.0f);
        /// <summary>
        /// Controls the opacity of the contact shadows.
        /// </summary>
        public ClampedFloatParameter opacity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        /// <summary>
        /// Scales the length of the contact shadow ray based on the linear depth value at the origin of the ray.
        /// </summary>
        public ClampedFloatParameter distanceScaleFactor = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        /// <summary>
        /// The distance from the camera, in meters, at which HDRP begins to fade out Contact Shadows.
        /// </summary>
        public MinFloatParameter maxDistance = new MinFloatParameter(50.0f, 0.0f);
        /// <summary>
        /// The distance from the camera, in meters, at which HDRP begins to fade in Contact Shadows.
        /// </summary>
        public MinFloatParameter minDistance = new MinFloatParameter(0.0f, 0.0f);
        /// <summary>
        /// The distance, in meters, over which HDRP fades Contact Shadows out when past the Max Distance.
        /// </summary>
        public MinFloatParameter fadeDistance = new MinFloatParameter(5.0f, 0.0f);
        /// <summary>
        /// The distance, in meters, over which HDRP fades Contact Shadows in when past the Min Distance.
        /// </summary>
        public MinFloatParameter fadeInDistance = new MinFloatParameter(0.0f, 0.0f);
        /// <summary>
        /// Controls the bias applied to the screen space ray cast to get contact shadows.
        /// </summary>
        public ClampedFloatParameter rayBias = new ClampedFloatParameter(0.2f, 0.0f, 1.0f);
        /// <summary>
        /// Controls the thickness of the objects found along the ray, essentially thickening the contact shadows.
        /// </summary>
        public ClampedFloatParameter thicknessScale = new ClampedFloatParameter(0.15f, 0.02f, 1.0f);
        public ClampedIntParameter sampleCount = new ClampedIntParameter(8, 8, 64);

        public override bool IsActive() => enable.value;



    }

    //https://github.com/himma-bit/empty/blob/main/Assets/Scripts/ContactShadow/ContactShadowMapGenerater.cs
    [PostProcess("ContactShadow", PostProcessInjectionPoint.BeforeRenderingGBuffer)]
    public class ContactShadowRenderer : PostProcessVolumeRenderer<ContactShadow>
    {
        static class ShaderConstants
        {
            public static readonly int ParametersID = Shader.PropertyToID("_ContactShadowParamsParameters");
            public static readonly int Parameters2ID = Shader.PropertyToID("_ContactShadowParamsParameters2");
            public static readonly int Parameters3ID = Shader.PropertyToID("_ContactShadowParamsParameters3");
            public static readonly int TextureUAVID = Shader.PropertyToID("_ContactShadowTextureUAV");
        }

        class RenderContactShadowPassData
        {
            public ComputeShader contactShadowsCS;
            public int kernel;

            public Vector4 params1;
            public Vector4 params2;
            public Vector4 params3;

            public int numTilesX;
            public int numTilesY;
            public int viewCount;

            // public RTHandle depthTexture;
            public RTHandle contactShadowsTexture;
        }

        RenderContactShadowPassData m_PassData = new();
        int m_DeferredContactShadowKernel = -1;


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;
            desc.useMipMap = false;

            RenderingUtils.ReAllocateIfNeeded(ref m_PassData.contactShadowsTexture, desc);

            Shader.SetGlobalTexture("_ContactShadowMap", m_PassData.contactShadowsTexture);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            RenderContractShadows();
            var computeShader = postProcessFeatureData.computeShaders.contractShadowCS;
            if (m_DeferredContactShadowKernel == -1)
            {
                m_DeferredContactShadowKernel = computeShader.FindKernel("ContactShadowMap");
            }

            cmd.SetComputeVectorParam(computeShader, ShaderConstants.ParametersID, m_PassData.params1);
            cmd.SetComputeVectorParam(computeShader, ShaderConstants.Parameters2ID, m_PassData.params2);
            cmd.SetComputeVectorParam(computeShader, ShaderConstants.Parameters3ID, m_PassData.params3);
            cmd.SetComputeTextureParam(computeShader, m_DeferredContactShadowKernel, ShaderConstants.TextureUAVID, m_PassData.contactShadowsTexture);

            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;
            cmd.DispatchCompute(computeShader, m_DeferredContactShadowKernel, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);


            // cmd.Blit(m_PassData.contactShadowsTexture, destination);
        }


        void RenderContractShadows()
        {
            float contactShadowRange = Mathf.Clamp(settings.fadeDistance.value, 0.0f, settings.maxDistance.value);
            float contactShadowFadeEnd = settings.maxDistance.value;
            float contactShadowOneOverFadeRange = 1.0f / Math.Max(1e-6f, contactShadowRange);

            float contactShadowMinDist = Mathf.Min(settings.minDistance.value, contactShadowFadeEnd);
            float contactShadowFadeIn = Mathf.Clamp(settings.fadeInDistance.value, 1e-6f, contactShadowFadeEnd);

            m_PassData.params1 = new Vector4(settings.length.value, settings.distanceScaleFactor.value, contactShadowFadeEnd, contactShadowOneOverFadeRange);
            m_PassData.params2 = new Vector4(0, contactShadowMinDist, contactShadowFadeIn, settings.rayBias.value * 0.01f);
            m_PassData.params3 = new Vector4(settings.sampleCount.value, settings.thicknessScale.value * 10.0f, 0.0f, 0.0f);


        }
    }
}
