using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace ShinySSRR
{

    [CustomEditor(typeof(ShinySSRR))]
    public class RenderFeatureEditor : Editor
    {

        SerializedProperty useDeferred, enableScreenSpaceNormalsPass, renderPassEvent, cameraLayerMask;
        Volume shinyVolume;

        private void OnEnable()
        {
            renderPassEvent = serializedObject.FindProperty("renderPassEvent");
            useDeferred = serializedObject.FindProperty("useDeferred");
            cameraLayerMask = serializedObject.FindProperty("cameraLayerMask");
            enableScreenSpaceNormalsPass = serializedObject.FindProperty("enableScreenSpaceNormalsPass");

            FindShinySSRRVolume();
        }


        void FindShinySSRRVolume()
        {
            Volume[] vols = FindObjectsOfType<Volume>(true);
            foreach (Volume volume in vols)
            {
                if (volume.sharedProfile != null && volume.sharedProfile.Has<ShinyScreenSpaceRaytracedReflections>())
                {
                    shinyVolume = volume;
                    return;
                }
            }
        }


        public override void OnInspectorGUI()
        {
            EditorGUILayout.PropertyField(renderPassEvent);
            EditorGUILayout.PropertyField(cameraLayerMask);
            EditorGUILayout.PropertyField(useDeferred);
            EditorGUILayout.Separator();
            if (shinyVolume != null)
            {
                EditorGUILayout.HelpBox("Select the Post Processing Volume to customize Shiny SSRR settings.", MessageType.Info);
                if (GUILayout.Button("Show Volume Settings"))
                {
                    Selection.SetActiveObjectWithContext(shinyVolume, null);
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Create a Post Processing volume in the scene to customize Shiny SSRR settings.", MessageType.Info);
            }

            EditorGUILayout.Separator();
            if (!useDeferred.boolValue)
            {
                GUILayout.Label("Advanced", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(enableScreenSpaceNormalsPass, new GUIContent("Enable Screen Space Normals"));
                if (!enableScreenSpaceNormalsPass.boolValue)
                {
                    EditorGUILayout.HelpBox("In forward rendering, surface normals are obtained from the bump map texture attached to the object material unless this 'Screen Space Normals' option is enabled. In this case, a full screen normals pass is used. This option is recommended if you use shaders that alter the surface normals.", MessageType.Info);
                }

            }
        }

    }
}