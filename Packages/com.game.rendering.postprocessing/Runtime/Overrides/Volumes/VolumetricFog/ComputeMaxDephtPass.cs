// using UnityEngine;
// using UnityEngine.Experimental.Rendering;
// using UnityEngine.Rendering;
// using UnityEngine.Rendering.RenderGraphModule;
// using UnityEngine.Rendering.Universal;
//
// namespace Game.Core.PostProcessing
// {
//     public class ComputeMaxDephtPass : ScriptableRenderPass
//     {
//         private class PassData
//         {
//             public ComputeShader generateMaxZCS;
//             public int maxZKernel;
//             public int maxZDownsampleKernel;
//             public int dilateMaxZKernel;
//
//             public Vector2Int intermediateMaskSize;
//             public Vector2Int finalMaskSize;
//             public Vector2Int minDepthMipOffset;
//
//             public float dilationWidth;
//             public int viewCount;
//
//             public TextureHandle depthTexture;
//             public TextureHandle maxZ8xBuffer;
//             public TextureHandle maxZBuffer;
//             public TextureHandle dilatedMaxZBuffer;
//         }
//         
//         static internal VolumetricCameraParams hdCamera;
//
//         public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
//         {
//             const string passName = "ComputeMaxDepth";
//
//             // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
//             using (var builder = renderGraph.AddComputePass<PassData>(passName, out var passData))
//             {
//                 builder.AllowPassCulling(false);
//                 var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricFogResources>();
//                 passData.generateMaxZCS = runtimeShaders.computeMaxDepth;
//                 passData.generateMaxZCS.shaderKeywords = null;
//
//                 CoreUtils.SetKeyword(passData.generateMaxZCS, "PLANAR_OBLIQUE_DEPTH", false); //是否考虑平面反射
//
//                 passData.maxZKernel = passData.generateMaxZCS.FindKernel("ComputeMaxZ");
//                 passData.maxZDownsampleKernel = passData.generateMaxZCS.FindKernel("ComputeFinalMask");
//                 passData.dilateMaxZKernel = passData.generateMaxZCS.FindKernel("DilateMask");
//
//                 passData.intermediateMaskSize.x = GraphicsUtility.DivRoundUp(hdCamera.actualWidth, 8);
//                 passData.intermediateMaskSize.y = GraphicsUtility.DivRoundUp(hdCamera.actualHeight, 8);
//
//                 passData.finalMaskSize.x = passData.intermediateMaskSize.x / 2;
//                 passData.finalMaskSize.y = passData.intermediateMaskSize.y / 2;
//                 //TODO:mip offset
//                 passData.minDepthMipOffset.x = 0;
//                 passData.minDepthMipOffset.y = 0;
//
//                 int frameIndex = (int)VolumetricFrameIndex(hdCamera);
//                 var currIdx = frameIndex & 1;
//
//                 if (perCameraDatas[nowCameraIndex].vBufferParams != null)
//                 {
//                     var currentParams = perCameraDatas[nowCameraIndex].vBufferParams[currIdx];
//                     float ratio = (float)currentParams.viewportSize.x / (float)hdCamera.actualWidth;
//                     passData.dilationWidth = ratio < 0.1f ? 2 :
//                         ratio < 0.5f ? 1 : 0;
//                 }
//                 else
//                 {
//                     passData.dilationWidth = 1;
//                 }
//
//                 passData.viewCount = 1;
//
//                 //Debug.Log(xr.viewCount);
//                 UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
//
//                 passData.maxZ8xBuffer = builder.CreateTransientTexture(new TextureDesc(Vector2.one * 0.125f, true, true)
//                     { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "MaxZ mask 8x" });
//                 builder.UseTexture(passData.maxZ8xBuffer, AccessFlags.ReadWrite);
//                 passData.maxZBuffer = builder.CreateTransientTexture(new TextureDesc(Vector2.one * 0.125f, true, true)
//                     { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "MaxZ mask" });
//                 builder.UseTexture(passData.maxZBuffer, AccessFlags.ReadWrite);
//                 passData.dilatedMaxZBuffer = renderGraph.CreateTexture(new TextureDesc(Vector2.one / 16.0f, true, true)
//                     { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "Dilated MaxZ mask" });
//                 builder.UseTexture(passData.dilatedMaxZBuffer, AccessFlags.ReadWrite);
//                 passData.depthTexture = resourceData.activeDepthTexture;
//
//                 builder.UseTexture(passData.depthTexture, AccessFlags.Read);
//                 //Debug.Log(passData.dilatedMaxZBuffer.GetDescriptor(renderGraph).dimension);
//                 //Debug.Log(11);
//
//                 builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) =>
//                     {
//                         var cs = data.generateMaxZCS;
//                         var kernel = data.maxZKernel;
//
//                         int maskW = data.intermediateMaskSize.x;
//                         int maskH = data.intermediateMaskSize.y;
//
//                         int dispatchX = maskW;
//                         int dispatchY = maskH;
//
//
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._OutputTexture, data.maxZ8xBuffer);
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._CameraDepthTexture, data.depthTexture);
//                         //Debug.Log("计算着色器尺寸X" + dispatchX.ToString() + "计算着色器尺寸Y：" + dispatchY.ToString() + "计算着色器尺寸Z:" + data.viewCount);
//
//                         ctx.cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);
//
//                         // --------------------------------------------------------------
//                         // Downsample to 16x16 and compute gradient if required
//
//                         kernel = data.maxZDownsampleKernel;
//
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._InputTexture, data.maxZ8xBuffer);
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._OutputTexture, data.maxZBuffer);
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._CameraDepthTexture, data.depthTexture);
//
//                         Vector4 srcLimitAndDepthOffset = new Vector4(
//                             maskW,
//                             maskH,
//                             data.minDepthMipOffset.x,
//                             data.minDepthMipOffset.y
//                         );
//                         ctx.cmd.SetComputeVectorParam(cs, VolumetricFogShaderIDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
//                         ctx.cmd.SetComputeFloatParam(cs, VolumetricFogShaderIDs._DilationWidth, data.dilationWidth);
//
//                         int finalMaskW = Mathf.CeilToInt(maskW / 2.0f);
//                         int finalMaskH = Mathf.CeilToInt(maskH / 2.0f);
//
//                         dispatchX = GraphicsUtility.DivRoundUp(finalMaskW, 8);
//                         dispatchY = GraphicsUtility.DivRoundUp(finalMaskH, 8);
//
//                         ctx.cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);
//
//                         // --------------------------------------------------------------
//                         // Dilate max Z and gradient.
//                         kernel = data.dilateMaxZKernel;
//
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._InputTexture, data.maxZBuffer);
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._OutputTexture, data.dilatedMaxZBuffer);
//                         ctx.cmd.SetComputeTextureParam(cs, kernel, VolumetricFogShaderIDs._CameraDepthTexture, data.depthTexture);
//
//                         srcLimitAndDepthOffset.x = finalMaskW;
//                         srcLimitAndDepthOffset.y = finalMaskH;
//                         ctx.cmd.SetComputeVectorParam(cs, VolumetricFogShaderIDs._SrcOffsetAndLimit, srcLimitAndDepthOffset);
//                         ctx.cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, data.viewCount);
//                     }
//                 );
//
//                 m_MaxZHandle = passData.dilatedMaxZBuffer;
//             }
//         }
//     }
// }