using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.Core.PostProcessing
{
    public class DebugHandler : IDisposable
    {
        DebugDisplaySettingsPostProcessing m_PostProcessingSetting;
        private PostProcessingDebugPass m_DebugPass;
        private StencilDebugPass m_StencilDebugPass;
        private ExposureDebugPass m_ExposureDebugPass;

        public DebugDisplaySettingsPostProcessing PostProcessingSetting => m_PostProcessingSetting;
        public bool AreAnySettingsActive => m_PostProcessingSetting.AreAnySettingsActive;

        public void Init(PostProcessData context)
        {
            m_PostProcessingSetting = AddPanel(new DebugDisplaySettingsPostProcessing());
            
            m_DebugPass = new PostProcessingDebugPass(this);

            ComputeShader cs = null;
#if UNITY_EDITOR
            var shaderPath = AssetDatabase.GUIDToAssetPath("1f6212cbaa876c447b940eda42123a9b");
            cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);
#endif
            m_StencilDebugPass = new StencilDebugPass(cs);

            ComputeShader debugImageHistogramCS = null;
            Shader debugExposure = null;

#if UNITY_EDITOR
            debugImageHistogramCS = AssetDatabase.LoadAssetAtPath<ComputeShader>(PostProcessingUtils.packagePath + "/Runtime/Overrides/Volumes/Exposure/Shaders/DebugHistogramImage.compute");
            debugExposure = AssetDatabase.LoadAssetAtPath<Shader>(PostProcessingUtils.packagePath + "/Runtime/Overrides/Volumes/Exposure/Shaders/DebugExposure.shader");
#endif
            m_ExposureDebugPass = new ExposureDebugPass(context, debugExposure, debugImageHistogramCS, m_PostProcessingSetting.exposureDebugSettings);
        }

        public void EnqueuePass(ScriptableRenderer renderer)
        {
            if (m_PostProcessingSetting.AreAnySettingsActive)
            {
                renderer.EnqueuePass(m_DebugPass);
            }

            if (m_PostProcessingSetting.enableStencilDebug)
            {
                m_StencilDebugPass.Setup(m_PostProcessingSetting.stencilDebugScale, m_PostProcessingSetting.stencilDebugMargin);
                renderer.EnqueuePass(m_StencilDebugPass);
            }

            if (m_PostProcessingSetting.exposureDebugSettings.exposureDebugMode != Exposure.ExposureDebugMode.None)
            {
                renderer.EnqueuePass(m_ExposureDebugPass);
            }
        }

        public void Dispose()
        {
            m_DebugPass?.Dispose();
            m_StencilDebugPass?.Dispose();
            m_ExposureDebugPass?.Dispose();
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
