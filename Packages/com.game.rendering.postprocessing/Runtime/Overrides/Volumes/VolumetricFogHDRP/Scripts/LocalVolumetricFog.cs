using System;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.Serialization;
using static Unity.Mathematics.math;
using UnityEngine.Rendering;

using ShaderIDs = Game.Core.PostProcessing.VolumetricFogShaderIDs;

namespace Game.Core.PostProcessing
{
    [ExecuteAlways]
    [AddComponentMenu("Rendering/Local Volumetric Fog")]
    public class LocalVolumetricFog : MonoBehaviour
    {
         /// <summary>Local Volumetric Fog parameters.</summary>
        public LocalVolumetricFogArtistParameters parameters = new LocalVolumetricFogArtistParameters(Color.white, 10.0f, 0.0f);

        /// <summary>Action shich should be performed after updating the texture.</summary>
        public Action OnTextureUpdated;

        [NonSerialized]
        MaterialPropertyBlock m_RenderingProperties;
        [NonSerialized]
        int m_GlobalIndex;

        [NonSerialized]
        internal Material textureMaterial;
        [NonSerialized]
        internal int currentLocalIndex;
        /// <summary>Gather and Update any parameters that may have changed.</summary>
        internal void PrepareParameters(float time)
        {
            parameters.Update(time);
        }

        private void OnEnable()
        {
            LocalVolumetricFogManager.manager.RegisterVolume(this);

#if UNITY_EDITOR
            // Handle scene visibility
            SceneVisibilityManager.visibilityChanged -= UpdateLocalVolumetricFogVisibility;
            SceneVisibilityManager.visibilityChanged += UpdateLocalVolumetricFogVisibility;
            SceneView.duringSceneGui -= UpdateLocalVolumetricFogVisibilityPrefabStage;
            SceneView.duringSceneGui += UpdateLocalVolumetricFogVisibilityPrefabStage;
#endif
        }

        private void Update()
        {
            PrepareDrawCall(currentLocalIndex);
        }

        internal int GetGlobalIndex() => m_GlobalIndex;

