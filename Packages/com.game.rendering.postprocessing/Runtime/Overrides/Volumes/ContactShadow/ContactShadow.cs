using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        public override bool IsActive() => enable.value;


        public int sampleCount
        {
            get
            {
                return m_SampleCount.value;
            }
            set { m_SampleCount.value = value; }
        }

        [SerializeField]
        private NoInterpClampedIntParameter m_SampleCount = new NoInterpClampedIntParameter(10, 4, 64);
    }


    [PostProcess("ContactShadow", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class ContactShadowRenderer : PostProcessVolumeRenderer<ContactShadow>
    {
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

            public RTHandle depthTexture;
            public RTHandle contactShadowsTexture;
        }

        RenderContactShadowPassData m_PassData = new RenderContactShadowPassData();

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            bool msaa = renderingData.cameraData.camera.allowMSAA;

        }


        void RenderContractShadows(int firstMipOffsetY)
        {
            float contactShadowRange = Mathf.Clamp(settings.fadeDistance.value, 0.0f, settings.maxDistance.value);
            float contactShadowFadeEnd = settings.maxDistance.value;
            float contactShadowOneOverFadeRange = 1.0f / Math.Max(1e-6f, contactShadowRange);

            float contactShadowMinDist = Mathf.Min(settings.minDistance.value, contactShadowFadeEnd);
            float contactShadowFadeIn = Mathf.Clamp(settings.fadeInDistance.value, 1e-6f, contactShadowFadeEnd);

            m_PassData.params1 = new Vector4(settings.length.value, settings.distanceScaleFactor.value, contactShadowFadeEnd, contactShadowOneOverFadeRange);
            m_PassData.params2 = new Vector4(firstMipOffsetY, contactShadowMinDist, contactShadowFadeIn, settings.rayBias.value * 0.01f);
            m_PassData.params3 = new Vector4(settings.sampleCount, settings.thicknessScale.value * 10.0f, 0.0f, 0.0f);
        }
    }
}
