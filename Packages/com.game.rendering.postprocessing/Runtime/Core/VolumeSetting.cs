using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{

    [Serializable]
    public abstract class VolumeSetting : VolumeComponent, IPostProcessComponent
    {
        // 只是为了隐式处理点击总的选项开关功能
        [HideInInspector]
        public BoolParameter enabled = new (true, true);
        public abstract bool IsActive();
        public bool IsTileCompatible() => false;
    }

    public abstract class PostProcessVolumeRenderer<T> : PostProcessRenderer where T : VolumeSetting
    {
        protected T settings => VolumeManager.instance.stack.GetComponent<T>();
        public override bool IsActive(ref RenderingData renderingData) => settings.active && settings.enabled.overrideState && settings.IsActive();
    }
}
