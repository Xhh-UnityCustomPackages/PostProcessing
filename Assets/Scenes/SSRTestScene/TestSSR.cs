using System;
using System.Globalization;
using Game.Core.PostProcessing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class TestSSR : MonoBehaviour
{
    [SerializeField] private Toggle m_EnableToggle;
    [SerializeField] private Dropdown m_ModeDropdown;
    [SerializeField] private Toggle m_MipmapToggle;

    [SerializeField] private Slider m_StepSizeSlider;
    [SerializeField] private Text m_StepSizeText;
    
    [SerializeField] private Slider m_ThicknessSlider;
    [SerializeField] private Text m_ThicknessText;
    
    [SerializeField] private Slider m_IterationSlider;
    [SerializeField] private Text m_IterationText;
    
    [SerializeField] private Dropdown m_ResolutionDropdown;

    private ScreenSpaceReflection m_Settings;
    [SerializeField] private Volume m_Volume;
    private bool m_Init;
    
    void Start()
    {
        QualitySettings.vSyncCount = 0;          // 关闭垂直同步
        Application.targetFrameRate = -1;        // 不限制帧率
        
        foreach (var s in m_Volume.sharedProfile.components)
        {
            if (s is ScreenSpaceReflection ssr)
                m_Settings = ssr;
        }
        Init();
    }

    void Init()
    {
        if (m_Settings == null)
            return;

        if (m_Init) return;
        m_Init = true;
        m_EnableToggle.onValueChanged.AddListener(OnEnableToggleChanged);
        m_ModeDropdown.onValueChanged.AddListener(OnModeChanged);
        m_MipmapToggle.onValueChanged.AddListener(OnMipmapChanged);
        m_ResolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        m_StepSizeSlider.onValueChanged.AddListener(OnStepSizeValueChanged);
        m_ThicknessSlider.onValueChanged.AddListener(OnThicknessValueChanged);
        m_IterationSlider.onValueChanged.AddListener(OnIterationValueChanged);
        
        m_EnableToggle.isOn = m_Settings.Enable.value;
        m_ModeDropdown.value = (int)m_Settings.mode.value;
        m_MipmapToggle.isOn = m_Settings.enableMipmap.value;
        m_ResolutionDropdown.value = (int)m_Settings.resolution.value;

        m_StepSizeSlider.value = m_Settings.stepSize.value;
        m_StepSizeText.text = m_Settings.stepSize.value.ToString(CultureInfo.InvariantCulture);
        m_ThicknessSlider.value = m_Settings.thickness.value;
        m_ThicknessText.text = m_Settings.thickness.value.ToString(CultureInfo.InvariantCulture);
        m_IterationSlider.value = m_Settings.maximumIterationCount.value;
        m_IterationText.text = m_Settings.maximumIterationCount.value.ToString();
    }

    void OnEnableToggleChanged(bool isOn)
    {
        m_Settings.Enable.value = isOn;
    }

    void OnModeChanged(int index)
    {
        m_Settings.mode.value = (ScreenSpaceReflection.RaytraceModes)index;
    }

    void OnMipmapChanged(bool isOn)
    {
        m_Settings.enableMipmap.value = isOn;
    }
    
    void OnResolutionChanged(int index)
    {
        m_Settings.resolution.value = (ScreenSpaceReflection.Resolution)index;
    }

    void OnStepSizeValueChanged(float v)
    {
        m_Settings.stepSize.value = v;
        m_StepSizeText.text = m_Settings.stepSize.value.ToString(CultureInfo.InvariantCulture);
    }

    void OnThicknessValueChanged(float v)
    {
        m_Settings.thickness.value = v;
        m_ThicknessText.text = m_Settings.thickness.value.ToString(CultureInfo.InvariantCulture);
    }

    void OnIterationValueChanged(float v)
    {
        m_Settings.maximumIterationCount.value = (int)v;
        m_IterationText.text = m_Settings.maximumIterationCount.value.ToString(CultureInfo.InvariantCulture);
    }
}
