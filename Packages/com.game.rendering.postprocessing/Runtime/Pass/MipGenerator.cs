using System;
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

        private bool m_OffsetBufferWillNeedUpdate;

        public void Allocate()
        {
            mipLevelOffsets = new Vector2Int[15];
            mipLevelOffsetsCheckerboard = new Vector2Int[15];
            mipLevelSizes = new Vector2Int[15];
            m_OffsetBufferWillNeedUpdate = true;
        }

        enum PackDirection
        {
            Right,
            Down,
        }

        static Vector2Int NextMipBegin(Vector2Int prevMipBegin, Vector2Int prevMipSize, PackDirection dir)
        {
            Vector2Int mipBegin = prevMipBegin;
            if (dir == PackDirection.Right)
                mipBegin.x += prevMipSize.x;
            else
                mipBegin.y += prevMipSize.y;
            return mipBegin;
        }

        // We pack all MIP levels into the top MIP level to avoid the Pow2 MIP chain restriction.
        // We compute the required size iteratively.
        // This function is NOT fast, but it is illustrative, and can be optimized later.
        public void ComputePackedMipChainInfo(Vector2Int viewportSize, int checkerboardMipCount)
        {
            // only support up to 2 mips of checkerboard data being created
            checkerboardMipCount = Mathf.Clamp(checkerboardMipCount, 0, 2);

            bool isHardwareDrsOn = DynamicResolutionHandler.instance.HardwareDynamicResIsEnabled();
            Vector2Int hardwareTextureSize = isHardwareDrsOn ? DynamicResolutionHandler.instance.ApplyScalesOnSize(viewportSize) : viewportSize;
            Vector2 textureScale = isHardwareDrsOn ? new Vector2((float)viewportSize.x / (float)hardwareTextureSize.x, (float)viewportSize.y / (float)hardwareTextureSize.y) : new Vector2(1.0f, 1.0f);


            // We need to mark the buffer dirty in case another camera has a different viewport size
            m_OffsetBufferWillNeedUpdate = true;

            // No work needed.
            if (cachedHardwareTextureSize == hardwareTextureSize && cachedTextureScale == textureScale &&
                cachedCheckerboardMipCount == checkerboardMipCount)
                return;

            cachedHardwareTextureSize = hardwareTextureSize;
            cachedTextureScale = textureScale;
            cachedCheckerboardMipCount = checkerboardMipCount;

            mipLevelSizes[0] = hardwareTextureSize;
            mipLevelOffsets[0] = Vector2Int.zero;
            mipLevelOffsetsCheckerboard[0] = mipLevelOffsets[0];

            int mipLevel = 0;
            Vector2Int mipSize = hardwareTextureSize;
            bool hasCheckerboard = (checkerboardMipCount != 0);
            int maxCheckboardLevelCount = hasCheckerboard ? (1 + checkerboardMipCount) : 0;
            do
            {
                mipLevel++;

                // Round up.
                mipSize.x = Math.Max(1, (mipSize.x + 1) >> 1);
                mipSize.y = Math.Max(1, (mipSize.y + 1) >> 1);

                mipLevelSizes[mipLevel] = mipSize;

                Vector2Int prevMipSize = mipLevelSizes[mipLevel - 1];
                Vector2Int prevMipBegin = mipLevelOffsets[mipLevel - 1];
                Vector2Int prevMipBeginCheckerboard = mipLevelOffsetsCheckerboard[mipLevel - 1];

                Vector2Int mipBegin = prevMipBegin;
                Vector2Int mipBeginCheckerboard = prevMipBeginCheckerboard;
                if (mipLevel == 1)
                {
                    // first mip always below full resolution
                    mipBegin = NextMipBegin(prevMipBegin, prevMipSize, PackDirection.Down);

                    // pack checkerboard next to it if present
                    if (hasCheckerboard)
                        mipBeginCheckerboard = NextMipBegin(mipBegin, mipSize, PackDirection.Right);
                    else
                        mipBeginCheckerboard = mipBegin;
                }
                else
                {
                    // alternate directions, mip 2 starts with down if checkerboard, right if not
                    bool isOdd = ((mipLevel & 1) != 0);
                    PackDirection dir = (isOdd ^ hasCheckerboard) ? PackDirection.Down : PackDirection.Right;

                    mipBegin = NextMipBegin(prevMipBegin, prevMipSize, dir);
                    mipBeginCheckerboard = NextMipBegin(prevMipBeginCheckerboard, prevMipSize, dir);
                }

                mipLevelOffsets[mipLevel] = mipBegin;
                mipLevelOffsetsCheckerboard[mipLevel] = mipBeginCheckerboard;

                hardwareTextureSize.x = Math.Max(hardwareTextureSize.x, mipBegin.x + mipSize.x);
                hardwareTextureSize.y = Math.Max(hardwareTextureSize.y, mipBegin.y + mipSize.y);
                hardwareTextureSize.x = Math.Max(hardwareTextureSize.x, mipBeginCheckerboard.x + mipSize.x);
                hardwareTextureSize.y = Math.Max(hardwareTextureSize.y, mipBeginCheckerboard.y + mipSize.y);
            } 
            while ((mipSize.x > 1) || (mipSize.y > 1));

            textureSize = new Vector2Int(
                (int)Mathf.Ceil((float)hardwareTextureSize.x * textureScale.x), (int)Mathf.Ceil((float)hardwareTextureSize.y * textureScale.y));

            mipLevelCount = mipLevel + 1;
            mipLevelCountCheckerboard = hasCheckerboard ? (1 + checkerboardMipCount) : 0;
        }
        
        public ComputeBuffer GetOffsetBufferData(ComputeBuffer mipLevelOffsetsBuffer)
        {
            if (m_OffsetBufferWillNeedUpdate)
            {
                mipLevelOffsetsBuffer.SetData(mipLevelOffsets);
                m_OffsetBufferWillNeedUpdate = false;
            }

            return mipLevelOffsetsBuffer;
        }
    }

    public class MipGenerator
    {
        static class ShaderIDs
        {
            public static readonly int _DepthPyramidConstants = Shader.PropertyToID("DepthPyramidConstants");
            public static readonly int _Size = Shader.PropertyToID("_Size");
            public static readonly int _Source = Shader.PropertyToID("_Source");
            public static readonly int _BlitTexture = Shader.PropertyToID("_BlitTexture");
            public static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            public static readonly int _BlitMipLevel = Shader.PropertyToID("_BlitMipLevel");
            public static readonly int _Destination = Shader.PropertyToID("_Destination");
        }

        // RTHandle m_TempColorTargets;
        RTHandle m_TempDownsamplePyramid;

        ComputeShader m_DepthPyramidCS;
        ComputeShader m_ColorPyramidCS;
        // Shader m_ColorPyramidPS;
        // Material m_ColorPyramidPSMat;
        MaterialPropertyBlock m_PropertyBlock;

        int m_DepthDownsampleKernel;
        int m_ColorDownsampleKernel;
        int m_ColorGaussianKernel;

        public MipGenerator()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<PostProcessFeatureRuntimeResources>();
            m_DepthPyramidCS = runtimeShaders.depthPyramidCS;
            m_ColorPyramidCS = runtimeShaders.colorPyramidCS;
            
            m_DepthDownsampleKernel = m_DepthPyramidCS.FindKernel("KDepthDownsample8DualUav");
            m_ColorDownsampleKernel = m_ColorPyramidCS.FindKernel("KColorDownsample");
            m_ColorGaussianKernel = m_ColorPyramidCS.FindKernel("KColorGaussian");

            m_PropertyBlock = new MaterialPropertyBlock();
        }

        public void Release()
        {
            // RTHandles.Release(m_TempColorTargets);
            // m_TempColorTargets = null;
            RTHandles.Release(m_TempDownsamplePyramid);
            m_TempDownsamplePyramid = null;
            
            // CoreUtils.Destroy(m_ColorPyramidPSMat);
        }

        public void RenderMinDepthPyramid(CommandBuffer cmd, RenderTexture texture, PackedMipChainInfo info)
        {
            PostProcessingUtils.CheckRTCreated(texture);

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
                ConstantBuffer.Push(cmd, cb, cs, ShaderIDs._DepthPyramidConstants);

                CoreUtils.SetKeyword(cmd, cs, "ENABLE_CHECKERBOARD", cbCount != 0);

                Vector2Int dstSize = info.mipLevelSizes[dstIndex0];
                cmd.DispatchCompute(cs, kernel, GraphicsUtility.DivRoundUp(dstSize.x, 8), GraphicsUtility.DivRoundUp(dstSize.y, 8), texture.volumeDepth);

                dstIndex0 += minCount;
            }
        }
        
        
        // Generates the gaussian pyramid of source into destination
        // We can't do it in place as the color pyramid has to be read while writing to the color
        // buffer in some cases (e.g. refraction, distortion)
        // Returns the number of mips
        public int RenderColorGaussianPyramid(CommandBuffer cmd, Vector2Int size, Texture source, RenderTexture destination)
        {
            // Select between Tex2D and Tex2DArray versions of the kernels
            bool sourceIsArray = (source.dimension == TextureDimension.Tex2DArray);
            int rtIndex = sourceIsArray ? 1 : 0;
            // Sanity check
            if (sourceIsArray)
            {
                Debug.Assert(source.dimension == destination.dimension, "MipGenerator source texture does not match dimension of destination!");
            }
            
            int srcMipLevel = 0;
            int srcMipWidth = size.x;
            int srcMipHeight = size.y;
            int slices = destination.volumeDepth;
            
            // Check if format has changed since last time we generated mips
            if (m_TempDownsamplePyramid != null && m_TempDownsamplePyramid.rt.graphicsFormat != destination.graphicsFormat)
            {
                RTHandles.Release(m_TempDownsamplePyramid);
                m_TempDownsamplePyramid = null;
            }
            
            if (m_TempDownsamplePyramid == null)
            {
                m_TempDownsamplePyramid = RTHandles.Alloc(
                    Vector2.one * 0.5f,
                    sourceIsArray ? TextureXR.slices : 1,
                    dimension: source.dimension,
                    filterMode: FilterMode.Bilinear,
                    colorFormat: destination.graphicsFormat,
                    enableRandomWrite: true,
                    useMipMap: false,
                    useDynamicScale: true,
                    name: "Temporary Downsampled Pyramid"
                );

                cmd.SetRenderTarget(m_TempDownsamplePyramid);
                cmd.ClearRenderTarget(false, true, Color.black);
            }
            
            bool isHardwareDrsOn = DynamicResolutionHandler.instance.HardwareDynamicResIsEnabled();
            var hardwareTextureSize = new Vector2Int(source.width, source.height);
            if (isHardwareDrsOn)
                hardwareTextureSize = DynamicResolutionHandler.instance.ApplyScalesOnSize(hardwareTextureSize);

            float sourceScaleX = size.x / (float)hardwareTextureSize.x;
            float sourceScaleY = size.y / (float)hardwareTextureSize.y;
            // Mark! 这里有所修改 为了适配RenderScale
            sourceScaleX = sourceScaleY = 1;
            srcMipWidth = hardwareTextureSize.x;
            srcMipHeight = hardwareTextureSize.y;
            
            // Copies src mip0 to dst mip0
            // Note that we still use a fragment shader to do the first copy because fragment are faster at copying
            // data types like R11G11B10 (default) and pretty similar in term of speed with R16G16B16A16.
            m_PropertyBlock.SetTexture(ShaderIDs._BlitTexture, source);
            m_PropertyBlock.SetVector(ShaderIDs._BlitScaleBias, new Vector4(sourceScaleX, sourceScaleY, 0f, 0f));
            m_PropertyBlock.SetFloat(ShaderIDs._BlitMipLevel, 0f);
            cmd.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
            cmd.SetViewport(new Rect(0, 0, srcMipWidth, srcMipHeight));
            cmd.DrawProcedural(Matrix4x4.identity, Blitter.GetBlitMaterial(source.dimension), 0, MeshTopology.Triangles, 3, 1, m_PropertyBlock);
            
            var finalTargetSize = new Vector2Int(destination.width, destination.height);
            if (destination.useDynamicScale && isHardwareDrsOn)
                finalTargetSize = DynamicResolutionHandler.instance.ApplyScalesOnSize(finalTargetSize);
            
            // Note: smaller mips are excluded as we don't need them and the gaussian compute works
            // on 8x8 blocks
            while (srcMipWidth >= 8 || srcMipHeight >= 8)
            {
                int dstMipWidth = Mathf.Max(1, srcMipWidth >> 1);
                int dstMipHeight = Mathf.Max(1, srcMipHeight >> 1);
                
                cmd.SetComputeVectorParam(m_ColorPyramidCS, ShaderIDs._Size,
                    new Vector4(srcMipWidth, srcMipHeight, 0f, 0f));

                {
                    // Downsample.
                    cmd.SetComputeTextureParam(m_ColorPyramidCS, m_ColorDownsampleKernel, ShaderIDs._Source,
                        destination, srcMipLevel);
                    cmd.SetComputeTextureParam(m_ColorPyramidCS, m_ColorDownsampleKernel, ShaderIDs._Destination,
                        m_TempDownsamplePyramid);
                    cmd.DispatchCompute(m_ColorPyramidCS, m_ColorDownsampleKernel, (dstMipWidth + 7) / 8,
                        (dstMipHeight + 7) / 8, TextureXR.slices);

                    // Single pass blur
                    cmd.SetComputeVectorParam(m_ColorPyramidCS, ShaderIDs._Size,
                        new Vector4(dstMipWidth, dstMipHeight, 0f, 0f));
                    cmd.SetComputeTextureParam(m_ColorPyramidCS, m_ColorGaussianKernel, ShaderIDs._Source,
                        m_TempDownsamplePyramid);
                    cmd.SetComputeTextureParam(m_ColorPyramidCS, m_ColorGaussianKernel, ShaderIDs._Destination,
                        destination, srcMipLevel + 1);
                    cmd.DispatchCompute(m_ColorPyramidCS, m_ColorGaussianKernel, (dstMipWidth + 7) / 8,
                        (dstMipHeight + 7) / 8, TextureXR.slices);
                }

                srcMipLevel++;
                srcMipWidth >>= 1;
                srcMipHeight >>= 1;

                finalTargetSize.x >>= 1;
                finalTargetSize.y >>= 1;
            }
            
            return srcMipLevel + 1;
        }

    }
}