        internal void PrepareDrawCall(int globalIndex)
        {
            m_GlobalIndex = globalIndex;
            if (!LocalVolumetricFogManager.manager.IsInitialized())
                return;

            if (m_RenderingProperties == null)
                m_RenderingProperties = new MaterialPropertyBlock();
            m_RenderingProperties.Clear();

            m_RenderingProperties.SetInteger(ShaderIDs._VolumetricFogGlobalIndex, m_GlobalIndex);

            Material material = parameters.materialMask;
            if (parameters.maskMode == LocalVolumetricFogMaskMode.Texture)
            {
                bool alphaTexture = false;
                if (textureMaterial == null)
                {
                    var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogHDRPResources>();
                    textureMaterial = CoreUtils.CreateEngineMaterial(runtimeShaders.defaultFogVolumeShader);
                }

                FogVolumeAPI.SetupFogVolumeBlendMode(textureMaterial, parameters.blendingMode);

                material = textureMaterial;
                if (parameters.volumeMask != null)
                {

                    m_RenderingProperties.SetTexture(ShaderIDs._VolumetricMask, parameters.volumeMask);
                    textureMaterial.EnableKeyword("_ENABLE_VOLUMETRIC_FOG_MASK");
                    if (parameters.volumeMask is Texture3D t3d)
                        alphaTexture = t3d.format == TextureFormat.Alpha8;
                }

                else
                {
                    textureMaterial.DisableKeyword("_ENABLE_VOLUMETRIC_FOG_MASK");
                }

                m_RenderingProperties.SetVector(ShaderIDs._VolumetricScrollSpeed, parameters.textureScrollingSpeed);
                m_RenderingProperties.SetVector(ShaderIDs._VolumetricTiling, parameters.textureTiling);
                m_RenderingProperties.SetFloat(ShaderIDs._AlphaOnlyTexture, alphaTexture ? 1 : 0);

                m_RenderingProperties.SetFloat(FogVolumeAPI.k_FogDistanceProperty, parameters.meanFreePath);
                m_RenderingProperties.SetColor(FogVolumeAPI.k_SingleScatteringAlbedoProperty, parameters.albedo.gamma);
            }

            if (material == null)
                return;

            m_RenderingProperties.SetBuffer(ShaderIDs._VolumetricMaterialData, LocalVolumetricFogManager.manager.volumetricMaterialDataBuffer);

            // Send local properties inside constants instead of structured buffer to optimize GPU reads
            var engineData = parameters.ConvertToEngineData();
            var tr = transform;
            var position = tr.position;
            var bounds = new OrientedBBox(Matrix4x4.TRS(position * 2, tr.rotation, parameters.size * 2));
            m_RenderingProperties.SetVector(ShaderIDs._VolumetricMaterialObbRight, bounds.right);
            m_RenderingProperties.SetVector(ShaderIDs._VolumetricMaterialObbUp, bounds.up);
            m_RenderingProperties.SetVector(ShaderIDs._VolumetricMaterialObbExtents, new Vector3(bounds.extentX, bounds.extentY, bounds.extentZ));
            m_RenderingProperties.SetVector(ShaderIDs._VolumetricMaterialObbCenter, bounds.center);

            m_RenderingProperties.SetVector(ShaderIDs._VolumetricMaterialRcpPosFaceFade, engineData.rcpPosFaceFade);
            m_RenderingProperties.SetVector(ShaderIDs._VolumetricMaterialRcpNegFaceFade, engineData.rcpNegFaceFade);
            m_RenderingProperties.SetInteger(ShaderIDs._VolumetricMaterialInvertFade, engineData.invertFade);

            m_RenderingProperties.SetFloat(ShaderIDs._VolumetricMaterialRcpDistFadeLen, engineData.rcpDistFadeLen);
            m_RenderingProperties.SetFloat(ShaderIDs._VolumetricMaterialEndTimesRcpDistFadeLen, engineData.endTimesRcpDistFadeLen);
            m_RenderingProperties.SetInteger(ShaderIDs._VolumetricMaterialFalloffMode, (int)engineData.falloffMode);

            var AABBExtents = abs(bounds.right * bounds.extentX) +
                     abs(bounds.up * bounds.extentY) +
                     abs(bounds.forward * bounds.extentZ);
            var AABB = new Bounds(bounds.center * 0.5f, AABBExtents);
            var renderParams = new RenderParams
            {
                layer = gameObject.layer,
                rendererPriority = parameters.priority,
                worldBounds = AABB,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                renderingLayerMask = 0xFFFFFFFF,
                material = material,
                matProps = m_RenderingProperties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
#if UNITY_EDITOR
                overrideSceneCullingMask = true,
                sceneCullingMask = gameObject.sceneCullingMask,
#endif
            };                       
                Graphics.RenderPrimitivesIndexedIndirect(renderParams, MeshTopology.Triangles, LocalVolumetricFogManager.manager.volumetricMaterialIndexBuffer, LocalVolumetricFogManager.manager.globalIndirectBuffer, 1, m_GlobalIndex);                        
        }
#if UNITY_EDITOR
        void UpdateLocalVolumetricFogVisibility()
        {
            bool isVisible = !SceneVisibilityManager.instance.IsHidden(gameObject);
            UpdateLocalVolumetricFogVisibility(isVisible);
        }

        void UpdateLocalVolumetricFogVisibilityPrefabStage(SceneView sv)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                bool isVisible = true;
                bool isInPrefabStage = gameObject.scene == stage.scene;

                if (!isInPrefabStage && stage.mode == PrefabStage.Mode.InIsolation)
                    isVisible = false;
                if (!isInPrefabStage && CoreUtils.IsSceneViewPrefabStageContextHidden())
                    isVisible = false;

                UpdateLocalVolumetricFogVisibility(isVisible);
            }
        }

        void UpdateLocalVolumetricFogVisibility(bool isVisible)
        {
            if (isVisible)
            {
                if (!LocalVolumetricFogManager.manager.ContainsVolume(this))
                    LocalVolumetricFogManager.manager.RegisterVolume(this);
            }
            else
            {
                if (LocalVolumetricFogManager.manager.ContainsVolume(this))
                    LocalVolumetricFogManager.manager.DeRegisterVolume(this);
            }
        }
#endif

        private void OnDisable()
        {
            LocalVolumetricFogManager.manager.DeRegisterVolume(this);

#if UNITY_EDITOR
            SceneVisibilityManager.visibilityChanged -= UpdateLocalVolumetricFogVisibility;
            SceneView.duringSceneGui -= UpdateLocalVolumetricFogVisibilityPrefabStage;
#endif
        }

        private void OnValidate()
        {
            parameters.Constrain();
        }

    }
}

