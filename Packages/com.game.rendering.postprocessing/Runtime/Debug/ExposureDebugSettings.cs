using System;

namespace Game.Core.PostProcessing
{
    [Serializable]
    public class ExposureDebugSettings
    {
        /// <summary>Exposure debug mode.</summary>
        public Exposure.ExposureDebugMode exposureDebugMode = Exposure.ExposureDebugMode.None;
        /// <summary>Exposure compensation to apply on current scene exposure.</summary>
        public float debugExposure = 0.0f;
        /// <summary>Whether to show tonemap curve in the histogram debug view or not.</summary>
        public bool showTonemapCurveAlongHistogramView = true;
        /// <summary>Whether to center the histogram debug view around the middle-grey point or not.</summary>
        public bool centerHistogramAroundMiddleGrey = false;
        /// <summary>Whether to show tonemap curve in the histogram debug view or not.</summary>
        public bool displayFinalImageHistogramAsRGB = false;
        /// <summary>Whether to show the only the mask in the picture in picture. If unchecked, the mask view is weighted by the scene color.</summary>
        public bool displayMaskOnly = false;
        /// <summary>Whether to show the on scene overlay displaying pixels excluded by the exposure computation via histogram.</summary>
        public bool displayOnSceneOverlay = true;
    }
}