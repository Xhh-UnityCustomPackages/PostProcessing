using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class VolumetricFogHDRPRenderer
    {
        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
        
            //初始化VBuffer数据
            ReinitializeVolumetricBufferParams(postProcessData);
            //更新分割相机的VBuffer数据并写入VolumetricCameraParams用于后续计算
            UpdateVolumetricBufferParams(settings, postProcessData);
		    
            //准备用于计算重投影数据
            ResizeVolumetricHistoryBuffers(postProcessData);
        }
    }
}
