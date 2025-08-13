using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Diagnostics;

namespace Game.Core.PostProcessing
{
    [Serializable, DebuggerDisplay(k_DebuggerDisplay)]
    public sealed class EnumParameter<T> : VolumeParameter<T>
    {
        /// <summary>
        /// Creates a new <see cref="EnumParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public EnumParameter(T value, bool overrideState = false) : base(value, overrideState) { }
    }
}
