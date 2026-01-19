using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    [PostProcess("GroundTruthAmbientOcclusion", PostProcessInjectionPoint.BeforeRenderingDeferredLights | PostProcessInjectionPoint.BeforeRenderingOpaques)]
    public partial class GroundTruthAmbientOcclusionRenderer : PostProcessVolumeRenderer<GroundTruthAmbientOcclusion>
    {
        private enum ShaderPasses
        {
            AmbientOcclusion = 0,
            
            BilateralBlurHorizontal = 1,
            BilateralBlurVertical = 2,
            BilateralBlurFinal = 3,
            
            GaussianBlurHorizontal = 4,
            GaussianBlurVertical = 5
        }
        
        RenderTextureDescriptor m_AmbientOcclusionDescriptor;
        private bool _blurInCS;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
        {
        }

        private void PrepareAOParameters(ref RenderingData renderingData, out int downsampleDivider, out GroundTruthAmbientOcclusion.BlurQuality actualBlurQuality)
        {
            downsampleDivider = settings.downSample.value ? 2 : 1;
            actualBlurQuality = settings.blurQuality.value;
            // if (actualBlurQuality == AmbientOcclusionBlurQuality.Spatial && !_rendererData.PreferComputeShader)
            // {
            //     actualBlurQuality = AmbientOcclusionBlurQuality.Bilateral;
            // }

            
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            
            DescriptorDownSample(ref desc, downsampleDivider);
            m_AmbientOcclusionDescriptor = desc;
            
            _blurInCS =  actualBlurQuality == GroundTruthAmbientOcclusion.BlurQuality.Spatial;
            
            Matrix4x4 view = renderingData.cameraData.GetViewMatrix();
            Matrix4x4 proj = renderingData.cameraData.GetProjectionMatrix();
            
            // camera view space without translation, used by SSAO.hlsl ReconstructViewPos() to calculate view vector.
            Matrix4x4 cview = view;
            cview.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            Matrix4x4 cviewProj = proj * cview;
            Matrix4x4 cviewProjInv = cviewProj.inverse;
        }
    }
}