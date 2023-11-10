using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public class DebugHandler
    {

        DebugDisplaySettingsPostProcessing m_PostProcessingSetting;


        public DebugDisplaySettingsPostProcessing PostProcessingSetting => m_PostProcessingSetting;


        public bool AreAnySettingsActive => m_PostProcessingSetting.AreAnySettingsActive;

        public void Init()
        {
            m_PostProcessingSetting = AddPanel(new DebugDisplaySettingsPostProcessing());
        }

        T AddPanel<T>(T data) where T : IDebugDisplaySettingsData
        {
            IDebugDisplaySettingsPanelDisposable disposeableSettingsPanel = data.CreatePanel();
            var panelWidgets = disposeableSettingsPanel.Widgets;


            DebugManager.instance.RemovePanel(disposeableSettingsPanel.PanelName);

            var panel = DebugManager.instance.GetPanel(
                displayName: disposeableSettingsPanel.PanelName,
                createIfNull: true,
                groupIndex: (disposeableSettingsPanel is DebugDisplaySettingsPanel debugDisplaySettingsPanel) ? debugDisplaySettingsPanel.Order : 0);

            var panelChildren = panel.children;

            panel.flags = disposeableSettingsPanel.Flags;
            panelChildren.Add(panelWidgets);

            return data;
        }



        internal bool TryGetFullscreenDebugMode(out DebugFullScreenMode debugFullScreenMode)
        {
            return TryGetFullscreenDebugMode(out debugFullScreenMode, out _);
        }

        internal bool TryGetFullscreenDebugMode(out DebugFullScreenMode debugFullScreenMode, out int textureHeightPercent)
        {
            debugFullScreenMode = m_PostProcessingSetting.fullScreenDebugMode;
            textureHeightPercent = m_PostProcessingSetting.fullScreenDebugModeOutputSizeScreenPercent;
            return debugFullScreenMode != DebugFullScreenMode.None;
        }
    }
}
