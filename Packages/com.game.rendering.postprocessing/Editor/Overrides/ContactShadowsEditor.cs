using UnityEditor;
using UnityEditor.Rendering;

namespace Game.Core.PostProcessing.UnityEditor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(ContactShadow))]
    public class ContactShadowsEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable;
        SerializedDataParameter m_Length;
        SerializedDataParameter m_DistanceScaleFactor;
        SerializedDataParameter m_MaxDistance;
        SerializedDataParameter m_MinDistance;
        SerializedDataParameter m_FadeDistance;
        SerializedDataParameter m_FadeInDistance;
        SerializedDataParameter m_SampleCount;
        SerializedDataParameter m_Opacity;
        SerializedDataParameter m_Bias;
        SerializedDataParameter m_Thickness;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ContactShadow>(serializedObject);

            m_Enable = Unpack(o.Find(x => x.enable));
            m_Length = Unpack(o.Find(x => x.length));
            m_DistanceScaleFactor = Unpack(o.Find(x => x.distanceScaleFactor));
            m_MaxDistance = Unpack(o.Find(x => x.maxDistance));
            m_MinDistance = Unpack(o.Find(x => x.minDistance));
            m_FadeDistance = Unpack(o.Find(x => x.fadeDistance));
            m_FadeInDistance = Unpack(o.Find(x => x.fadeInDistance));
            m_SampleCount = Unpack(o.Find(x => x.sampleCount));
            m_Opacity = Unpack(o.Find(x => x.opacity));
            m_Bias = Unpack(o.Find(x => x.rayBias));
            m_Thickness = Unpack(o.Find(x => x.thicknessScale));

            base.OnEnable();
        }
        
        

    }
}