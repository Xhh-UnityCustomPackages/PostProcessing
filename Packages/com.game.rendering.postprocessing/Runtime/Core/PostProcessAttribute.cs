using System;

namespace Game.Core.PostProcessing
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PostProcessAttribute : Attribute
    {
        //
        public string Name { get; }

        public PostProcessInjectionPoint InjectionPoint { get; }

        public SupportRenderPath SupportRenderPath { get; }

        public bool ShareInstance { get; }

        public PostProcessAttribute(string name, PostProcessInjectionPoint injectionPoint, SupportRenderPath supportRenderPath = SupportRenderPath.Deferred | SupportRenderPath.Forward, bool shareInstance = false)
        {
            Name = name;
            InjectionPoint = injectionPoint;
            SupportRenderPath = supportRenderPath;
            ShareInstance = shareInstance;
        }

        public static PostProcessAttribute GetAttribute(Type type)
        {
            if (type == null) return null;

            var attributes = type.GetCustomAttributes(typeof(PostProcessAttribute), false);
            return (attributes.Length != 0) ? (attributes[0] as PostProcessAttribute) : null;
        }
    }
}
