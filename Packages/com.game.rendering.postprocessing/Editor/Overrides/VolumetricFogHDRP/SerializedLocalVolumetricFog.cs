using UnityEditor;
using UnityEngine;

namespace Game.Core.PostProcessing.UnityEditor
{
 class SerializedLocalVolumetricFog
    {
        public SerializedProperty densityParams;
        public SerializedProperty albedo;
        public SerializedProperty meanFreePath;

        public SerializedProperty blendingMode;
        public SerializedProperty priority;

        public SerializedProperty volumeTexture;
        public SerializedProperty textureScroll;
        public SerializedProperty textureTile;

        public SerializedProperty size;

        SerializedProperty positiveFade;
        SerializedProperty negativeFade;
        public SerializedProperty editorPositiveFade;
        public SerializedProperty editorNegativeFade;
        public SerializedProperty editorUniformFade;
        public SerializedProperty editorAdvancedFade;
        public SerializedProperty invertFade;

        public SerializedProperty distanceFadeStart;
        public SerializedProperty distanceFadeEnd;

        public SerializedProperty falloffMode;
        public SerializedProperty maskMode;
        public SerializedProperty materialMask;

        public bool isMaterialMaskCompatible;
        public bool isTextureMaskCompatible;

        SerializedObject m_SerializedObject;

        

        public SerializedLocalVolumetricFog(SerializedObject serializedObject)
        {
            m_SerializedObject = serializedObject;

            densityParams = m_SerializedObject.FindProperty("parameters");

            albedo = densityParams.FindPropertyRelative("albedo");
            meanFreePath = densityParams.FindPropertyRelative("meanFreePath");

            blendingMode = densityParams.FindPropertyRelative(nameof(LocalVolumetricFogArtistParameters.blendingMode));
            priority = densityParams.FindPropertyRelative(nameof(LocalVolumetricFogArtistParameters.priority));

            volumeTexture = densityParams.FindPropertyRelative("volumeMask");
            textureScroll = densityParams.FindPropertyRelative("textureScrollingSpeed");
            textureTile = densityParams.FindPropertyRelative("textureTiling");

            size = densityParams.FindPropertyRelative("size");

            positiveFade = densityParams.FindPropertyRelative("positiveFade");
            negativeFade = densityParams.FindPropertyRelative("negativeFade");

            editorPositiveFade = densityParams.FindPropertyRelative("m_EditorPositiveFade");
            editorNegativeFade = densityParams.FindPropertyRelative("m_EditorNegativeFade");
            editorUniformFade = densityParams.FindPropertyRelative("m_EditorUniformFade");
            editorAdvancedFade = densityParams.FindPropertyRelative("m_EditorAdvancedFade");

            invertFade = densityParams.FindPropertyRelative("invertFade");

            distanceFadeStart = densityParams.FindPropertyRelative("distanceFadeStart");
            distanceFadeEnd = densityParams.FindPropertyRelative("distanceFadeEnd");

            falloffMode = densityParams.FindPropertyRelative(nameof(LocalVolumetricFogArtistParameters.falloffMode));
            maskMode = densityParams.FindPropertyRelative(nameof(LocalVolumetricFogArtistParameters.maskMode));
            materialMask = densityParams.FindPropertyRelative(nameof(LocalVolumetricFogArtistParameters.materialMask));

            UpdateMaterialMaskCompatibility();
            UpdateTextureMaskCompatibility();
        }

        bool IsFogVolumeShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            else
            {
                if (shader == Shader.Find("Hidden/VolumeticLighting/DefaultFog"))
                {
                    return true;
                }

                else
                {
                    return false;
                }
            }
        }

        public void UpdateMaterialMaskCompatibility()
        {
            if (materialMask.objectReferenceValue is Material mat)
            {
                isMaterialMaskCompatible = IsFogVolumeShader(mat.shader);
                isMaterialMaskCompatible |= mat.FindPass("FogVolumeVoxelize") != -1;
            }
            else
                isMaterialMaskCompatible = false;
        }

        public void UpdateTextureMaskCompatibility()
        {
            if (volumeTexture.objectReferenceValue is Texture t)
                isTextureMaskCompatible = t.dimension == UnityEngine.Rendering.TextureDimension.Tex3D;
            else
                isTextureMaskCompatible = false;
        }

        public void Apply()
        {
            if (editorAdvancedFade.boolValue)
            {
                positiveFade.vector3Value = editorPositiveFade.vector3Value;
                negativeFade.vector3Value = editorNegativeFade.vector3Value;
            }
            else
            {
                positiveFade.vector3Value = negativeFade.vector3Value = new Vector3(
                    size.vector3Value.x > 0.00001 ? 1f - ((size.vector3Value.x - editorUniformFade.floatValue) / size.vector3Value.x) : 0f,
                    size.vector3Value.y > 0.00001 ? 1f - ((size.vector3Value.y - editorUniformFade.floatValue) / size.vector3Value.y) : 0f,
                    size.vector3Value.z > 0.00001 ? 1f - ((size.vector3Value.z - editorUniformFade.floatValue) / size.vector3Value.z) : 0f
                );
            }
            m_SerializedObject.ApplyModifiedProperties();
        }
    }
}
