using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [Serializable, VolumeComponentMenu("Post-processing Custom/TemporalAntialiasingLite")]
    public class TemporalAntialiasingLite : VolumeSetting
    {
        // ----------------------------------------------------------------------------------------------------
        [Tooltip("SceneView可见")]
        public BoolParameter visibleInSceneView = new BoolParameter(true);

        [Tooltip("使用MotionVector (SceneView没有)")]
        [HideInInspector]
        public BoolParameter useMotionVector = new BoolParameter(true);

        public ClampedFloatParameter jitterSpread = new ClampedFloatParameter(0.75f, 0.1f, 1f);
        public ClampedFloatParameter stationaryBlending = new ClampedFloatParameter(0.95f, 0f, 0.99f);
        public ClampedFloatParameter motionBlending = new ClampedFloatParameter(0.7f, 0, 0.99f);
        public ClampedFloatParameter sharpness = new ClampedFloatParameter(0.95f, 0f, 3f);

        public override bool IsActive() => true;
    }

    [PostProcess("TemporalAntialiasingLite", PostProcessInjectionPoint.BeforeRenderingPostProcessing)]
    public class TemporalAntialiasingLiteRenderer : PostProcessVolumeRenderer<TemporalAntialiasingLite>
    {
        static class ShaderConstants
        {
            internal static readonly int PrevViewProjectionMatrix = Shader.PropertyToID("_PrevViewProjectionMatrix");

            internal static readonly int Jitter = Shader.PropertyToID("_Jitter");
            internal static readonly int Sharpness = Shader.PropertyToID("_Sharpness");
            internal static readonly int FinalBlendParameters = Shader.PropertyToID("_FinalBlendParameters");
            internal static readonly int HistoryTex = Shader.PropertyToID("_HistoryTex");
        }

        public class MultiCameraInfo
        {
            const int k_NumHistoryTextures = 2;
            RTHandle[] m_HistoryPingPongRTHandle;
            public Matrix4x4 m_PreviousViewProjectionMatrix = Matrix4x4.zero;
            int m_PingPong = 0;

            public MultiCameraInfo()
            {
                m_HistoryPingPongRTHandle = new RTHandle[k_NumHistoryTextures];
            }

            public Matrix4x4 SetPreviousVPMatrix(Matrix4x4 curVPMatrix)
            {
                Matrix4x4 preVPMatrix = m_PreviousViewProjectionMatrix == Matrix4x4.zero ? curVPMatrix : m_PreviousViewProjectionMatrix;
                m_PreviousViewProjectionMatrix = curVPMatrix;
                return preVPMatrix;
            }

            public RTHandle GetHistoryRTHandle(int id)
            {
                return m_HistoryPingPongRTHandle[id];
            }

            public void SetHistoryRTHandle(int id, RTHandle rt)
            {
                m_HistoryPingPongRTHandle[id] = rt;
            }

            public void GetHistoryPingPongRT(ref RTHandle rt1, ref RTHandle rt2)
            {
                int index = m_PingPong;
                m_PingPong = ++m_PingPong % 2;

                rt1 = GetHistoryRTHandle(index);
                rt2 = GetHistoryRTHandle(m_PingPong);
            }

            public void Clear()
            {
                for (int i = 0; i < m_HistoryPingPongRTHandle.Length; i++)
                {
                    if (m_HistoryPingPongRTHandle[i] != null)
                        m_HistoryPingPongRTHandle[i].Release();
                    m_HistoryPingPongRTHandle[i] = null;
                }
            }
        }

        public int sampleIndex { get; private set; }
        const int k_SampleCount = 8;

        Vector2 GenerateRandomOffset()
        {
            // The variance between 0 and the actual halton sequence values reveals noticeable instability
            // in Unity's shadow maps, so we avoid index 0.
            var offset = new Vector2(
                HaltonSequence.Get((sampleIndex & 1023) + 1, 2) - 0.5f,
                HaltonSequence.Get((sampleIndex & 1023) + 1, 3) - 0.5f
            );

            if (++sampleIndex >= k_SampleCount)
                sampleIndex = 0;

            return offset;
        }

        public static Matrix4x4 GetJitteredProjectionMatrix(CameraData cameraData, ref Vector2 jitter)
        {
            var projMatrix = cameraData.camera.projectionMatrix;
            var desc = cameraData.cameraTargetDescriptor;

            jitter = new Vector2(jitter.x / desc.width, jitter.y / desc.height);

            if (cameraData.camera.orthographic)
            {
                projMatrix[0, 3] -= jitter.x * 2;
                projMatrix[1, 3] -= jitter.y * 2;
            }
            else
            {
                projMatrix[0, 2] += jitter.x * 2;
                projMatrix[1, 2] += jitter.y * 2;
            }

            return projMatrix;
        }

        public override bool visibleInSceneView => settings.visibleInSceneView.value;
        public override ScriptableRenderPassInput input => settings.useMotionVector.value ? ScriptableRenderPassInput.Motion : base.input;
        public override bool dontCareSourceTargetCopy => true;

        Material m_TemporalAntialiasingMaterial;
        // ------------------------------------------------------------------------------------
        string[] m_ShaderKeywords = new string[1];

        Matrix4x4 m_PreviousViewProjectionMatrix;
        Vector2 m_Jitter;
        TemporalAntialiasingCamera m_TemporalAntialiasingCamera;

        // 为了SceneView的显示 得把历史RT分开存
        Dictionary<int, MultiCameraInfo> m_MultiCameraInfo = new Dictionary<int, MultiCameraInfo>();

        RTHandle rt1, rt2;

        void CheckHistoryRT(int id, int hash, CommandBuffer cmd, RTHandle source, RenderTextureDescriptor desc, bool isSceneView)
        {
            if (!m_MultiCameraInfo.ContainsKey(hash))
            {
                m_MultiCameraInfo[hash] = new MultiCameraInfo();
            }

            // 为了记录A通道 需要修改格式  移动平台未知

            var rtHandle = m_MultiCameraInfo[hash].GetHistoryRTHandle(id);

            if (RenderingUtils.ReAllocateIfNeeded(ref rtHandle, desc, name: "_TemporalHistoryRT_" + id))
            {
                cmd.Blit(source, rtHandle);
                // 分辨率改变时还是从上一个历史RT拷贝
                m_MultiCameraInfo[hash].SetHistoryRTHandle(id, rtHandle);
            }
        }

        private void SetupMaterials(ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;

            m_TemporalAntialiasingMaterial.SetMatrix(ShaderConstants.PrevViewProjectionMatrix, m_PreviousViewProjectionMatrix);

            const float kMotionAmplification = 100f * 60f;
            m_TemporalAntialiasingMaterial.SetVector(ShaderConstants.Jitter, m_Jitter);
            m_TemporalAntialiasingMaterial.SetFloat(ShaderConstants.Sharpness, settings.sharpness.value);
            m_TemporalAntialiasingMaterial.SetVector(ShaderConstants.FinalBlendParameters,
            new Vector4(settings.stationaryBlending.value, settings.motionBlending.value, kMotionAmplification, 0f));

            // -------------------------------------------------------------------------------------------------
            // local shader keywords
            m_ShaderKeywords[0] = (!cameraData.isSceneViewCamera && settings.useMotionVector.value) ? "_USEMOTIONVECTOR" : "_";
            m_TemporalAntialiasingMaterial.shaderKeywords = m_ShaderKeywords;
        }

        void Clear()
        {
            foreach (var info in m_MultiCameraInfo)
            {
                if (info.Value != null)
                    info.Value.Clear();
            }
            m_MultiCameraInfo.Clear();

            sampleIndex = 0;
        }

        public override void Setup()
        {
            m_TemporalAntialiasingMaterial = GetMaterial(m_PostProcessFeatureData.shaders.temporalAntialiasingLitePS);
            m_TemporalAntialiasingCamera = new TemporalAntialiasingCamera();
        }
        public override void ShowHide(bool showHide)
        {
            if (!showHide)
            {
                Clear();
            }
        }

        public override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_TemporalAntialiasingMaterial);
            Clear();
        }

        public override void AddRenderPasses(ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            Camera camera = cameraData.camera;
            int hash = camera.GetHashCode();

            if (!m_MultiCameraInfo.ContainsKey(hash))
            {
                m_MultiCameraInfo[hash] = new MultiCameraInfo();
            }
            Matrix4x4 curVPMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;
            m_PreviousViewProjectionMatrix = m_MultiCameraInfo[hash].SetPreviousVPMatrix(curVPMatrix);
            // -------------------------------------------------------------------------------------
            // jitterSpread
            int taaFrameIndex = sampleIndex;
            Vector2 jitter = GenerateRandomOffset();
            jitter *= settings.jitterSpread.value;
            Matrix4x4 jitterredProjectMatrix = GetJitteredProjectionMatrix(cameraData, ref jitter);
            m_Jitter = jitter;

            // camera setup
            m_TemporalAntialiasingCamera.Setup(jitterredProjectMatrix, taaFrameIndex);
            renderingData.cameraData.renderer.EnqueuePass(m_TemporalAntialiasingCamera);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) { }
        public override void OnCameraCleanup(CommandBuffer cmd) { }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;

            Camera camera = renderingData.cameraData.camera;

            //
            SetupMaterials(ref renderingData);

            int hash = camera.GetHashCode();
            CheckHistoryRT(0, hash, cmd, source, desc, renderingData.cameraData.isSceneViewCamera);
            CheckHistoryRT(1, hash, cmd, source, desc, renderingData.cameraData.isSceneViewCamera);


            m_MultiCameraInfo[hash].GetHistoryPingPongRT(ref rt1, ref rt2);

            //
            cmd.SetGlobalTexture(ShaderConstants.HistoryTex, rt1);
            Blit(cmd, source, rt2, m_TemporalAntialiasingMaterial, 0);
            //
            m_RenderPass.Blit(cmd, rt2, target);
        }
    }
}
