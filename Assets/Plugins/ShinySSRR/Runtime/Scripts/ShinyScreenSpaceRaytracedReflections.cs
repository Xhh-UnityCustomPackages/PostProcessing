/// <summary>
/// Shiny SSRR - Screen Space Reflections for URP - (c) 2021-2022 Kronnect
/// </summary>

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShinySSRR
{

    public enum OutputMode
    {
        Final,
        OnlyReflections,
        SideBySideComparison,
        DebugDepth = 10,
        DebugDeferredNormals = 11
    }

    public enum RaytracingPreset
    {
        Fast = 10,
        Medium = 20,
        High = 30,
        Superb = 35,
        Ultra = 40
    }

    [ExecuteInEditMode, VolumeComponentMenu("Kronnect/Shiny SSRR")]
    public class ShinyScreenSpaceRaytracedReflections : VolumeComponent, IPostProcessComponent
    {

        [Header("General Settings")]

        [Tooltip("Reflection multiplier")]
        public ClampedFloatParameter reflectionsMultiplier = new ClampedFloatParameter(0f, 0, 2f);

        [Tooltip("Show reflections in SceneView window")]
        public BoolParameter showInSceneView = new BoolParameter(true);


        [Serializable] public sealed class OutputModeParameter : VolumeParameter<OutputMode> { }

        [Tooltip("Max number of samples used during the raymarch loop")]
        public ClampedIntParameter sampleCount = new ClampedIntParameter(16, 4, 128);

        [Tooltip("Maximum reflection distance")]
        public FloatParameter maxRayLength = new FloatParameter(24f);

        [Tooltip("Assumed thickness of geometry in the depth buffer before binary search")]
        public FloatParameter thickness = new FloatParameter(0.3f);

        [Tooltip("Number of refinements steps when a reflection hit is found")]
        public ClampedIntParameter binarySearchIterations = new ClampedIntParameter(6, 0, 16);

        [Tooltip("Increase accuracy of reflection hit after binary search by discarding points further than a reduced thickness.")]
        public BoolParameter refineThickness = new BoolParameter(false);

        [Tooltip("Assumed thickness of geometry in the depth buffer after binary search")]
        public ClampedFloatParameter thicknessFine = new ClampedFloatParameter(0.05f, 0.005f, 1f);

        [Tooltip("Jitter helps smoothing edges")]
        public ClampedFloatParameter jitter = new ClampedFloatParameter(0.3f, 0, 1f);

        [Tooltip("Animates jitter every frame")]
        public BoolParameter animatedJitter = new BoolParameter(false);

        [Tooltip("Performs a depth pre-pass to compute true thickness instead of assuming a fixed thickness for all geometry. This option can be expensive since it requires drawing the back depth of all opaque objects in the view frustum. You may need to increase the sample count as well.")]
        public BoolParameter computeBackFaces = new BoolParameter(false);

        [Tooltip("Minimum allowed thickness for any geometry.")]
        public ClampedFloatParameter thicknessMinimum = new ClampedFloatParameter(0.16f, 0.1f, 1f);

        [Tooltip("Which objects should be included in the back-faces depth pass.")]
        public LayerMaskParameter computeBackFacesLayerMask = new LayerMaskParameter(-1);

        [Tooltip("Downsampling multiplier applied to the final blurred reflections")]
        public ClampedIntParameter downsampling = new ClampedIntParameter(1, 1, 8);

        [Tooltip("Bias applied to depth checking. Increase if reflections desappear at the distance")]
        public FloatParameter depthBias = new FloatParameter(0.03f);

        [Tooltip("Enables temporal filter which reduces flickering")]
        public BoolParameter temporalFilter = new BoolParameter(false);

        [Tooltip("Temporal filter response speed determines how fast the history buffer is discarded")]
        public FloatParameter temporalFilterResponseSpeed = new FloatParameter(1f);


        [Tooltip("Reflection intensity mapping curve.")]
        public AnimationCurveParameter reflectionsIntensityCurve = new AnimationCurveParameter(AnimationCurve.Linear(0, 0, 1, 1));

        [Tooltip("Reflection smooothness mapping curve.")]
        public AnimationCurveParameter reflectionsSmoothnessCurve = new AnimationCurveParameter(new AnimationCurve(new Keyframe(0, 0f, 0, 0.166666f), new Keyframe(0.5f, 0.25f, 0.833333f, 1.166666f), new Keyframe(1, 1f, 1.833333f, 0)));

        [Tooltip("Reduces reflection based on view angle")]
        public ClampedFloatParameter fresnel = new ClampedFloatParameter(0.75f, 0, 1f);

        [Tooltip("Reflection decay with distance to reflective point")]
        public FloatParameter decay = new FloatParameter(2f);

        [Tooltip("Reduces intensity of specular reflections")]
        public BoolParameter specularControl = new BoolParameter(true);

        [Min(0), Tooltip("Power of the specular filter")]
        public FloatParameter specularSoftenPower = new FloatParameter(15f);


        [Tooltip("Controls the attenuation range of effect on screen borders")]
        public ClampedFloatParameter vignetteSize = new ClampedFloatParameter(1.1f, 0.5f, 2f);

        [Tooltip("Controls the attenuation gradient of effect on screen borders")]
        public ClampedFloatParameter vignettePower = new ClampedFloatParameter(1.5f, 0.1f, 10f);

        [Header("Reflection Sharpness")]

        [Tooltip("Ray dispersion with distance")]
        public FloatParameter fuzzyness = new FloatParameter(0);

        [Tooltip("Makes sharpen reflections near objects")]
        public FloatParameter contactHardening = new FloatParameter(0);

        [Tooltip("Minimum blur to be applied")]
        public ClampedFloatParameter minimumBlur = new ClampedFloatParameter(0.25f, 0, 4f);

        [Tooltip("Downsampling multiplier applied to the blur")]
        public ClampedIntParameter blurDownsampling = new ClampedIntParameter(1, 1, 8);

        [Tooltip("Custom directional blur strength")]
        public Vector2Parameter blurStrength = new Vector2Parameter(Vector2.one);

        [Header("Advanced Options")]
        [Tooltip("Show final result / debug view or compare view")]
        public OutputModeParameter outputMode = new OutputModeParameter { value = OutputMode.Final };

        [Tooltip("Position of the dividing line")]
        public ClampedFloatParameter separationPos = new ClampedFloatParameter(0.5f, -0.01f, 1.01f);

        [Tooltip("HDR reflections")]
        public BoolParameter lowPrecision = new BoolParameter(false);

        [Tooltip("Prevents out of range colors when composing reflections in the destination buffer. This operation performs a ping-pong copy of the frame buffer which can be expensive. Use only if required.")]
        public BoolParameter stopNaN = new BoolParameter(false);

        [Tooltip("Enables stencil check during GI composition. This option let you exclude GI over certain objects that also use stencil buffer.")]
        public BoolParameter stencilCheck = new BoolParameter(false);

        public IntParameter stencilValue = new IntParameter(1);

        [Serializable] public sealed class CompareFunctionParameter : VolumeParameter<CompareFunction> { }

        public CompareFunctionParameter stencilCompareFunction = new CompareFunctionParameter { value = CompareFunction.NotEqual };

        [Tooltip("Uses any reflection script when rendering in deferred rendering path. Usually, when rendering in deferred, you don't need to add Reflections scripts to gameobjects to get reflections. However, some effects can be achieved on transparent objects like puddles by adding a Reflections script on them.")]
        public BoolParameter useReflectionsScripts = new BoolParameter(false);

        [Tooltip("Which reflections scripts can be used when in deferred.")]
        public LayerMaskParameter reflectionsScriptsLayerMask = new LayerMaskParameter(-1);

        [Tooltip("In deferred, skips full screen deferred pass. This way you can just compute reflections on surfaces with Reflections scripts.")]
        public BoolParameter skipDeferredPass = new BoolParameter(false);

        public bool IsActive() => reflectionsMultiplier.value > 0;

        public bool IsTileCompatible() => true;

        public static float metallicGradientCachedId, smoothnessGradientCachedId;

        private void OnValidate()
        {
            decay.value = Mathf.Max(1f, decay.value);
            maxRayLength.value = Mathf.Max(0.1f, maxRayLength.value);
            fuzzyness.value = Mathf.Max(0, fuzzyness.value);
            thickness.value = Mathf.Max(0.01f, thickness.value);
            thicknessFine.value = Mathf.Max(0.01f, thicknessFine.value);
            contactHardening.value = Mathf.Max(0, contactHardening.value);

            Vector2 blurStrength = this.blurStrength.value;
            blurStrength.x = Mathf.Max(blurStrength.x, 0f);
            blurStrength.y = Mathf.Max(blurStrength.y, 0f);
            this.blurStrength.value = blurStrength;
            temporalFilterResponseSpeed.value = Mathf.Max(0, temporalFilterResponseSpeed.value);
            metallicGradientCachedId = smoothnessGradientCachedId = -1;
        }


        public void ApplyRaytracingPreset(RaytracingPreset preset)
        {
            switch (preset)
            {
                case RaytracingPreset.Fast:
                    sampleCount.Override(16);
                    maxRayLength.Override(6);
                    binarySearchIterations.Override(4);
                    downsampling.Override(3);
                    thickness.Override(0.5f);
                    refineThickness.Override(false);
                    jitter.Override(0.3f);
                    temporalFilter.Override(false);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.Medium:
                    sampleCount.Override(24);
                    maxRayLength.Override(12);
                    binarySearchIterations.Override(5);
                    downsampling.Override(2);
                    refineThickness.Override(false);
                    temporalFilter.Override(false);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.High:
                    sampleCount.Override(48);
                    maxRayLength.Override(24);
                    binarySearchIterations.Override(6);
                    downsampling.Override(1);
                    refineThickness.Override(false);
                    thicknessFine.Override(0.05f);
                    temporalFilter.Override(false);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.Superb:
                    sampleCount.Override(88);
                    maxRayLength.Override(48);
                    binarySearchIterations.Override(7);
                    downsampling.Override(1);
                    refineThickness.Override(true);
                    thicknessFine.Override(0.02f);
                    temporalFilter.Override(true);
                    computeBackFaces.Override(false);
                    break;
                case RaytracingPreset.Ultra:
                    sampleCount.Override(128);
                    maxRayLength.Override(64);
                    binarySearchIterations.Override(8);
                    downsampling.Override(1);
                    refineThickness.Override(true);
                    thicknessFine.Override(0.02f);
                    temporalFilter.Override(true);
                    computeBackFaces.Override(true);
                    break;
            }
            Reflections.needUpdateMaterials = true;
        }

    }

}
