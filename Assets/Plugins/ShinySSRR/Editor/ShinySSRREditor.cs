using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using System.Text;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShinySSRR
{

    [CustomEditor(typeof(ShinyScreenSpaceRaytracedReflections))]
    public class ShinySSRREditor : VolumeComponentEditor
    {

        SerializedDataParameter reflectionsMultiplier, showInSceneView;
        SerializedDataParameter reflectionsIntensityCurve, reflectionsSmoothnessCurve;
        SerializedDataParameter downsampling, depthBias, computeBackFaces, thicknessMinimum, computeBackFacesLayerMask;
        SerializedDataParameter outputMode, separationPos, lowPrecision, stopNaN;
        SerializedDataParameter stencilCheck, stencilValue, stencilCompareFunction;
        SerializedDataParameter temporalFilter, temporalFilterResponseSpeed;
        SerializedDataParameter sampleCount, maxRayLength, thickness, binarySearchIterations, refineThickness, thicknessFine, decay, jitter, animatedJitter;
        SerializedDataParameter fresnel, fuzzyness, contactHardening, minimumBlur;
        SerializedDataParameter blurDownsampling, blurStrength, specularControl, specularSoftenPower, vignetteSize, vignettePower;
        SerializedDataParameter useReflectionsScripts, reflectionsScriptsLayerMask, skipDeferredPass;

        Reflections[] reflections;
        public Texture bulbOnIcon, bulbOffIcon, deleteIcon, arrowRight;
        readonly StringBuilder sb = new StringBuilder();

        public override void OnEnable()
        {
            base.OnEnable();

            var o = new PropertyFetcher<ShinyScreenSpaceRaytracedReflections>(serializedObject);

            showInSceneView = Unpack(o.Find(x => x.showInSceneView));
            reflectionsMultiplier = Unpack(o.Find(x => x.reflectionsMultiplier));
            reflectionsIntensityCurve = Unpack(o.Find(x => x.reflectionsIntensityCurve));
            reflectionsSmoothnessCurve = Unpack(o.Find(x => x.reflectionsSmoothnessCurve));
            computeBackFaces = Unpack(o.Find(x => x.computeBackFaces));
            computeBackFacesLayerMask = Unpack(o.Find(x => x.computeBackFacesLayerMask));
            thicknessMinimum = Unpack(o.Find(x => x.thicknessMinimum));
            downsampling = Unpack(o.Find(x => x.downsampling));
            depthBias = Unpack(o.Find(x => x.depthBias));
            outputMode = Unpack(o.Find(x => x.outputMode));
            separationPos = Unpack(o.Find(x => x.separationPos));
            lowPrecision = Unpack(o.Find(x => x.lowPrecision));
            stopNaN = Unpack(o.Find(x => x.stopNaN));
            stencilCheck = Unpack(o.Find(x => x.stencilCheck));
            stencilValue = Unpack(o.Find(x => x.stencilValue));
            stencilCompareFunction = Unpack(o.Find(x => x.stencilCompareFunction));
            temporalFilter = Unpack(o.Find(x => x.temporalFilter));
            temporalFilterResponseSpeed = Unpack(o.Find(x => x.temporalFilterResponseSpeed));
            sampleCount = Unpack(o.Find(x => x.sampleCount));
            maxRayLength = Unpack(o.Find(x => x.maxRayLength));
            binarySearchIterations = Unpack(o.Find(x => x.binarySearchIterations));
            thickness = Unpack(o.Find(x => x.thickness));
            thicknessFine = Unpack(o.Find(x => x.thicknessFine));
            refineThickness = Unpack(o.Find(x => x.refineThickness));
            decay = Unpack(o.Find(x => x.decay));
            fresnel = Unpack(o.Find(x => x.fresnel));
            fuzzyness = Unpack(o.Find(x => x.fuzzyness));
            contactHardening = Unpack(o.Find(x => x.contactHardening));
            minimumBlur = Unpack(o.Find(x => x.minimumBlur));
            jitter = Unpack(o.Find(x => x.jitter));
            animatedJitter = Unpack(o.Find(x => x.animatedJitter));
            blurDownsampling = Unpack(o.Find(x => x.blurDownsampling));
            blurStrength = Unpack(o.Find(x => x.blurStrength));
            specularControl = Unpack(o.Find(x => x.specularControl));
            specularSoftenPower = Unpack(o.Find(x => x.specularSoftenPower));
            vignetteSize = Unpack(o.Find(x => x.vignetteSize));
            vignettePower = Unpack(o.Find(x => x.vignettePower));
            useReflectionsScripts = Unpack(o.Find(x => x.useReflectionsScripts));
            reflectionsScriptsLayerMask = Unpack(o.Find(x => x.reflectionsScriptsLayerMask));
            skipDeferredPass = Unpack(o.Find(x => x.skipDeferredPass));

#if UNITY_2020_1_OR_NEWER
            reflections = GameObject.FindObjectsOfType<Reflections>(true);
#else
            reflections = GameObject.FindObjectsOfType<Reflections>();
#endif

            bulbOnIcon = Resources.Load<Texture>("bulbOn");
            bulbOffIcon = Resources.Load<Texture>("bulbOff");
            arrowRight = Resources.Load<Texture>("arrowRight");
            deleteIcon = Resources.Load<Texture>("delete");
        }

        public override void OnInspectorGUI()
        {

            UniversalRenderPipelineAsset pipe = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipe == null)
            {
                EditorGUILayout.HelpBox("Universal Rendering Pipeline asset is not set in 'Project Settings / Graphics or Quality' !", MessageType.Error);
                EditorGUILayout.Separator();
                GUI.enabled = false;
            }
            else if (!ShinySSRR.installed)
            {
                EditorGUILayout.HelpBox("Shiny SSRR Render Feature must be added to the rendering pipeline renderer.", MessageType.Error);
                if (GUILayout.Button("Go to Universal Rendering Pipeline Asset"))
                {
                    Selection.activeObject = pipe;
                }
                EditorGUILayout.Separator();
                GUI.enabled = false;
            }
            else
            {
                if (!pipe.supportsCameraDepthTexture)
                {
                    EditorGUILayout.HelpBox("Depth Texture option is required. Check Universal Rendering Pipeline asset!", MessageType.Warning);
                    if (GUILayout.Button("Go to Universal Rendering Pipeline Asset"))
                    {
                        Selection.activeObject = pipe;
                    }
                    EditorGUILayout.Separator();
                    GUI.enabled = false;
                }
            }

            if (pipe != null)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                if (GUILayout.Button("Show URP Renderer Settings"))
                {
                    var so = new SerializedObject(pipe);
                    var prop = so.FindProperty("m_RendererDataList");
                    if (prop != null && prop.arraySize > 0)
                    {
                        var o = prop.GetArrayElementAtIndex(0);
                        if (o != null)
                        {
                            Selection.SetActiveObjectWithContext(o.objectReferenceValue, null);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }


            serializedObject.Update();

            int reflectionsCount = reflections != null ? reflections.Length : 0;

            PropertyField(reflectionsMultiplier, new GUIContent("Intensity"));
            PropertyField(showInSceneView);

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Quality Settings", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Apply Preset:", GUILayout.Width(EditorGUIUtility.labelWidth));
            ShinyScreenSpaceRaytracedReflections ssr = (ShinyScreenSpaceRaytracedReflections)target;
            if (GUILayout.Button("Fast"))
            {
                ssr.ApplyRaytracingPreset(RaytracingPreset.Fast);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("Medium"))
            {
                ssr.ApplyRaytracingPreset(RaytracingPreset.Medium);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("High"))
            {
                ssr.ApplyRaytracingPreset(RaytracingPreset.High);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("Superb"))
            {
                ssr.ApplyRaytracingPreset(RaytracingPreset.Superb);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("Ultra"))
            {
                ssr.ApplyRaytracingPreset(RaytracingPreset.Ultra);
                EditorUtility.SetDirty(target);
            }
            EditorGUILayout.EndHorizontal();
            PropertyField(sampleCount);
            PropertyField(maxRayLength);
            PropertyField(binarySearchIterations);
            PropertyField(computeBackFaces);
            if (computeBackFaces.value.boolValue)
            {
                EditorGUI.indentLevel++;
                PropertyField(thicknessMinimum, new GUIContent("Min Thickness"));
                PropertyField(thickness, new GUIContent("Max Thickness"));
                PropertyField(computeBackFacesLayerMask, new GUIContent("Layer Mask"));
                EditorGUI.indentLevel--;
            }
            else
            {
                PropertyField(thickness, new GUIContent("Max Thickness"));
            }
            PropertyField(refineThickness);
            if (refineThickness.value.boolValue)
            {
                EditorGUI.indentLevel++;
                PropertyField(thicknessFine);
                EditorGUI.indentLevel--;
            }
            PropertyField(jitter);
            PropertyField(animatedJitter);
#if UNITY_2021_3_OR_NEWER
            PropertyField(temporalFilter);
            if (temporalFilter.value.boolValue)
            {
                EditorGUI.indentLevel++;
                PropertyField(temporalFilterResponseSpeed, new GUIContent("Response Speed"));
                EditorGUI.indentLevel--;
            }
#endif
            PropertyField(downsampling);
            PropertyField(depthBias);

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Reflection Intensity", EditorStyles.miniLabel);


            PropertyField(reflectionsIntensityCurve, new GUIContent("Metallic Curve"));
            PropertyField(reflectionsSmoothnessCurve, new GUIContent("Smoothness Curve"));

            PropertyField(fresnel);
            PropertyField(decay);
            PropertyField(specularControl);
            if (specularControl.value.boolValue)
            {
                EditorGUI.indentLevel++;
                PropertyField(specularSoftenPower, new GUIContent("Soften Power"));
                EditorGUI.indentLevel--;
            }
            PropertyField(vignetteSize);
            PropertyField(vignettePower);

            EditorGUILayout.Separator();
            PropertyField(fuzzyness, new GUIContent("Fuzziness"));
            PropertyField(contactHardening);
            PropertyField(minimumBlur);
            PropertyField(blurDownsampling);
            PropertyField(blurStrength);

            EditorGUILayout.Separator();
            PropertyField(outputMode);
            if (outputMode.value.intValue == (int)OutputMode.SideBySideComparison)
            {
                EditorGUI.indentLevel++;
                PropertyField(separationPos);
                EditorGUI.indentLevel--;
            }
            PropertyField(lowPrecision);
            PropertyField(stopNaN, new GUIContent("Stop NaN"));
            PropertyField(stencilCheck);
            if (stencilCheck.value.boolValue)
            {
                EditorGUI.indentLevel++;
                PropertyField(stencilValue, new GUIContent("Value"));
                PropertyField(stencilCompareFunction, new GUIContent("Compare Funciton"));
                EditorGUI.indentLevel--;
            }

            if (reflectionsCount > 0)
            {
                EditorGUILayout.Separator();
                if (!ShinySSRR.isDeferredActive)
                {
                    EditorGUILayout.HelpBox("Some settings may be overridden by Reflections scripts on specific objects.", MessageType.Info);
                }
                EditorGUILayout.Separator();
                EditorGUILayout.LabelField("Reflections scripts in Scene", EditorStyles.helpBox);
                if (ShinySSRR.isDeferredActive)
                {
                    EditorGUILayout.HelpBox("In deferred mode, you don't need to use Reflections scripts. But they can be used to force adding custom reflections on transparent objects like puddles.", MessageType.Info);
                    PropertyField(useReflectionsScripts);
                    if (useReflectionsScripts.value.boolValue)
                    {
                        PropertyField(skipDeferredPass);
                    }
                }
                PropertyField(reflectionsScriptsLayerMask, new GUIContent("Layer Mask", "Which reflections scripts can be used."));
                for (int k = 0; k < reflectionsCount; k++)
                {
                    Reflections refl = reflections[k];
                    if (refl == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = refl.gameObject.activeInHierarchy;
                    if (GUILayout.Button(new GUIContent(refl.enabled ? bulbOnIcon : bulbOffIcon, "Toggle on/off this reflection"), EditorStyles.miniButton, GUILayout.Width(35)))
                    {
                        refl.enabled = !refl.enabled;
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button(new GUIContent(deleteIcon, "Remove this reflection script"), EditorStyles.miniButton, GUILayout.Width(35)))
                    {
                        if (EditorUtility.DisplayDialog("Confirmation", "Remove the reflection script on " + refl.gameObject.name + "?", "Ok", "Cancel"))
                        {
                            GameObject.DestroyImmediate(refl);
                            reflections[k] = null;
                            continue;
                        }
                    }
                    if (GUILayout.Button(new GUIContent(arrowRight, "Select this reflection script"), EditorStyles.miniButton, GUILayout.Width(35), GUILayout.Width(40)))
                    {
                        Selection.activeObject = refl.gameObject;
                        EditorGUIUtility.PingObject(refl.gameObject);
                        GUIUtility.ExitGUI();
                    }
                    GUI.enabled = refl.isActiveAndEnabled;
                    sb.Clear();
                    sb.Append(refl.name);
                    if (!refl.gameObject.activeInHierarchy)
                    {
                        sb.Append(" (hidden gameobject)");
                    }
                    if (refl.overrideGlobalSettings)
                    {
                        sb.Append(" (uses custom settings)");
                    }
                    GUILayout.Label(sb.ToString());
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                }
            }
            else if (reflectionsCount == 0)
            {
                if (!ShinySSRR.isDeferredActive)
                {
                    EditorGUILayout.Separator();
                    EditorGUILayout.LabelField("Reflections in Scene", EditorStyles.helpBox);
                    EditorGUILayout.HelpBox("In forward rendering path, add a Reflections script to any object or group of objects that you want to get reflections.", MessageType.Info);
                }

            }

            if (serializedObject.ApplyModifiedProperties())
            {
                Reflections.needUpdateMaterials = true; // / reflections scripts that do not override global settings need to be updated as well
            }

        }

    }
}