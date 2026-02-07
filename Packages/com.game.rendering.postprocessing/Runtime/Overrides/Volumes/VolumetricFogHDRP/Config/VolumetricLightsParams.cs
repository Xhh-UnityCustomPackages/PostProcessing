using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class VolumetricLightsParams
    {
        static class VolumetricLightParamsBuffer
        {
            public static int _AdditnalLightCount;
            public static int _AdditnalLightEnable;
            public static int _AdditnalLightMultiplier;
            public static int _AdditnalLightShadowDimmer;
            public static int _AdditnalLightUseShadow;

            public static int _MainLightEnable;
            public static int _MainLightMultiplier;
            public static int _MainLightShadowDimmer;

        }
        
        public float[] m_LightEnable;
        public float[] m_LightMultiplier;
        public float[] m_LightShadowDimmer;
        public float[] m_UseCurrentShadow; 
        public int maxLights;

        public void InitVolumetricLightParams()
        {
            //初始化体积光参数ID
            VolumetricLightParamsBuffer._AdditnalLightEnable = Shader.PropertyToID("_AdditnalLightEnable");
            VolumetricLightParamsBuffer._AdditnalLightMultiplier = Shader.PropertyToID("_AdditnalLightMultiplier");
            VolumetricLightParamsBuffer._AdditnalLightShadowDimmer = Shader.PropertyToID("_AdditnalLightShadowDimmer");
            VolumetricLightParamsBuffer._MainLightEnable = Shader.PropertyToID("_MainLightEnable");
            VolumetricLightParamsBuffer._MainLightMultiplier = Shader.PropertyToID("_MainLightMultiplier");
            VolumetricLightParamsBuffer._MainLightShadowDimmer = Shader.PropertyToID("_MainLightShadowDimmer");
            VolumetricLightParamsBuffer._AdditnalLightCount = Shader.PropertyToID("_AdditnalLightCount");
            VolumetricLightParamsBuffer._AdditnalLightUseShadow = Shader.PropertyToID("_AdditnalLightUseShadow");
            //获取管线中允许使用实时多光源最大数量
            maxLights = UniversalRenderPipeline.maxVisibleAdditionalLights;
            m_LightEnable = new float[maxLights];
            m_LightMultiplier = new float[maxLights];
            m_LightShadowDimmer = new float[maxLights];
            m_UseCurrentShadow = new float[maxLights];  
        }
        
        public void UpadateSetVolumetricMainLightParams(CommandBuffer cmd, UniversalLightData lightData)
        {
            float useVolumetric = 0;
            var lights = lightData.visibleLights;
            if (lights.Length == 0)
            {
                return;
            }
            int mainLightIndex = lightData.mainLightIndex;
            if (mainLightIndex < 0)
            {
                return;
            }

            ref VisibleLight mainLightData = ref lights.UnsafeElementAtMutable(mainLightIndex);
            Light mainLight = mainLightData.light;

            if (mainLight != null)
            {
                var setting = VolumeManager.instance.stack.GetComponent<VolumetricFogHDRP>();
                
                var mainLightParams = mainLight.GetUniversalAdditionalLightData();
                // if (mainLightParams.useVolumetric)
                // {
                    useVolumetric = 1;
                // }
                // else
                // {
                //     useVolumetric = 0;
                // }

                cmd.SetGlobalFloat(VolumetricLightParamsBuffer._MainLightEnable, useVolumetric);
                cmd.SetGlobalFloat(VolumetricLightParamsBuffer._MainLightMultiplier, setting.mainLightMultiplier.value);
                cmd.SetGlobalFloat(VolumetricLightParamsBuffer._MainLightShadowDimmer, setting.mainLightShadowDimmer.value);
            }
        }

        public void UpadateSetVolumetricAdditionalLightParams(CommandBuffer cmd, UniversalLightData lightData)
        {
            var lights = lightData.visibleLights;
            int addlightCount = lightData.additionalLightsCount;
            int additionalIndex = 0;
            int mainLightIndex = lightData.mainLightIndex;

            for (int i = 0; i < maxLights; i++)
            {
                m_LightEnable[i] = 0;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                if (i != mainLightIndex)
                {
                    ref VisibleLight nowLightData = ref lights.UnsafeElementAtMutable(i);
                    Light light = nowLightData.light;
                    light.gameObject.TryGetComponent<VolumetricAdditionalLight>(out var additionalLightData);
                    // var additionalLightData = light.GetUniversalAdditionalLightData();
                    if (/*additionalLightData.useVolumetric &&*/ light.type != LightType.Rectangle && !light.bakingOutput.isBaked)
                    {
                        m_LightEnable[additionalIndex] = 1;
                    }

                    else
                    {
                        m_LightEnable[additionalIndex] = 0;
                    }

                    m_LightMultiplier[additionalIndex] = additionalLightData.Scattering;//additionalLightData.volumetricMultiplier;
                    m_LightShadowDimmer[additionalIndex] = additionalLightData.Radius;//additionalLightData.shadowDimmer;

                    if (light.shadows == LightShadows.None)
                    {
                        m_UseCurrentShadow[additionalIndex] = 0;
                    }

                    else
                    {
                        m_UseCurrentShadow[additionalIndex] = 1;
                    }

                    additionalIndex += 1;
                    //Debug.Log(additionalLightData.volumetricMultiplier);
                }
            }

            cmd.SetGlobalFloatArray(VolumetricLightParamsBuffer._AdditnalLightEnable, m_LightEnable);
            cmd.SetGlobalFloatArray(VolumetricLightParamsBuffer._AdditnalLightMultiplier, m_LightMultiplier);
            cmd.SetGlobalFloatArray(VolumetricLightParamsBuffer._AdditnalLightShadowDimmer, m_LightShadowDimmer);
            cmd.SetGlobalFloatArray(VolumetricLightParamsBuffer._AdditnalLightUseShadow, m_UseCurrentShadow);
            cmd.SetGlobalInt(VolumetricLightParamsBuffer._AdditnalLightCount, addlightCount);
        }


    }
}