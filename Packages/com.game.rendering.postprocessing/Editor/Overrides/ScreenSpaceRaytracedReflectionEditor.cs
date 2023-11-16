using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor;
using Game.Core.PostProcessing;


namespace Game.Core.PostProcessing.UnityEditor
{
    [CustomEditor(typeof(ScreenSpaceRaytracedReflection))]
    public class ScreenSpaceRaytracedReflectionEditor : VolumeComponentEditor
    {

        SerializedDataParameter reflectionsMultiplier, showInSceneView;
        SerializedDataParameter reflectionsIntensityCurve, reflectionsSmoothnessCurve;
        SerializedDataParameter downsampling, depthBias, computeBackFaces, thicknessMinimum, computeBackFacesLayerMask;
        SerializedDataParameter outputMode, separationPos, lowPrecision;
        SerializedDataParameter stencilCheck, stencilValue, stencilCompareFunction;
        SerializedDataParameter noiseTex;
        // SerializedDataParameter temporalFilter, temporalFilterResponseSpeed;
        SerializedDataParameter sampleCount, maxRayLength, thickness, binarySearchIterations, refineThickness, thicknessFine, decay, jitter, animatedJitter;
        SerializedDataParameter fresnel, fuzzyness, contactHardening, minimumBlur;
        SerializedDataParameter blurDownsampling, blurStrength, specularControl, specularSoftenPower, vignetteSize, vignettePower;
        SerializedDataParameter useReflectionsScripts, reflectionsScriptsLayerMask, skipDeferredPass;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceRaytracedReflection>(serializedObject);


            showInSceneView = Unpack(o.Find(x => x.showInSceneView));
            reflectionsMultiplier = Unpack(o.Find(x => x.intensity));
            reflectionsIntensityCurve = Unpack(o.Find(x => x.reflectionsIntensityCurve));
            reflectionsSmoothnessCurve = Unpack(o.Find(x => x.reflectionsSmoothnessCurve));
            computeBackFaces = Unpack(o.Find(x => x.computeBackFaces));
            // computeBackFacesLayerMask = Unpack(o.Find(x => x.computeBackFacesLayerMask));
            thicknessMinimum = Unpack(o.Find(x => x.thicknessMinimum));
            downsampling = Unpack(o.Find(x => x.downsampling));
            depthBias = Unpack(o.Find(x => x.depthBias));
            outputMode = Unpack(o.Find(x => x.outputMode));
            separationPos = Unpack(o.Find(x => x.separationPos));
            lowPrecision = Unpack(o.Find(x => x.isHDR));
            noiseTex = Unpack(o.Find(x => x.noiseTex));
            // stopNaN = Unpack(o.Find(x => x.stopNaN));
            // stencilCheck = Unpack(o.Find(x => x.stencilCheck));
            // stencilValue = Unpack(o.Find(x => x.stencilValue));
            // stencilCompareFunction = Unpack(o.Find(x => x.stencilCompareFunction));
            // temporalFilter = Unpack(o.Find(x => x.temporalFilter));
            // temporalFilterResponseSpeed = Unpack(o.Find(x => x.temporalFilterResponseSpeed));
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
        }

        public override void OnInspectorGUI()
        {

            PropertyField(reflectionsMultiplier, new GUIContent("Intensity"));
            PropertyField(showInSceneView);

            EditorGUILayout.Separator();
            // EditorGUILayout.LabelField("Quality Settings", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Apply Preset:", GUILayout.Width(EditorGUIUtility.labelWidth));
            ScreenSpaceRaytracedReflection ssr = (ScreenSpaceRaytracedReflection)target;
            if (GUILayout.Button("Fast"))
            {
                ssr.ApplyRaytracingPreset(ScreenSpaceRaytracedReflection.RaytracingPreset.Fast);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("Medium"))
            {
                ssr.ApplyRaytracingPreset(ScreenSpaceRaytracedReflection.RaytracingPreset.Medium);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("High"))
            {
                ssr.ApplyRaytracingPreset(ScreenSpaceRaytracedReflection.RaytracingPreset.High);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("Superb"))
            {
                ssr.ApplyRaytracingPreset(ScreenSpaceRaytracedReflection.RaytracingPreset.Superb);
                EditorUtility.SetDirty(target);
            }
            if (GUILayout.Button("Ultra"))
            {
                ssr.ApplyRaytracingPreset(ScreenSpaceRaytracedReflection.RaytracingPreset.Ultra);
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

            // PropertyField(temporalFilter);
            // if (temporalFilter.value.boolValue)
            // {
            //     EditorGUI.indentLevel++;
            //     PropertyField(temporalFilterResponseSpeed, new GUIContent("Response Speed"));
            //     EditorGUI.indentLevel--;
            // }

            PropertyField(downsampling);
            PropertyField(depthBias);
            PropertyField(noiseTex);

            EditorGUILayout.Separator();
            // EditorGUILayout.LabelField("Reflection Intensity", EditorStyles.miniLabel);


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
            if (outputMode.value.intValue == (int)ScreenSpaceRaytracedReflection.OutputMode.SideBySideComparison)
            {
                EditorGUI.indentLevel++;
                PropertyField(separationPos);
                EditorGUI.indentLevel--;
            }
            PropertyField(lowPrecision);
            // PropertyField(stopNaN, new GUIContent("Stop NaN"));
            // PropertyField(stencilCheck);
            // if (stencilCheck.value.boolValue)
            // {
            //     EditorGUI.indentLevel++;
            //     PropertyField(stencilValue, new GUIContent("Value"));
            //     PropertyField(stencilCompareFunction, new GUIContent("Compare Funciton"));
            //     EditorGUI.indentLevel--;
            // }
        }
    }
}
