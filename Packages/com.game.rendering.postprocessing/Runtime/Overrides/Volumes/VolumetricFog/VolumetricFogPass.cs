using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
     [PostProcess("体积雾 (Volumetric Fog)", PostProcessInjectionPoint.AfterRenderingSkybox)]
    public partial class VolumetricFogRenderer : PostProcessVolumeRenderer<VolumetricFog>
    {
	    static internal ComputeShader m_VolumeVoxelizationCS = null;
	    static internal ComputeShader m_VolumetricLightingCS = null;
	    static internal ComputeShader m_VolumetricLightingFilteringCS = null;
	    
	    static internal List<PerCameraVolumetricFogData> perCameraDatas = new List<PerCameraVolumetricFogData>();
	    
	    public override ScriptableRenderPassInput input => ScriptableRenderPassInput.Depth;


	    public override void Setup()
	    {
		    var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogResources>();
		    m_VolumeVoxelizationCS = runtimeShaders.volumeVoxelization;
		    m_VolumetricLightingCS = runtimeShaders.volumetricFogLighting;
		    m_VolumetricLightingFilteringCS = runtimeShaders.volumetricLightingFilter;
		    
		    profilingSampler = new ProfilingSampler("Volumetric Fog");
		    
	    }

	    public override void Dispose(bool disposing)
	    {
	    }

	    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
	    {
		   
	    }

	    public override void Render(CommandBuffer cmd, RTHandle source, RTHandle destination, ref RenderingData renderingData)
	    {
		    var camera = renderingData.cameraData.camera;
		    //初始化VBuffer数据
		    // ReinitializeVolumetricBufferParams(camera);
	    }

	    // static internal void ReinitializeVolumetricBufferParams(VolumetricCameraParams hdCamera)
	    // {
		   //  bool init = perCameraDatas[nowCameraIndex].vBufferParams != null;
	    //
	    //
		   //  if (init)
		   //  {
			  //   // Deinitialize.
			  //   perCameraDatas[nowCameraIndex].vBufferParams = null;
		   //  }
		   //  else
		   //  {
			  //   // Initialize.
			  //   // Start with the same parameters for both frames. Then update them one by one every frame.
			  //   var parameters = ComputeVolumetricBufferParameters(hdCamera);
			  //   perCameraDatas[nowCameraIndex].vBufferParams = new VBufferParameters[2];
			  //   perCameraDatas[nowCameraIndex].vBufferParams[0] = parameters;
			  //   perCameraDatas[nowCameraIndex].vBufferParams[1] = parameters;
		   //  }
	    // }
    }
}