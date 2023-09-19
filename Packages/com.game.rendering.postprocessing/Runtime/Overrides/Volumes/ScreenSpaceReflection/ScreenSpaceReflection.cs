using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/屏幕空间反射 (Screen Space Reflection)")]
    public class ScreenSpaceReflection : VolumeSetting
    {
        public ScreenSpaceReflection()
        {
            displayName = "屏幕空间反射 (Screen Space Reflection)";
        }

        public static int MAX_BLUR_ITERATIONS = 4;

        public enum Resolution
        {
            Half,
            Full,
            Double
        }

        public enum DebugMode
        {
            Disabled,
            SSROnly,
            IndirectSpecular,
        }

        [Serializable]
        public class ResolutionParameter : VolumeParameter<Resolution> { }

        [Serializable]
        public class DebugModeParameter : VolumeParameter<DebugMode> { }

        [Tooltip("分辨率")]
        public ResolutionParameter resolution = new ResolutionParameter { value = Resolution.Double };

        [Tooltip("最大追踪次数, 移动端会被固定到10次")]
        public ClampedIntParameter maximumIterationCount = new ClampedIntParameter(256, 1, 256);

        [Tooltip("模糊迭代次数")]
        public ClampedIntParameter blurIterations = new ClampedIntParameter(3, 1, MAX_BLUR_ITERATIONS);

        [Space(6)]
        [Tooltip("强度")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 5f);

        [Tooltip("实际上是追踪步长, 越大精度越低, 追踪范围越大, 越节省追踪次数")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(8f, 1f, 64f);

        [Tooltip("最大追踪距离")]
        public MinFloatParameter maximumMarchDistance = new MinFloatParameter(100f, 0f);

        [Tooltip("值越大, 未追踪部分天空颜色会越多, 过度边界会越硬")]
        public ClampedFloatParameter distanceFade = new ClampedFloatParameter(0.02f, 0f, 1f);

        [Tooltip("渐变")]
        public ClampedFloatParameter vignette = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("减少闪烁问题, 需要MotionVector, SceneView未处理")]
        public BoolParameter antiFlicker = new BoolParameter(true);

        [Tooltip("Unity老版本算法")]
        public BoolParameter oldMethod = new BoolParameter(false);


        public DebugModeParameter debugMode = new DebugModeParameter { value = DebugMode.Disabled };
        public override bool IsActive() => intensity.value > 0;


    }

    [PostProcess("ScreenSpaceReflection", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public class ScreenSpaceReflectionRenderer : PostProcessVolumeRenderer<ScreenSpaceReflection>
    {
        static class ShaderConstants
        {
            internal static readonly int ResolveTex = Shader.PropertyToID("_ResolveTex");
            internal static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
            internal static readonly int TestTex = Shader.PropertyToID("_TestTex");
            internal static readonly int HistoryTex = Shader.PropertyToID("_HistoryTex");

            internal static readonly int ViewMatrix = Shader.PropertyToID("_ViewMatrixSSR");
            internal static readonly int InverseViewMatrix = Shader.PropertyToID("_InverseViewMatrixSSR");
            internal static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrixSSR");
            internal static readonly int ScreenSpaceProjectionMatrix = Shader.PropertyToID("_ScreenSpaceProjectionMatrixSSR");

            internal static readonly int Params1 = Shader.PropertyToID("_Params1");
            internal static readonly int Params2 = Shader.PropertyToID("_Params2");
            internal static readonly int Offset = Shader.PropertyToID("_Offset");

            public static int[] _BlurMipUp;
            public static int[] _BlurMipDown;

            public static string GetDebugKeyword(ScreenSpaceReflection.DebugMode debugMode)
            {
                switch (debugMode)
                {
                    case ScreenSpaceReflection.DebugMode.SSROnly:
                        return "DEBUG_SCREEN_SPACE_REFLECTION";
                    case ScreenSpaceReflection.DebugMode.IndirectSpecular:
                        return "DEBUG_INDIRECT_SPECULAR";
                    case ScreenSpaceReflection.DebugMode.Disabled:
                    default:
                        return "_";
                }
            }
        }

        RenderTextureDescriptor m_ScreenSpaceReflectionDescriptor;
        Material m_ScreenSpaceReflectionMaterial;
        Material m_BlurMaterial;

        bool m_SupportARGBHalf = true;
        const int k_MaxPyramidSize = 16;

        RTHandle m_TestRT;
        RTHandle m_ResloveRT;


        RTHandle[] m_BlurMipUpsRT = new RTHandle[ScreenSpaceReflection.MAX_BLUR_ITERATIONS];
        RTHandle[] m_BlurMipDownsRT = new RTHandle[ScreenSpaceReflection.MAX_BLUR_ITERATIONS];

        const int k_NumHistoryTextures = 2;
        RTHandle[] m_HistoryPingPongRT = new RTHandle[k_NumHistoryTextures];
        int m_PingPong = 0;

        public void GetHistoryPingPongRT(ref RTHandle rt1, ref RTHandle rt2)
        {
            int index = m_PingPong;
            m_PingPong = ++m_PingPong % 2;

            rt1 = m_HistoryPingPongRT[index];
            rt2 = m_HistoryPingPongRT[m_PingPong];
        }

        // public override bool IsActive(ref RenderingData renderingData)
        // {
        //     Debug.LogError(renderingData.cameraData.camera.actualRenderingPath);
        //     bool isDeferred = renderingData.cameraData.camera.actualRenderingPath == RenderingPath.DeferredShading;
        //     return isDeferred && base.IsActive(ref renderingData);
        // }

        public override void Setup()
        {
            m_ScreenSpaceReflectionMaterial = GetMaterial(postProcessFeatureData.shaders.screenSpaceReflectionPS);
            m_BlurMaterial = Material.Instantiate(postProcessFeatureData.materials.DualBlur);

            ShaderConstants._BlurMipUp = new int[k_MaxPyramidSize];
            ShaderConstants._BlurMipDown = new int[k_MaxPyramidSize];
            for (int i = 0; i < k_MaxPyramidSize; i++)
            {
                ShaderConstants._BlurMipUp[i] = Shader.PropertyToID("_SSR_BlurMipUp" + i);
                ShaderConstants._BlurMipDown[i] = Shader.PropertyToID("_SSR_BlurMipDown" + i);
            }

            m_SupportARGBHalf = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_ScreenSpaceReflectionDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            m_ScreenSpaceReflectionDescriptor.msaaSamples = 1;
            m_ScreenSpaceReflectionDescriptor.depthBufferBits = 0;

            int size = Mathf.ClosestPowerOfTwo(Mathf.Min(m_ScreenSpaceReflectionDescriptor.width, m_ScreenSpaceReflectionDescriptor.height));

            if (settings.resolution.value == ScreenSpaceReflection.Resolution.Half)
                size >>= 1;
            else if (settings.resolution.value == ScreenSpaceReflection.Resolution.Double)
                size <<= 1;
            m_ScreenSpaceReflectionDescriptor.width = size;
            m_ScreenSpaceReflectionDescriptor.height = size;


            // SSR 移动端用B10G11R11 见MakeRenderTextureGraphicsFormat 就算不管Alpha通道问题 精度也非常难受
            var testDesc = m_ScreenSpaceReflectionDescriptor;
            if (m_SupportARGBHalf)
            {
                testDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                m_ScreenSpaceReflectionDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            }
            else
            {
                // resolve需要一个渐变模糊后参与最终混合, 必须要Alpha通道
                // 移动端没办法 就只能降到LDR了
                m_ScreenSpaceReflectionDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            }

            RenderingUtils.ReAllocateIfNeeded(ref m_TestRT, testDesc, FilterMode.Point, name: "_TestTex");
            RenderingUtils.ReAllocateIfNeeded(ref m_ResloveRT, m_ScreenSpaceReflectionDescriptor, FilterMode.Bilinear, name: "_ResolveTex");
        }


        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            SetupMaterials(ref renderingData);

            Blit(cmd, source, m_TestRT, m_ScreenSpaceReflectionMaterial, 0);

            m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.TestTex, m_TestRT);
            Blit(cmd, source, m_ResloveRT, m_ScreenSpaceReflectionMaterial, 1);


            RTHandle lastDownId = m_ResloveRT;
            // ----------------------------------------------------------------------------------
            // 简化版本没有用Jitter所以sceneview部分就不处理了
            if (!renderingData.cameraData.isSceneViewCamera && settings.antiFlicker.value)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_HistoryPingPongRT[0], m_ScreenSpaceReflectionDescriptor);
                RenderingUtils.ReAllocateIfNeeded(ref m_HistoryPingPongRT[1], m_ScreenSpaceReflectionDescriptor);

                // 不确定移动端CopyTexture的支持，所以先用这种方法
                RTHandle rt1 = null, rt2 = null;
                GetHistoryPingPongRT(ref rt1, ref rt2);

                m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.HistoryTex, rt1);
                Blit(cmd, m_ResloveRT, rt2, m_ScreenSpaceReflectionMaterial, 5);
                lastDownId = rt2;
            }
            // ----------------------------------------------------------------------------------


            // ------------------------------------------------------------------------------------------------
            // 简化版本 DualBlur替代 放弃不同粗糙度mipmap的采样
            int iter = settings.blurIterations.value;
            RTHandle lastUp;
            if (iter > 0)
            {
                RenderTextureDescriptor blurDesc = m_ScreenSpaceReflectionDescriptor;
                for (int i = 0; i < iter; i++)
                {
                    RenderingUtils.ReAllocateIfNeeded(ref m_BlurMipUpsRT[i], blurDesc, FilterMode.Bilinear, name: "_BlurMipUp" + i);
                    RenderingUtils.ReAllocateIfNeeded(ref m_BlurMipDownsRT[i], blurDesc, FilterMode.Bilinear, name: "_BlurMipDown" + i);
                    //         cmd.GetTemporaryRT(ShaderConstants._BlurMipUp[i], blurDesc, FilterMode.Bilinear);
                    //         cmd.GetTemporaryRT(ShaderConstants._BlurMipDown[i], blurDesc, FilterMode.Bilinear);

                    Blit(cmd, lastDownId, m_BlurMipDownsRT[i], m_BlurMaterial, 0);

                    lastDownId = m_BlurMipDownsRT[i];
                    DescriptorDownSample(ref blurDesc, 2);
                }

                // Upsample
                lastUp = m_BlurMipDownsRT[iter - 1];
                for (int i = iter - 2; i >= 0; i--)
                {
                    Blit(cmd, lastUp, m_BlurMipUpsRT[i], m_BlurMaterial, 1);
                    lastUp = m_BlurMipUpsRT[i];
                }

                // Render blurred texture in blend pass
                Blit(cmd, lastUp, m_ResloveRT, m_BlurMaterial, 1);
            }


            m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.ResolveTex, m_ResloveRT);
            Blit(cmd, source, target, m_ScreenSpaceReflectionMaterial, 3);
        }



        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_ScreenSpaceReflectionMaterial);
            CoreUtils.Destroy(m_BlurMaterial);

            m_ResloveRT?.Release();
            m_TestRT?.Release();

            for (int i = 0; i < m_HistoryPingPongRT.Length; i++)
                m_HistoryPingPongRT[i]?.Release();

            for (int i = 0; i < m_BlurMipUpsRT.Length; i++)
                m_BlurMipUpsRT[i]?.Release();

            for (int i = 0; i < m_BlurMipDownsRT.Length; i++)
                m_BlurMipDownsRT[i]?.Release();
        }


        private void SetupMaterials(ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            var size = m_ScreenSpaceReflectionDescriptor.width;

            var noiseTex = postProcessFeatureData.textures.blueNoiseTex;
            m_ScreenSpaceReflectionMaterial.SetTexture(ShaderConstants.NoiseTex, noiseTex);


            var screenSpaceProjectionMatrix = new Matrix4x4();
            screenSpaceProjectionMatrix.SetRow(0, new Vector4(size * 0.5f, 0f, 0f, size * 0.5f));
            screenSpaceProjectionMatrix.SetRow(1, new Vector4(0f, size * 0.5f, 0f, size * 0.5f));
            screenSpaceProjectionMatrix.SetRow(2, new Vector4(0f, 0f, 1f, 0f));
            screenSpaceProjectionMatrix.SetRow(3, new Vector4(0f, 0f, 0f, 1f));

            var projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            screenSpaceProjectionMatrix *= projectionMatrix;

            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.ViewMatrix, camera.worldToCameraMatrix);
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.InverseViewMatrix, camera.worldToCameraMatrix.inverse);
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.InverseProjectionMatrix, projectionMatrix.inverse);
            m_ScreenSpaceReflectionMaterial.SetMatrix(ShaderConstants.ScreenSpaceProjectionMatrix, screenSpaceProjectionMatrix);
            m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params1, new Vector4((float)settings.vignette.value, settings.distanceFade.value, settings.maximumMarchDistance.value, settings.intensity.value));
            m_ScreenSpaceReflectionMaterial.SetVector(ShaderConstants.Params2, new Vector4(width / height, (float)size / (float)noiseTex.width, settings.thickness.value, settings.maximumIterationCount.value));

            // 没有调节的需求
            m_BlurMaterial.SetFloat(ShaderConstants.Offset, 0.1f);

            // -------------------------------------------------------------------------------------------------
            // local shader keywords
            // m_ShaderKeywords[0] = settings.oldMethod.value ? "_OLD_METHOD" : "_";
            // m_ShaderKeywords[1] = ShaderConstants.GetDebugKeyword(settings.debugMode.value);

            // m_ScreenSpaceReflectionMaterial.shaderKeywords = m_ShaderKeywords;
        }
    }
}
