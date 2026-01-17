using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using NameAndTooltip = UnityEngine.Rendering.DebugUI.Widget.NameAndTooltip;

namespace Game.Core.PostProcessing
{
    public class DebugDisplaySettingsPostProcessing : IDebugDisplaySettingsData
    {

        #region IDebugDisplaySettingsQuery

        public bool TryGetScreenClearColor(ref Color color)
        {
            return false;
        }

        public bool AreAnySettingsActive => (fullScreenDebugMode != DebugFullScreenMode.None);
        public bool IsPostProcessingAllowed => true;
        public bool IsLightingActive => true;

        public IDebugDisplaySettingsPanelDisposable CreatePanel()
        {
            return new SettingsPanel(this);
        }
        #endregion IDebugDisplaySettingsQuery
        

        public DebugFullScreenMode fullScreenDebugMode { get; set; } = DebugFullScreenMode.None;
        public int fullScreenDebugModeOutputSizeScreenPercent { get; set; } = 50;
        public int hiZMipmapLevel { get; set; } = 0;
        

        public StencilDebugSettings stencilDebugSettings = new();
        public ExposureDebugSettings exposureDebugSettings = new();

        static class Strings
        {
            public static readonly NameAndTooltip MapOverlays = new() { name = "Map Overlays", tooltip = "Overlays render pipeline textures to validate the scene." };
            public static readonly NameAndTooltip MapSize = new() { name = "Map Size", tooltip = "Set the size of the render pipeline texture in the scene." };
            public static readonly NameAndTooltip HiZMipMapLevel = new() { name = "HiZ MipMap Level", tooltip = "Set the size of the render pipeline texture in the scene." };
            
            public static readonly NameAndTooltip StencilDebug = new() { name = "Stencil Debug", tooltip = "Stencil Debug." };
            public static readonly NameAndTooltip StencilDebugScale = new() { name = "Stencil Debug Scale", tooltip = "Stencil Debug." };
            public static readonly NameAndTooltip StencilDebugMargin = new() { name = "Stencil Debug Margin", tooltip = "Stencil Debug." };
            
            public static readonly NameAndTooltip Exposure = new() { name = "Exposure", tooltip = "Allows the selection of an Exposure debug mode to use." };
            public static readonly NameAndTooltip ExposureDebugMode = new() { name = "DebugMode", tooltip = "Use the drop-down to select a debug mode to validate the exposure." };
            public static readonly NameAndTooltip ExposureDisplayMaskOnly = new() { name = "Display Mask Only", tooltip = "Display only the metering mask in the picture-in-picture. When disabled, the mask is visible after weighting the scene color instead." };
            public static readonly NameAndTooltip ExposureShowTonemapCurve = new() { name = "Show Tonemap Curve", tooltip = "Overlay the tonemap curve to the histogram debug view." };
            public static readonly NameAndTooltip DisplayHistogramSceneOverlay = new () { name = "Show Scene Overlay", tooltip = "Display the scene overlay showing pixels excluded by the exposure computation via histogram." };
            public static readonly NameAndTooltip ExposureCenterAroundExposure = new() { name = "Center Around Exposure", tooltip = "Center the histogram around the current exposure value." };
            public static readonly NameAndTooltip ExposureDisplayRGBHistogram = new() { name = "Display RGB Histogram", tooltip = "Display the Final Image Histogram as an RGB histogram instead of just luminance." };
            public static readonly NameAndTooltip DebugExposureCompensation = new() { name = "Debug Exposure Compensation", tooltip = "Set an additional exposure on top of your current exposure for debug purposes." };

        }

        internal static class WidgetFactory
        {
            internal static DebugUI.Widget CreateMapOverlays(SettingsPanel panel) => new DebugUI.EnumField
            {
                nameAndTooltip = Strings.MapOverlays,
                autoEnum = typeof(DebugFullScreenMode),
                getter = () => (int)panel.data.fullScreenDebugMode,
                setter = (value) => panel.data.fullScreenDebugMode = (DebugFullScreenMode)value,
                getIndex = () => (int)panel.data.fullScreenDebugMode,
                setIndex = (value) => panel.data.fullScreenDebugMode = (DebugFullScreenMode)value
            };

            internal static DebugUI.Widget CreateMapOverlaySize(SettingsPanel panel) => new DebugUI.Container()
            {
                isHiddenCallback = () => panel.data.fullScreenDebugMode == DebugFullScreenMode.None,
                children =
                {
                    new DebugUI.IntField
                    {
                        nameAndTooltip = Strings.MapSize,
                        getter = () => panel.data.fullScreenDebugModeOutputSizeScreenPercent,
                        setter = value => panel.data.fullScreenDebugModeOutputSizeScreenPercent = value,
                        incStep = 10,
                        min = () => 0,
                        max = () => 100
                    }
                }
            };

            internal static DebugUI.Widget CreateHiZMipmapLevel(SettingsPanel panel) => new DebugUI.Container()
            {
                isHiddenCallback = () => panel.data.fullScreenDebugMode != DebugFullScreenMode.HiZ,
                children =
                {
                    new DebugUI.IntField
                    {
                        nameAndTooltip = Strings.HiZMipMapLevel,
                        getter = () => panel.data.hiZMipmapLevel,
                        setter = value => panel.data.hiZMipmapLevel = value,
                        incStep = 1,
                        min = () => 0,
                        max = () => 10
                    }
                }

            };
            
