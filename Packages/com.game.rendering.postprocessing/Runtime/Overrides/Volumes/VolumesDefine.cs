using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;

namespace Game.Core.PostProcessing
{
    public enum EnableMode
    {
        Enable,
        Disable
    }


    [Serializable]
    public class EnableModeParameter : VolumeParameter<EnableMode>
    {
        public EnableModeParameter(EnableMode value, bool overrideState = false) : base(value, overrideState) { }
    }
}
