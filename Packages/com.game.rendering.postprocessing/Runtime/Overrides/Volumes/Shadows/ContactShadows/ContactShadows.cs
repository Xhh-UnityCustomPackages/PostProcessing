using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/接触阴影 (Contact Shadows)")]
    public class ContactShadows : VolumeSetting
    {
        public ContactShadows()
        {
            displayName = "接触阴影 (Contact Shadows)";
        }

        /// <summary>
        /// When enabled, HDRP processes Contact Shadows for this Volume.
        /// </summary>
        public BoolParameter enable = new (false, BoolParameter.DisplayType.EnumPopup);
        /// <summary>
        /// Controls the length of the rays HDRP uses to calculate Contact Shadows. It is in meters, but it gets scaled by a factor depending on Distance Scale Factor
        /// and the depth of the point from where the contact shadow ray is traced.
        /// </summary>
        public ClampedFloatParameter length = new (0.15f, 0.0f, 1.0f);
        /// <summary>
        /// Controls the opacity of the contact shadows.
        /// </summary>
        public ClampedFloatParameter opacity = new (1.0f, 0.0f, 1.0f);
        /// <summary>
        /// Scales the length of the contact shadow ray based on the linear depth value at the origin of the ray.
        /// </summary>
        public ClampedFloatParameter distanceScaleFactor = new (0.5f, 0.0f, 1.0f);
        /// <summary>
        /// The distance from the camera, in meters, at which HDRP begins to fade out Contact Shadows.
        /// </summary>
        public MinFloatParameter maxDistance = new (50.0f, 0.0f);
        /// <summary>
        /// The distance from the camera, in meters, at which HDRP begins to fade in Contact Shadows.
        /// </summary>
        public MinFloatParameter minDistance = new (0.0f, 0.0f);
        /// <summary>
        /// The distance, in meters, over which HDRP fades Contact Shadows out when past the Max Distance.
        /// </summary>
        public MinFloatParameter fadeDistance = new (5.0f, 0.0f);
        /// <summary>
        /// The distance, in meters, over which HDRP fades Contact Shadows in when past the Min Distance.
        /// </summary>
        public MinFloatParameter fadeInDistance = new (0.0f, 0.0f);
        /// <summary>
        /// Controls the bias applied to the screen space ray cast to get contact shadows.
        /// </summary>
        public ClampedFloatParameter rayBias = new (0.2f, 0.0f, 1.0f);
        /// <summary>
        /// Controls the thickness of the objects found along the ray, essentially thickening the contact shadows.
        /// </summary>
        public ClampedFloatParameter thicknessScale = new (0.15f, 0.02f, 1.0f);
        
        public EnumParameter<ShadowDenoiser> shadowDenoiser = new(ShadowDenoiser.None);
        
        /// <summary>
        /// Controls the numbers of samples taken during the ray-marching process for shadows. Increasing this might lead to higher quality at the expenses of performance.
        /// </summary>
        [Tooltip("Controls the numbers of samples taken during the ray-marching process for shadows. Increasing this might lead to higher quality at the expenses of performance.")]
        public ClampedIntParameter sampleCount = new(10, 4, 64);
        
        /// <summary>
        /// Control the size of the filter used for ray traced shadows
        /// </summary>
        [Tooltip("Control the size of the filter used for ray traced shadows")]
        public ClampedIntParameter filterSizeTraced = new(16, 1, 32);

        public override bool IsActive() => enable.value;
        
        
        public enum ShadowDenoiser
        {
            None,
            Spatial
        }
    }

    
    //需要走屏幕空间阴影
    //https://github.com/himma-bit/empty/blob/main/Assets/Scripts/ContactShadow/ContactShadowMapGenerater.cs
    [PostProcess("Contact Shadows", PostProcessInjectionPoint.BeforeRenderingGBuffer, SupportRenderPath.Deferred)]
    public partial class ContactShadowsRenderer : PostProcessVolumeRenderer<ContactShadows>
    {
        static class ShaderConstants
        {
            public static readonly int ParametersID = Shader.PropertyToID("_ContactShadowParamsParameters");
            public static readonly int Parameters2ID = Shader.PropertyToID("_ContactShadowParamsParameters2");
            public static readonly int Parameters3ID = Shader.PropertyToID("_ContactShadowParamsParameters3");
            public static readonly int TextureUAVID = Shader.PropertyToID("_ContactShadowTextureUAV");
            public static readonly int ContactShadowsRT = Shader.PropertyToID("_ContactShadowMap");
        }

        
        public RTHandle m_ContactShadowsTexture;
        private readonly RenderContactShadowPassData m_PassData = new();
        
        private ComputeShader m_ContactShadowCS;
        
   
        private DiffuseShadowDenoisePass m_DiffuseShadowDenoisePass;
        
        public override PostProcessPassInput postProcessPassInput => PostProcessPassInput.ScreenSpaceShadow;
        public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth;

        public override void Setup()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ContactShadowResources>();
            m_ContactShadowCS = runtimeShaders.contractShadowCS;
        }

        public override void AddRenderPasses(ref RenderingData renderingData)
        {
            switch (settings.shadowDenoiser.value)
            {
                case ContactShadows.ShadowDenoiser.None:
                    break;
                case ContactShadows.ShadowDenoiser.Spatial:
                    if (m_DiffuseShadowDenoisePass == null)
                        m_DiffuseShadowDenoisePass = new();
                    renderingData.cameraData.renderer.EnqueuePass(m_DiffuseShadowDenoisePass);
                    break;
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            var format = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Linear | GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;
            
            GetCompatibleDescriptor(ref desc, format);
            desc.enableRandomWrite = true;
            desc.useMipMap = false;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_ContactShadowsTexture, desc);

            Shader.SetGlobalTexture(ShaderConstants.ContactShadowsRT, m_ContactShadowsTexture);
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
            RenderContractShadows(m_PassData);
            m_PassData.contactShadowsCS = m_ContactShadowCS;
            var computeShader = m_PassData.contactShadowsCS;
            m_PassData.deferredContactShadowKernel = computeShader.FindKernel("ContactShadowMap");

            cmd.SetComputeVectorParam(computeShader, ShaderConstants.ParametersID, m_PassData.params1);
            cmd.SetComputeVectorParam(computeShader, ShaderConstants.Parameters2ID, m_PassData.params2);
            cmd.SetComputeVectorParam(computeShader, ShaderConstants.Parameters3ID, m_PassData.params3);
            cmd.SetComputeTextureParam(computeShader, m_PassData.deferredContactShadowKernel, ShaderConstants.TextureUAVID, m_ContactShadowsTexture);

            int width = renderingData.cameraData.cameraTargetDescriptor.width;
            int height = renderingData.cameraData.cameraTargetDescriptor.height;
            cmd.DispatchCompute(computeShader, m_PassData.deferredContactShadowKernel, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);
        }

        void RenderContractShadows(RenderContactShadowPassData passData)
        {
            float contactShadowRange = Mathf.Clamp(settings.fadeDistance.value, 0.0f, settings.maxDistance.value);
            float contactShadowFadeEnd = settings.maxDistance.value;
            float contactShadowOneOverFadeRange = 1.0f / Math.Max(1e-6f, contactShadowRange);

            float contactShadowMinDist = Mathf.Min(settings.minDistance.value, contactShadowFadeEnd);
            float contactShadowFadeIn = Mathf.Clamp(settings.fadeInDistance.value, 1e-6f, contactShadowFadeEnd);

            passData.params1 = new Vector4(settings.length.value, settings.distanceScaleFactor.value, contactShadowFadeEnd, contactShadowOneOverFadeRange);
            passData.params2 = new Vector4(0, contactShadowMinDist, contactShadowFadeIn, settings.rayBias.value * 0.01f);
            passData.params3 = new Vector4(settings.sampleCount.value, settings.thicknessScale.value * 10.0f, 0.0f, 0.0f);
        }

        public override void Dispose(bool disposing)
        {
            m_ContactShadowsTexture?.Release();
        }
    }
}
