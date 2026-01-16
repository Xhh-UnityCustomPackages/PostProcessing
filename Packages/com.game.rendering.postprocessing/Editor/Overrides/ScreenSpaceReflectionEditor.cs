using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor;
using Game.Core.PostProcessing;


namespace Game.Core.PostProcessing.UnityEditor
{
    [CustomEditor(typeof(ScreenSpaceReflection))]
    public class ScreenSpaceReflectionEditor : VolumeComponentEditor
    {
        private SerializedDataParameter Enable;
        private SerializedDataParameter visibleInSceneView;
        private SerializedDataParameter mode;
        private SerializedDataParameter usedAlgorithm;
        private SerializedDataParameter enableMipmap;
        private SerializedDataParameter resolution;
        private SerializedDataParameter intensity;
        private SerializedDataParameter thickness;
        private SerializedDataParameter stepSize;
        private SerializedDataParameter minSmoothness;
        private SerializedDataParameter smoothnessFadeStart;
        private SerializedDataParameter maximumIterationCount;
        private SerializedDataParameter maximumMarchDistance;
        private SerializedDataParameter vignette;
        
        private SerializedDataParameter debugMode;
        private SerializedDataParameter split;
        
        private ScreenSpaceReflection m_ScreenSpaceReflection;
        
        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceReflection>(serializedObject);
            Enable = Unpack(o.Find(x => x.Enable));
            visibleInSceneView = Unpack(o.Find(x => x.visibleInSceneView));
            mode = Unpack(o.Find(x => x.mode));
            usedAlgorithm = Unpack(o.Find(x => x.usedAlgorithm));
            enableMipmap = Unpack(o.Find(x => x.enableMipmap));
            resolution = Unpack(o.Find(x => x.resolution));
            intensity = Unpack(o.Find(x => x.intensity));
            thickness = Unpack(o.Find(x => x.thickness));
            stepSize = Unpack(o.Find(x => x.stepSize));
            minSmoothness = Unpack(o.Find(x => x.minSmoothness));
            smoothnessFadeStart = Unpack(o.Find(x => x.smoothnessFadeStart));
            maximumIterationCount = Unpack(o.Find(x => x.maximumIterationCount));
            maximumMarchDistance = Unpack(o.Find(x => x.maximumMarchDistance));
            vignette = Unpack(o.Find(x => x.vignette));
            debugMode = Unpack(o.Find(x => x.debugMode));
            split = Unpack(o.Find(x => x.split));
        }

        void PresetUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Apply Preset:", GUILayout.Width(EditorGUIUtility.labelWidth));
           
            if (GUILayout.Button("Fast"))
            {
                m_ScreenSpaceReflection.ApplyPreset(ScreenSpaceReflection.Preset.Fast);
                EditorUtility.SetDirty(target);
            }
            
            if (GUILayout.Button("High"))
            {
                m_ScreenSpaceReflection.ApplyPreset(ScreenSpaceReflection.Preset.High);
                EditorUtility.SetDirty(target);
            }
            
            if (GUILayout.Button("Ultra"))
            {
                m_ScreenSpaceReflection.ApplyPreset(ScreenSpaceReflection.Preset.Ultra);
                EditorUtility.SetDirty(target);
            }

            EditorGUILayout.EndHorizontal();
        }

        public override void OnInspectorGUI()
        {
            m_ScreenSpaceReflection = (ScreenSpaceReflection)target;
            
            PropertyField(Enable);
            PropertyField(visibleInSceneView);
            PropertyField(mode);
            PropertyField(usedAlgorithm);
            PropertyField(enableMipmap);
            PropertyField(intensity);
            PropertyField(minSmoothness);
            PropertyField(smoothnessFadeStart);
            PropertyField(vignette);
            
            EditorGUILayout.Space(10);
            PresetUI();
            PropertyField(resolution);
            PropertyField(maximumIterationCount);
            
            if (mode.value.GetEnumValue<ScreenSpaceReflection.RaytraceModes>() == ScreenSpaceReflection.RaytraceModes.LinearTracing)
            {
                PropertyField(stepSize);
                PropertyField(maximumMarchDistance);
            }

            PropertyField(thickness);
            
            PropertyField(debugMode);
            if (debugMode.value.GetEnumValue<ScreenSpaceReflection.DebugMode>() == ScreenSpaceReflection.DebugMode.Split)
            {
                PropertyField(split);
            }
            
            m_ScreenSpaceReflection.smoothnessFadeStart.value = Mathf.Max(m_ScreenSpaceReflection.minSmoothness.value, m_ScreenSpaceReflection.smoothnessFadeStart.value);
        }
    }
}