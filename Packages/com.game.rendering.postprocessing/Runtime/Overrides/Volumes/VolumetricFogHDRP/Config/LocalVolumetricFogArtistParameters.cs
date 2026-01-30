using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.Core.PostProcessing
{
    [Serializable]
    public partial struct LocalVolumetricFogArtistParameters
    {
        [ColorUsage(false)] public Color albedo;

        /// <summary>Mean free path, in meters: [1, inf].</summary>
        public float meanFreePath; // Should be chromatic - this is an optimization!

        /// <summary>
        /// Specifies how the fog in the volume will interact with the fog.
        /// </summary>
        public LocalVolumetricFogBlendingMode blendingMode;

        /// <summary>
        /// Rendering priority of the volume, higher priority will be rendered first.
        /// </summary>
        public int priority;

        /// <summary>Anisotropy of the phase function: [-1, 1]. Positive values result in forward scattering, and negative values - in backward scattering.</summary>
        [FormerlySerializedAs("asymmetry")]
        public float anisotropy; // . Not currently available for Local Volumetric Fog

        /// <summary>Texture containing density values.</summary>
        public Texture volumeMask;

        /// <summary>Scrolling speed of the density texture.</summary>
        public Vector3 textureScrollingSpeed;

        /// <summary>Tiling rate of the density texture.</summary>
        public Vector3 textureTiling;

        /// <summary>Edge fade factor along the positive X, Y and Z axes.</summary>
        [FormerlySerializedAs("m_PositiveFade")]
        public Vector3 positiveFade;

        /// <summary>Edge fade factor along the negative X, Y and Z axes.</summary>
        [FormerlySerializedAs("m_NegativeFade")]
        public Vector3 negativeFade;

        [SerializeField, FormerlySerializedAs("m_UniformFade")]
        public float m_EditorUniformFade;

        [SerializeField] public Vector3 m_EditorPositiveFade;
        [SerializeField] public Vector3 m_EditorNegativeFade;

        [SerializeField, FormerlySerializedAs("advancedFade"), FormerlySerializedAs("m_AdvancedFade")]
        public bool m_EditorAdvancedFade;

        /// <summary>Dimensions of the volume.</summary>
        public Vector3 size;

        /// <summary>Inverts the fade gradient.</summary>
        public bool invertFade;

        /// <summary>Distance at which density fading starts.</summary>
        public float distanceFadeStart;

        /// <summary>Distance at which density fading ends.</summary>
        public float distanceFadeEnd;

        /// <summary>Allows translation of the tiling density texture.</summary>
        [SerializeField, FormerlySerializedAs("volumeScrollingAmount")]
        public Vector3 textureOffset;

        /// <summary>When Blend Distance is above 0, controls which kind of falloff is applied to the transition area.</summary>
        public LocalVolumetricFogFalloffMode falloffMode;

        /// <summary>The mask mode to use when writing this volume in the volumetric fog.</summary>
        public LocalVolumetricFogMaskMode maskMode;

        /// <summary>The material used to mask the local volumetric fog when the mask mode is set to Material. The material needs to use the "Fog Volume" material type in Shader Graph.</summary>
        public Material materialMask;

        /// <summary>Minimum fog distance you can set in the meanFreePath parameter</summary>
        internal const float kMinFogDistance = 0.05f;

        /// <summary>Constructor.</summary>
        /// <param name="color">Single scattering albedo.</param>
        /// <param name="_meanFreePath">Mean free path.</param>
        /// <param name="_anisotropy">Anisotropy.</param>
        public LocalVolumetricFogArtistParameters(Color color, float _meanFreePath, float _anisotropy)
        {
            albedo = color;
            meanFreePath = _meanFreePath;
            blendingMode = LocalVolumetricFogBlendingMode.Additive;
            priority = 0;
            anisotropy = _anisotropy;

            volumeMask = null;
            materialMask = null;
            textureScrollingSpeed = Vector3.zero;
            textureTiling = Vector3.one;
            textureOffset = textureScrollingSpeed;

            size = Vector3.one;

            positiveFade = Vector3.one * 0.1f;
            negativeFade = Vector3.one * 0.1f;
            invertFade = false;

            distanceFadeStart = 10000;
            distanceFadeEnd = 10000;

            falloffMode = LocalVolumetricFogFalloffMode.Linear;
            maskMode = LocalVolumetricFogMaskMode.Texture;

            m_EditorPositiveFade = positiveFade;
            m_EditorNegativeFade = negativeFade;
            m_EditorUniformFade = 0.1f;
            m_EditorAdvancedFade = false;
        }

        internal void Update(float time)
        {
            //Update scrolling based on deltaTime
            if (volumeMask != null)
            {
                // Switch from right-handed to left-handed coordinate system.
                textureOffset = -(textureScrollingSpeed * time);
            }
        }

        internal void Constrain()
        {
            albedo.r = Mathf.Clamp01(albedo.r);
            albedo.g = Mathf.Clamp01(albedo.g);
            albedo.b = Mathf.Clamp01(albedo.b);
            albedo.a = 1.0f;

            meanFreePath = Mathf.Clamp(meanFreePath, kMinFogDistance, float.MaxValue);

            anisotropy = Mathf.Clamp(anisotropy, -1.0f, 1.0f);

            textureOffset = Vector3.zero;

            distanceFadeStart = Mathf.Max(0, distanceFadeStart);
            distanceFadeEnd = Mathf.Max(distanceFadeStart, distanceFadeEnd);
        }

        internal LocalVolumetricFogEngineData ConvertToEngineData()
        {
            LocalVolumetricFogEngineData data = new LocalVolumetricFogEngineData();

            data.scattering = VolumeRenderingUtils.ScatteringFromExtinctionAndAlbedo(VolumeRenderingUtils.ExtinctionFromMeanFreePath(meanFreePath), (Vector4)albedo);

            data.blendingMode = blendingMode;

            data.textureScroll = textureOffset;
            data.textureTiling = textureTiling;

            // Clamp to avoid NaNs.
            Vector3 positiveFade = this.positiveFade;
            Vector3 negativeFade = this.negativeFade;

            data.rcpPosFaceFade.x = Mathf.Min(1.0f / positiveFade.x, float.MaxValue);
            data.rcpPosFaceFade.y = Mathf.Min(1.0f / positiveFade.y, float.MaxValue);
            data.rcpPosFaceFade.z = Mathf.Min(1.0f / positiveFade.z, float.MaxValue);

            data.rcpNegFaceFade.y = Mathf.Min(1.0f / negativeFade.y, float.MaxValue);
            data.rcpNegFaceFade.x = Mathf.Min(1.0f / negativeFade.x, float.MaxValue);
            data.rcpNegFaceFade.z = Mathf.Min(1.0f / negativeFade.z, float.MaxValue);

            data.invertFade = invertFade ? 1 : 0;
            data.falloffMode = falloffMode;

            float distFadeLen = Mathf.Max(distanceFadeEnd - distanceFadeStart, 0.00001526f);

            data.rcpDistFadeLen = 1.0f / distFadeLen;
            data.endTimesRcpDistFadeLen = distanceFadeEnd * data.rcpDistFadeLen;

            return data;
        }
    }
}