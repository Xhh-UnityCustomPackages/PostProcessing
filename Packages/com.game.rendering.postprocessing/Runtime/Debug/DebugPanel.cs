using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.Core.PostProcessing
{
    public class DebugPanel
    {
        static bool lightEnabled = true;

        [InitializeOnLoadMethod]
        static void OnEnable()
        {
            AddPanel(new DebugDisplaySettingsPostProcessing());
        }


        static void AddPanel(IDebugDisplaySettingsData data)
        {
            IDebugDisplaySettingsPanelDisposable disposeableSettingsPanel = data.CreatePanel();
            var panelWidgets = disposeableSettingsPanel.Widgets;

            var panel = DebugManager.instance.GetPanel(
                displayName: disposeableSettingsPanel.PanelName,
                createIfNull: true,
                groupIndex: (disposeableSettingsPanel is DebugDisplaySettingsPanel debugDisplaySettingsPanel) ? debugDisplaySettingsPanel.Order : 0);

            var panelChildren = panel.children;

            panel.flags = disposeableSettingsPanel.Flags;
            panelChildren.Add(panelWidgets);
        }


    }
}
