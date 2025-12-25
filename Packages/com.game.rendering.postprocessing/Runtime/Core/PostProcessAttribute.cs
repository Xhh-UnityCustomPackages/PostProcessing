using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace Game.Core.PostProcessing
{
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PostProcessAttribute : System.Attribute
    {
        readonly string name;

        readonly PostProcessInjectionPoint injectionPoint;
        readonly SupportRenderPath supportRenderPath;

        readonly bool shareInstance;
        //
        public string Name => name;

        public PostProcessInjectionPoint InjectionPoint => injectionPoint;
        public SupportRenderPath SupportRenderPath => supportRenderPath;

        public bool ShareInstance => shareInstance;

        public PostProcessAttribute(string name, PostProcessInjectionPoint injectionPoint, SupportRenderPath supportRenderPath = SupportRenderPath.Deferred | SupportRenderPath.Forward, bool shareInstance = false)
        {
            this.name = name;
            this.injectionPoint = injectionPoint;
            this.supportRenderPath = supportRenderPath;
            this.shareInstance = shareInstance;
        }

        public static PostProcessAttribute GetAttribute(Type type)
        {
            if (type == null) return null;

            var atttributes = type.GetCustomAttributes(typeof(PostProcessAttribute), false);
            return (atttributes.Length != 0) ? (atttributes[0] as PostProcessAttribute) : null;
        }
    }
}
