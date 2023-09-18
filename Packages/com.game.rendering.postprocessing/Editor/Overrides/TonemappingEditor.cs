using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor;
using Game.Core.PostProcessing;


namespace Game.Core.PostProcessing.UnityEditor
{
    [CustomEditor(typeof(Tonemapping))]
    public class TonemappingEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Mode;

        // ACES
        SerializedDataParameter m_Unreal;
        SerializedDataParameter m_BlurCorrection;
        SerializedDataParameter m_ExpandGamut;
        SerializedDataParameter m_Curve;
        SerializedDataParameter m_FilmSlope;
        SerializedDataParameter m_FilmToe;
        SerializedDataParameter m_FilmShoulder;
        SerializedDataParameter m_FilmBlackClip;
        SerializedDataParameter m_FilmWhiteClip;


        public override void OnEnable()
        {
            var o = new PropertyFetcher<Tonemapping>(serializedObject);

            m_Mode = Unpack(o.Find(x => x.mode));

            m_Unreal = Unpack(o.Find(x => x.unreal));
            m_BlurCorrection = Unpack(o.Find(x => x.blueCorrection));
            m_ExpandGamut = Unpack(o.Find(x => x.expandGamut));
            m_Curve = Unpack(o.Find(x => x.curve));
            m_FilmSlope = Unpack(o.Find(x => x.filmSlope));
            m_FilmToe = Unpack(o.Find(x => x.filmToe));
            m_FilmShoulder = Unpack(o.Find(x => x.filmShoulder));
            m_FilmBlackClip = Unpack(o.Find(x => x.filmBlackClip));
            m_FilmWhiteClip = Unpack(o.Find(x => x.filmWhiteClip));
        }


        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode);


            if (m_Mode.value.GetEnumValue<Tonemapping.TonemappingMode>() == Tonemapping.TonemappingMode.ACES)
            {
                EditorGUILayout.Space();
                PropertyField(m_Unreal);
                if (m_Unreal.value.boolValue)
                {
                    PropertyField(m_BlurCorrection);
                    PropertyField(m_ExpandGamut);
                    PropertyField(m_Curve);
                    if (m_Curve.value.GetEnumValue<Tonemapping.TonemappingCurve>() == Tonemapping.TonemappingCurve.UnrealFilmic)
                    {
                        PropertyField(m_FilmSlope);
                        PropertyField(m_FilmToe);
                        PropertyField(m_FilmShoulder);
                        PropertyField(m_FilmBlackClip);
                        PropertyField(m_FilmWhiteClip);
                    }
                }
            }

        }
    }
}
