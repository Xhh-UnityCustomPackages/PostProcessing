using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class VolumetricFogHDRPRenderer
    {
        static internal TextureHandle m_MaxZTexture;
        static internal TextureHandle m_DensityTexture;
        static internal TextureHandle m_LightingTexture;
        
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            //初始化VBuffer数据
            ReinitializeVolumetricBufferParams(postProcessData);
            //更新分割相机的VBuffer数据并写入VolumetricCameraParams用于后续计算
            UpdateVolumetricBufferParams(settings, postProcessData);
		    
            //准备用于计算重投影数据
            ResizeVolumetricHistoryBuffers(postProcessData);
            VolumetricFogHDRP.UpdateShaderVariablesGlobalCB(ref volumetricGlobalCB);
            //更新GPU全局变量
            UpdateShaderVariablesGlobalVolumetrics(ref volumetricGlobalCB, postProcessData);
            //写入LocalVolumetricFog数据
            PrepareVisibleLocalVolumetricFogList(postProcessData);
		    
            int frameIndex = (int)VolumetricFrameIndex(postProcessData);
            var currIdx = (frameIndex + 0) & 1;
            var currParams = postProcessData.vBufferParams[currIdx];
		    
            var cvp = currParams.viewportSize;
            var res = new Vector4(cvp.x, cvp.y, 1.0f / cvp.x, 1.0f / cvp.y);
                 
            ComputeVolumetricFogSliceCountAndScreenFraction(settings, out var maxSliceCount, out _);
            UpdateShaderVariableslVolumetrics(ref m_ShaderVariablesVolumetricCB, postProcessData, res, maxSliceCount, true);
        }
    }
}
