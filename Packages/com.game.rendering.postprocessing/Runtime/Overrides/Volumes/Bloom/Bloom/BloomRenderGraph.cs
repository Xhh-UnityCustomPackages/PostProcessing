using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public partial class BloomRenderer : PostProcessVolumeRenderer<Bloom>
    {
        private class BloomRendererPassData
        {
            // Setup
            internal int mipCount;
            public Material material;
            internal Material[] upsampleMaterials;
            
            internal TextureHandle[] bloomMipUp;
            internal TextureHandle[] bloomMipDown;
            
            internal TextureHandle sourceTexture;
            internal TextureHandle targetTexture;

            internal ProfilingSampler profilingSampler_Prefilter;
            internal ProfilingSampler profilingSampler_Downsample;
            internal ProfilingSampler profilingSampler_Upsample;
            internal ProfilingSampler profilingSampler_Combine;

            internal bool antiFlick;
        }
        
        TextureHandle[] _BloomMipUp;
        TextureHandle[] _BloomMipDown;
        Material[] m_UpsampleMaterials;
        
        private ProfilingSampler m_ProfilingSampler_Setup;
        private ProfilingSampler m_ProfilingSampler_Prefilter;
        private ProfilingSampler m_ProfilingSampler_Downsample;
        private ProfilingSampler m_ProfilingSampler_Upsample;
        private ProfilingSampler m_ProfilingSampler_Combine;

        public override void InitProfilingSampler()
        {
            base.InitProfilingSampler();
            m_ProfilingSampler_Setup = new ("Bloom Setup");
            m_ProfilingSampler_Prefilter = new ("Bloom Prefilter");
            m_ProfilingSampler_Downsample = new ("Bloom Downsample");
            m_ProfilingSampler_Upsample = new ("Bloom Upsample");
            m_ProfilingSampler_Combine = new("Bloom Combine");
        }

        public override void DoRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            SetupMaterials();
            
            if (_BloomMipUp == null) _BloomMipUp = new TextureHandle[k_MaxPyramidSize];
            if (_BloomMipDown == null) _BloomMipDown = new TextureHandle[k_MaxPyramidSize];
            if (m_UpsampleMaterials == null)
            {
                m_UpsampleMaterials = new Material[k_MaxPyramidSize];
                for (int i = 0; i < k_MaxPyramidSize; i++)
                {
                    m_UpsampleMaterials[i] = GetMaterial(postProcessFeatureData.shaders.bloomPS);
                }
            }

            var desc = cameraData.cameraTargetDescriptor;
            GetCompatibleDescriptor(ref desc, desc.graphicsFormat);

            int downres = 1;
            switch (settings.downscale.value)
            {
                case BloomDownscaleMode.Half:
                    downres = 1;
                    break;
                case BloomDownscaleMode.Quarter:
                    downres = 2;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException();
            }

            int tw = desc.width >> downres;
            int th = desc.height >> downres;

            // Determine the iteration count
            int maxSize = Mathf.Max(tw, th);
            int iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
            int mipCount = Mathf.Clamp(iterations, 1, settings.maxIterations.value);

            using (new ProfilingScope(m_ProfilingSampler_Setup))
            {
                // Create bloom mip pyramid textures
                {
                    _BloomMipDown[0] = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipDown[0].name, false, FilterMode.Bilinear);
                    _BloomMipUp[0] = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipUp[0].name, false, FilterMode.Bilinear);
                    
                    for (int i = 1; i < mipCount; i++)
                    {
                        tw = Mathf.Max(1, tw >> 1);
                        th = Mathf.Max(1, th >> 1);
                        ref TextureHandle mipDown = ref _BloomMipDown[i];
                        ref TextureHandle mipUp = ref _BloomMipUp[i];

                        desc.width = tw;
                        desc.height = th;
                        
                        // NOTE: Reuse RTHandle names for TextureHandles
                        mipDown = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipUp[i].name, false);
                        mipUp = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipDown[i].name, false);
                    }
                }
            }
            
            // Setup bloom on uber
            var tint = settings.tint.value.linear;
            var luma = ColorUtils.Luminance(tint);
            tint = luma > 0f ? tint * (1f / luma) : Color.white;

            var bloomParams = new Vector4(settings.intensity.value, tint.r, tint.g, tint.b);
            m_Material.SetVector(ShaderConstants._Bloom_Params, bloomParams);
            m_Material.SetFloat(ShaderConstants._Bloom_RGBM, m_UseRGBM ? 1f : 0f);

            using (var builder = renderGraph.AddUnsafePass<BloomRendererPassData>(profilingSampler.name, out var passData))
            {
                passData.mipCount = mipCount;
                passData.material = m_Material;
                passData.upsampleMaterials = m_UpsampleMaterials;
                passData.bloomMipDown = _BloomMipDown;
                passData.bloomMipUp = _BloomMipUp;
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);
                passData.targetTexture = destination;
                builder.UseTexture(destination, AccessFlags.Write);
                passData.profilingSampler_Prefilter = m_ProfilingSampler_Prefilter;
                passData.profilingSampler_Downsample = m_ProfilingSampler_Downsample;
                passData.profilingSampler_Upsample = m_ProfilingSampler_Upsample;
                passData.profilingSampler_Combine = m_ProfilingSampler_Combine;
                passData.antiFlick = settings.antiFlick.value;
                
                // TODO RENDERGRAPH: properly setup dependencies between passes
                builder.AllowPassCulling(false);
                
                for (int i = 0; i < mipCount; i++)
                {
                    builder.UseTexture(_BloomMipDown[i], AccessFlags.ReadWrite);
                    builder.UseTexture(_BloomMipUp[i], AccessFlags.ReadWrite);
                }

                builder.SetRenderFunc(static (BloomRendererPassData data, UnsafeGraphContext context) =>
                {
                    // TODO: can't call BlitTexture with unsafe command buffer
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    var material = data.material;
                    int mipCount = data.mipCount;
                    
                    var loadAction = RenderBufferLoadAction.DontCare;   // Blit - always write all pixels
                    var storeAction = RenderBufferStoreAction.Store;    // Blit - always read by then next Blit

                    // Prefilter
                    using(new ProfilingScope(cmd, data.profilingSampler_Prefilter))
                    {
                        Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.bloomMipDown[0], loadAction, storeAction, material, 0);
                    }
                    
                    // Downsample - gaussian pyramid
                    // Classic two pass gaussian blur - use mipUp as a temporary target
                    //   First pass does 2x downsampling + 9-tap gaussian
                    //   Second pass does 9-tap gaussian using a 5-tap filter + bilinear filtering
                    using (new ProfilingScope(cmd, data.profilingSampler_Downsample))
                    {
                        TextureHandle lastDown = data.bloomMipDown[0];
                        for (int i = 1; i < mipCount; i++)
                        {
                            TextureHandle mipDown = data.bloomMipDown[i];
                            TextureHandle mipUp = data.bloomMipUp[i];

                            if (data.antiFlick)
                            {
                                Blitter.BlitCameraTexture(cmd, lastDown, mipUp, loadAction, storeAction, material, 5);
                                Blitter.BlitCameraTexture(cmd, mipUp, mipDown, loadAction, storeAction, material, 2);
                            }
                            else
                            {
                                Blitter.BlitCameraTexture(cmd, lastDown, mipUp, loadAction, storeAction, material, 1);
                                Blitter.BlitCameraTexture(cmd, mipUp, mipDown, loadAction, storeAction, material, 2);
                            }

                            lastDown = mipDown;
                        }
                    }

                    using (new ProfilingScope(cmd, data.profilingSampler_Upsample))
                    {
                        // Upsample (bilinear by default, HQ filtering does bicubic instead
                        for (int i = mipCount - 2; i >= 0; i--)
                        {
                            TextureHandle lowMip = (i == mipCount - 2) ? data.bloomMipDown[i + 1] : data.bloomMipUp[i + 1];
                            TextureHandle highMip = data.bloomMipDown[i];
                            TextureHandle dst = data.bloomMipUp[i];

                            // We need a separate material for each upsample pass because setting the low texture mip source
                            // gets overriden by the time the render func is executed.
                            // Material is a reference, so all the blits would share the same material state in the cmdbuf.
                            // NOTE: another option would be to use cmd.SetGlobalTexture().
                            var upMaterial = data.upsampleMaterials[i];
                            upMaterial.SetTexture(ShaderConstants._SourceTexLowMip, lowMip);
                            if (i == 0)
                                material.SetTexture(ShaderConstants._SourceTexLowMip, lowMip);
                            Blitter.BlitCameraTexture(cmd, highMip, dst, loadAction, storeAction, upMaterial, 3);
                        }
                    }

                    using (new ProfilingScope(cmd, data.profilingSampler_Combine))
                    {
                        Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.targetTexture, loadAction, storeAction, material, 4);
                    }
                });
            }
        }
    }
}