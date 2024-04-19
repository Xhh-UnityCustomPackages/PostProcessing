using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Rendering;
using UnityEditor;


namespace Game.Core.PostProcessing.UnityEditor
{

    sealed class LocalWindParameterDrawer
    {
        static readonly string[] modeNames = Enum.GetNames(typeof(WindParameter.WindOverrideMode));
        static readonly string[] modeNamesNoMultiply = { WindParameter.WindOverrideMode.Custom.ToString(), WindParameter.WindOverrideMode.Global.ToString(), WindParameter.WindOverrideMode.Additive.ToString() };
        static readonly int popupWidth = 70;

        public static bool BeginGUI(out Rect rect, GUIContent title, SerializedDataParameter parameter, SerializedProperty mode, bool excludeMultiply)
        {
            rect = EditorGUILayout.GetControlRect();
            rect.xMax -= popupWidth + 2;

            var popupRect = rect;
            popupRect.x = rect.xMax + 2;
            popupRect.width = popupWidth;
            mode.intValue = EditorGUI.Popup(popupRect, mode.intValue, excludeMultiply ? modeNamesNoMultiply : modeNames);

            if (mode.intValue == (int)WindParameter.WindOverrideMode.Additive)
            {
                var value = parameter.value.FindPropertyRelative("additiveValue");
                value.floatValue = EditorGUI.FloatField(rect, title, value.floatValue);
            }
            else if (mode.intValue == (int)WindParameter.WindOverrideMode.Multiply)
            {
                var value = parameter.value.FindPropertyRelative("multiplyValue");
                value.floatValue = EditorGUI.FloatField(rect, title, value.floatValue);
            }
            else
            {
                if (mode.intValue == (int)WindParameter.WindOverrideMode.Global)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.showMixedValue = true;
                }
                return false;
            }
            return true;
        }

        public static void EndGUI(SerializedProperty mode)
        {
            if (mode.intValue == (int)WindParameter.WindOverrideMode.Global)
            {
                EditorGUI.showMixedValue = false;
                EditorGUI.EndDisabledGroup();
            }
        }
    }

    [VolumeParameterDrawer(typeof(WindOrientationParameter))]
    sealed class WindOrientationParameterDrawer : VolumeParameterDrawer
    {
        public override bool OnGUI(SerializedDataParameter parameter, GUIContent title)
        {
            var mode = parameter.value.FindPropertyRelative("mode");
            if (!LocalWindParameterDrawer.BeginGUI(out var rect, title, parameter, mode, true))
            {
                var value = parameter.value.FindPropertyRelative("customValue");
                value.floatValue = EditorGUI.Slider(rect, title, value.floatValue, 0.0f, 360.0f);
            }
            LocalWindParameterDrawer.EndGUI(mode);

            return true;
        }
    }

    [VolumeParameterDrawer(typeof(WindSpeedParameter))]
    sealed class WindSpeedParameterDrawer : VolumeParameterDrawer
    {
        public override bool OnGUI(SerializedDataParameter parameter, GUIContent title)
        {
            var mode = parameter.value.FindPropertyRelative("mode");
            if (!LocalWindParameterDrawer.BeginGUI(out var rect, title, parameter, mode, false))
            {
                var value = parameter.value.FindPropertyRelative("customValue");
                value.floatValue = EditorGUI.FloatField(rect, title, value.floatValue);
            }
            LocalWindParameterDrawer.EndGUI(mode);

            return true;
        }
    }
}
