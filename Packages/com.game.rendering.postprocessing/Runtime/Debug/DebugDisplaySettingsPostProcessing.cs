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
        public bool AreAnySettingsActive => true;

        public IDebugDisplaySettingsPanelDisposable CreatePanel()
        {
            return new SettingsPanel(this);
        }
        #endregion IDebugDisplaySettingsQuery




        public DebugFullScreenMode fullScreenDebugMode { get; set; } = DebugFullScreenMode.None;
        public int fullScreenDebugModeOutputSizeScreenPercent { get; set; } = 50;



        static class Strings
        {
            public static readonly NameAndTooltip MapOverlays = new() { name = "Map Overlays", tooltip = "Overlays render pipeline textures to validate the scene." };
            public static readonly NameAndTooltip MapSize = new() { name = "Map Size", tooltip = "Set the size of the render pipeline texture in the scene." };
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
                        WidgetFactory.CreateMapOverlaySize(this),
                    }
                }
                );
            }
        }
    }
}
