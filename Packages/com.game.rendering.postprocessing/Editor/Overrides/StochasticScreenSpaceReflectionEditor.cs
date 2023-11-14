using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor;
using Game.Core.PostProcessing;


namespace Game.Core.PostProcessing.UnityEditor
{
    [CustomEditor(typeof(StochasticScreenSpaceReflection))]
    public class StochasticScreenSpaceReflectionEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enable;
        SerializedDataParameter m_Resolution;
        SerializedDataParameter m_TraceMethod;
        SerializedDataParameter m_RayNums;
        SerializedDataParameter m_BRDFBias;
        SerializedDataParameter m_Thickness;
        SerializedDataParameter m_ScreenFade;

        SerializedDataParameter m_HiZ_RaySteps;
        SerializedDataParameter m_HiZ_MaxLevel;
        SerializedDataParameter m_HiZ_StartLevel;
        SerializedDataParameter m_HiZ_StopLevel;


        SerializedDataParameter m_Linear_TraceBehind;
        SerializedDataParameter m_Linear_TowardRay;
        SerializedDataParameter m_Linear_RayDistance;
        SerializedDataParameter m_Linear_RaySteps;
        SerializedDataParameter m_Linear_StepSize;


        SerializedDataParameter m_BlueNoise_LUT;
        SerializedDataParameter m_PreintegratedGF_LUT;
        SerializedDataParameter m_SpatioSampler;
        SerializedDataParameter m_TemporalWeight;
        SerializedDataParameter m_TemporalScale;

        SerializedDataParameter m_DebugMode;


        public override void OnEnable()
        {
            var o = new PropertyFetcher<StochasticScreenSpaceReflection>(serializedObject);

            m_Enable = Unpack(o.Find(x => x.enableMode));

            m_Resolution = Unpack(o.Find(x => x.resolution));
            m_TraceMethod = Unpack(o.Find(x => x.TraceMethod));
            m_RayNums = Unpack(o.Find(x => x.RayNums));
            m_BRDFBias = Unpack(o.Find(x => x.BRDFBias));
            m_Thickness = Unpack(o.Find(x => x.Thickness));
            m_ScreenFade = Unpack(o.Find(x => x.ScreenFade));

            m_HiZ_RaySteps = Unpack(o.Find(x => x.HiZ_RaySteps));
            m_HiZ_MaxLevel = Unpack(o.Find(x => x.HiZ_MaxLevel));
            m_HiZ_StartLevel = Unpack(o.Find(x => x.HiZ_StartLevel));
            m_HiZ_StopLevel = Unpack(o.Find(x => x.HiZ_StopLevel));

            m_Linear_TraceBehind = Unpack(o.Find(x => x.Linear_TraceBehind));
            m_Linear_TowardRay = Unpack(o.Find(x => x.Linear_TowardRay));
            m_Linear_RayDistance = Unpack(o.Find(x => x.Linear_RayDistance));
            m_Linear_RaySteps = Unpack(o.Find(x => x.Linear_RaySteps));
            m_Linear_StepSize = Unpack(o.Find(x => x.Linear_StepSize));

            m_BlueNoise_LUT = Unpack(o.Find(x => x.BlueNoise_LUT));
            m_PreintegratedGF_LUT = Unpack(o.Find(x => x.PreintegratedGF_LUT));
            m_SpatioSampler = Unpack(o.Find(x => x.SpatioSampler));
            m_TemporalWeight = Unpack(o.Find(x => x.TemporalWeight));
            m_TemporalScale = Unpack(o.Find(x => x.TemporalScale));

            m_DebugMode = Unpack(o.Find(x => x.debugMode));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable);

            PropertyField(m_Resolution);
            PropertyField(m_RayNums);
            PropertyField(m_BRDFBias);
            PropertyField(m_Thickness);
            PropertyField(m_ScreenFade);
            PropertyField(m_TraceMethod);

            if (m_TraceMethod.value.GetEnumValue<StochasticScreenSpaceReflection.TraceApprox>() == StochasticScreenSpaceReflection.TraceApprox.HiZTrace)
            {
                PropertyField(m_HiZ_RaySteps);
                PropertyField(m_HiZ_MaxLevel);
                PropertyField(m_HiZ_StartLevel);
                PropertyField(m_HiZ_StopLevel);
            }
            else
            {
                PropertyField(m_Linear_TraceBehind);
                PropertyField(m_Linear_TowardRay);
                PropertyField(m_Linear_RayDistance);
                PropertyField(m_Linear_RaySteps);
                PropertyField(m_Linear_StepSize);
            }

            PropertyField(m_BlueNoise_LUT);
            PropertyField(m_PreintegratedGF_LUT);
            PropertyField(m_SpatioSampler);
            PropertyField(m_TemporalWeight);
            PropertyField(m_TemporalScale);

            PropertyField(m_DebugMode);
        }
    }
}
