using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
        
        public bool enableStencilDebug { get; set; } = false;
        public float stencilDebugScale { get; set; } = 10;
        public float stencilDebugMargin { get; set; } = 0.25f;

        static class Strings
        {
            public static readonly NameAndTooltip MapOverlays = new() { name = "Map Overlays", tooltip = "Overlays render pipeline textures to validate the scene." };
            public static readonly NameAndTooltip MapSize = new() { name = "Map Size", tooltip = "Set the size of the render pipeline texture in the scene." };
            public static readonly NameAndTooltip HiZMipMapLevel = new() { name = "HiZ MipMap Level", tooltip = "Set the size of the render pipeline texture in the scene." };
            public static readonly NameAndTooltip StencilDebug = new() { name = "Stencil Debug", tooltip = "Stencil Debug." };
            public static readonly NameAndTooltip StencilDebugScale = new() { name = "Stencil Debug Scale", tooltip = "Stencil Debug." };
            public static readonly NameAndTooltip StencilDebugMargin = new() { name = "Stencil Debug Margin", tooltip = "Stencil Debug." };
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
            
            internal static DebugUI.Widget CreateStencilDebug(SettingsPanel panel) => new DebugUI.BoolField
            {
                nameAndTooltip = Strings.StencilDebug,
                getter = () => panel.data.enableStencilDebug,
                setter = (value) => panel.data.enableStencilDebug = value
            };
            
            internal static DebugUI.Widget CreateStencilDebugScale(SettingsPanel panel) => new DebugUI.Container
            {
                isHiddenCallback = () => !panel.data.enableStencilDebug,
                children =
                {
                    new DebugUI.FloatField
                    {
                        nameAndTooltip = Strings.StencilDebugScale,
                        getter = () => panel.data.stencilDebugScale,
                        setter = value => panel.data.stencilDebugScale = value,
                        incStep = 1,
                        min = () => 0,
                        max = () => 100
                    }
                }
            };
            
            internal static DebugUI.Widget CreateStencilDebugMargin(SettingsPanel panel) => new DebugUI.Container
            {
                isHiddenCallback = () => !panel.data.enableStencilDebug,
                children =
                {
                    new DebugUI.FloatField
                    {
                        nameAndTooltip = Strings.StencilDebugMargin,
                        getter = () => panel.data.stencilDebugMargin,
                        setter = value => panel.data.stencilDebugMargin = value,
                        min = () => 0,
                        max = () => 1
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
                        WidgetFactory.CreateStencilDebug(this),
                        WidgetFactory.CreateStencilDebugScale(this),
                        WidgetFactory.CreateStencilDebugMargin(this),
                    }
                });
                
            }
        }
    }
}
