using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Core.PostProcessing
{
    public struct PackedMipChainInfo
    {
        public Vector2Int textureSize;
        public int mipLevelCount; // mips contain min (closest) depth
        public int mipLevelCountCheckerboard;
        public Vector2Int[] mipLevelSizes;
        public Vector2Int[] mipLevelOffsets; // mips contain min (closest) depth
        public Vector2Int[] mipLevelOffsetsCheckerboard;

        private Vector2 cachedTextureScale;
        private Vector2Int cachedHardwareTextureSize;
        private int cachedCheckerboardMipCount;
    }

    public class MipGenerator
    {
        RTHandle m_TempColorTargets;
        RTHandle m_TempDownsamplePyramid;

        ComputeShader m_DepthPyramidCS;
        ComputeShader m_ColorPyramidCS;
        Shader m_ColorPyramidPS;
        Material m_ColorPyramidPSMat;
        MaterialPropertyBlock m_PropertyBlock;

        int m_DepthDownsampleKernel;
        int m_ColorDownsampleKernel;
        int m_ColorGaussianKernel;

        public MipGenerator()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<PyramidDepthGeneratorResources>();
            m_DepthPyramidCS = runtimeShaders.depthPyramidCS;
            m_DepthDownsampleKernel = m_DepthPyramidCS.FindKernel("KDepthDownsample8DualUav");
        }
        
        public void Release()
        {
            RTHandles.Release(m_TempColorTargets);
            m_TempColorTargets = null;
            RTHandles.Release(m_TempDownsamplePyramid);
            m_TempDownsamplePyramid = null;

            CoreUtils.Destroy(m_ColorPyramidPSMat);
        }

        public void RenderMinDepthPyramid(CommandBuffer cmd, RenderTexture texture, PackedMipChainInfo info)
        {
            if (!texture.IsCreated())
                texture.Create();
            
            var cs = m_DepthPyramidCS;
            int kernel = m_DepthDownsampleKernel;

            cmd.SetComputeTextureParam(cs, kernel, PipelineShaderIDs._DepthMipChain, texture);
            
             // Note: Gather() doesn't take a LOD parameter and we cannot bind an SRV of a MIP level,
            // and we don't support Min samplers either. So we are forced to perform 4x loads.
            for (int dstIndex0 = 1; dstIndex0 < info.mipLevelCount;)
            {
                int minCount = Mathf.Min(info.mipLevelCount - dstIndex0, 4);
                int cbCount = 0;
                if (dstIndex0 < info.mipLevelCountCheckerboard)
                { 
                    cbCount = info.mipLevelCountCheckerboard - dstIndex0;
                    Debug.Assert(dstIndex0 == 1, "expected to make checkerboard mips on the first pass");
                    Debug.Assert(cbCount <= minCount, "expected fewer checkerboard mips than min mips");
                    Debug.Assert(cbCount <= 2, "expected 2 or fewer checkerboard mips for now");
                }

                Vector2Int srcOffset = info.mipLevelOffsets[dstIndex0 - 1];
                Vector2Int srcSize = info.mipLevelSizes[dstIndex0 - 1];
                int dstIndex1 = Mathf.Min(dstIndex0 + 1, info.mipLevelCount - 1);
                int dstIndex2 = Mathf.Min(dstIndex0 + 2, info.mipLevelCount - 1);
                int dstIndex3 = Mathf.Min(dstIndex0 + 3, info.mipLevelCount - 1);

                DepthPyramidConstants cb = new DepthPyramidConstants
                {
                    _MinDstCount = (uint)minCount,
                    _CbDstCount = (uint)cbCount,
                    _SrcOffset = srcOffset,
                    _SrcLimit = srcSize - Vector2Int.one,
                    _DstSize0 = info.mipLevelSizes[dstIndex0],
                    _DstSize1 = info.mipLevelSizes[dstIndex1],
                    _DstSize2 = info.mipLevelSizes[dstIndex2],
                    _DstSize3 = info.mipLevelSizes[dstIndex3],
                    _MinDstOffset0 = info.mipLevelOffsets[dstIndex0],
                    _MinDstOffset1 = info.mipLevelOffsets[dstIndex1],
                    _MinDstOffset2 = info.mipLevelOffsets[dstIndex2],
                    _MinDstOffset3 = info.mipLevelOffsets[dstIndex3],
                    _CbDstOffset0 = info.mipLevelOffsetsCheckerboard[dstIndex0],
                    _CbDstOffset1 = info.mipLevelOffsetsCheckerboard[dstIndex1],
                };
                ConstantBuffer.Push(cmd, cb, cs, PipelineShaderIDs._DepthPyramidConstants);

                CoreUtils.SetKeyword(cmd, cs, "ENABLE_CHECKERBOARD", cbCount != 0);

                Vector2Int dstSize = info.mipLevelSizes[dstIndex0];
                cmd.DispatchCompute(cs, kernel, GraphicsUtility.DivRoundUp(dstSize.x, 8), GraphicsUtility.DivRoundUp(dstSize.y, 8), texture.volumeDepth);

                dstIndex0 += minCount;
            }
        }
    }
}