            internal static DebugUI.Widget CreateStencilDebug(SettingsPanel panel) => new DebugUI.Container
            {
                children =
                {
                    new DebugUI.BoolField()
                    {
                        nameAndTooltip = Strings.StencilDebug,
                        getter = () => panel.data.stencilDebugSettings.enableStencilDebug,
                        setter = (value) => panel.data.stencilDebugSettings.enableStencilDebug = value
                    },
                    new DebugUI.FloatField
                    {
                        nameAndTooltip = Strings.StencilDebugScale,
                        getter = () => panel.data.stencilDebugSettings.stencilDebugScale,
                        setter = value => panel.data.stencilDebugSettings.stencilDebugScale = value,
                        incStep = 1,
                        min = () => 0,
                        max = () => 100,
                        isHiddenCallback = () => !panel.data.stencilDebugSettings.enableStencilDebug,
                    },
                    new DebugUI.FloatField
                    {
                        nameAndTooltip = Strings.StencilDebugMargin,
                        getter = () => panel.data.stencilDebugSettings.stencilDebugMargin,
                        setter = value => panel.data.stencilDebugSettings.stencilDebugMargin = value,
                        min = () => 0,
                        max = () => 1,
                        isHiddenCallback = () => !panel.data.stencilDebugSettings.enableStencilDebug,
                    }
                }
            };

            internal static DebugUI.Widget CreateExposureDebug(SettingsPanel panel) => new DebugUI.Container
            {
                isHiddenCallback = () => false,
                children =
                {
                    new DebugUI.EnumField
                    {
                        nameAndTooltip = Strings.ExposureDebugMode,
                        getter = () => (int)panel.data.exposureDebugSettings.exposureDebugMode,
                        setter = value => panel.data.exposureDebugSettings.exposureDebugMode = (Exposure.ExposureDebugMode)value,
                        autoEnum = typeof(Exposure.ExposureDebugMode),
                        getIndex = () => (int)panel.data.exposureDebugSettings.exposureDebugMode,
                        setIndex = value => panel.data.exposureDebugSettings.exposureDebugMode = (Exposure.ExposureDebugMode)value,
                    },
                    new DebugUI.BoolField()
                    {
                        nameAndTooltip = Strings.ExposureDisplayMaskOnly,
                        getter = () => panel.data.exposureDebugSettings.displayMaskOnly,
                        setter = value => panel.data.exposureDebugSettings.displayMaskOnly = value,
                        isHiddenCallback = () => panel.data.exposureDebugSettings.exposureDebugMode != Exposure.ExposureDebugMode.MeteringWeighted
                    },
                    new DebugUI.Container()
                    {
                        isHiddenCallback = () => panel.data.exposureDebugSettings.exposureDebugMode != Exposure.ExposureDebugMode.HistogramView,
                        children =
                        {
                            new DebugUI.BoolField()
                            {
                                nameAndTooltip = Strings.DisplayHistogramSceneOverlay,
                                getter = () => panel.data.exposureDebugSettings.displayOnSceneOverlay,
                                setter = value => panel.data.exposureDebugSettings.displayOnSceneOverlay = value
                            },
                            new DebugUI.BoolField()
                            {
                                nameAndTooltip = Strings.ExposureShowTonemapCurve,
                                getter = () => panel.data.exposureDebugSettings.showTonemapCurveAlongHistogramView,
                                setter = value => panel.data.exposureDebugSettings.showTonemapCurveAlongHistogramView = value
                            },
                            new DebugUI.BoolField()
                            {
                                nameAndTooltip = Strings.ExposureCenterAroundExposure,
                                getter = () => panel.data.exposureDebugSettings.centerHistogramAroundMiddleGrey,
                                setter = value => panel.data.exposureDebugSettings.centerHistogramAroundMiddleGrey = value
                            }
                        }
                    },
                    new DebugUI.BoolField()
                    {
                        nameAndTooltip = Strings.ExposureDisplayRGBHistogram,
                        getter = () => panel.data.exposureDebugSettings.displayFinalImageHistogramAsRGB,
                        setter = value => panel.data.exposureDebugSettings.displayFinalImageHistogramAsRGB = value,
                        isHiddenCallback = () => panel.data.exposureDebugSettings.exposureDebugMode != Exposure.ExposureDebugMode.FinalImageHistogramView
                    },
                    new DebugUI.FloatField
                    {
                        nameAndTooltip = Strings.DebugExposureCompensation,
                        getter = () => panel.data.exposureDebugSettings.debugExposure,
                        setter = value => panel.data.exposureDebugSettings.debugExposure = value
                    }
                }
            };
        }

        [DisplayInfo(name = "Custom Post-Processing", order = 10)]
        internal class SettingsPanel : DebugDisplaySettingsPanel<DebugDisplaySettingsPostProcessing>
        {
            public SettingsPanel(DebugDisplaySettingsPostProcessing data) : base(data)
            {
                AddWidget(new DebugUI.Foldout
                {
                    displayName = "Custom Post-Processing Debug",
                    flags = DebugUI.Flags.FrequentlyUsed,
                    isHeader = true,
                    opened = true,
                    children =
                    {
                        WidgetFactory.CreateMapOverlays(this),
                        WidgetFactory.CreateHiZMipmapLevel(this),
                        WidgetFactory.CreateMapOverlaySize(this),
                    }
                });
                
                AddWidget(new DebugUI.Foldout
                {
                    displayName = "Stencil Debug",
                    flags = DebugUI.Flags.FrequentlyUsed,
                    isHeader = true,
                    opened = true,
                    children =
                    {
                        WidgetFactory.CreateStencilDebug(this),
                    }
                });
                
                AddWidget(new DebugUI.Foldout
                {
                    displayName = "Expose",
                    flags = DebugUI.Flags.FrequentlyUsed,
                    isHeader = true,
                    opened = true,
                    children =
                    {
                        WidgetFactory.CreateExposureDebug(this),
                    }
                });
            }
        }
    }
}
