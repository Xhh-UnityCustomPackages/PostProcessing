using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Game.Core.PostProcessing
{
    public sealed class BlueNoise
    {
        // Structure that holds all the dithered sampling texture that shall be binded at dispatch time.
        internal struct DitheredTextureSet
        {
            public RTHandle owenScrambled256Tex;
            public RTHandle scramblingTile;
            public RTHandle rankingTile;
            public RTHandle scramblingTex;
        }
        
        // Structure that holds the Render Graph handles to the dithered sampling texture that have been loaded
        // at a given Render Graph execution using ImportResources()
        internal struct DitheredTextureHandleSet
        {
            public TextureHandle owenScrambled256Tex;
            public TextureHandle scramblingTile;
            public TextureHandle rankingTile;
            public TextureHandle scramblingTex;
        }
        
        static DitheredTextureSet m_DitheredTextureSet1SPP;
        static DitheredTextureSet m_DitheredTextureSet8SPP;
        static DitheredTextureSet m_DitheredTextureSet256SPP;
        
        internal DitheredTextureSet DitheredTextureSet1SPP() => m_DitheredTextureSet1SPP;
        internal DitheredTextureSet DitheredTextureSet8SPP() => m_DitheredTextureSet8SPP;
        internal DitheredTextureSet DitheredTextureSet256SPP() => m_DitheredTextureSet256SPP;
        
        public BlueNoise(PostProcessFeatureRuntimeTextures textures)
        {
            m_DitheredTextureSet1SPP = new DitheredTextureSet
            {
                owenScrambled256Tex = RTHandles.Alloc(textures.owenScrambled256Tex),
                scramblingTile      = RTHandles.Alloc(textures.scramblingTile1SPP),
                rankingTile         = RTHandles.Alloc(textures.rankingTile1SPP),
                scramblingTex       = RTHandles.Alloc(textures.scramblingTex)
            };                        

            m_DitheredTextureSet8SPP = new DitheredTextureSet
            {
                owenScrambled256Tex = RTHandles.Alloc(textures.owenScrambled256Tex),
                scramblingTile      = RTHandles.Alloc(textures.scramblingTile8SPP),
                rankingTile         = RTHandles.Alloc(textures.rankingTile8SPP),
                scramblingTex       = RTHandles.Alloc(textures.scramblingTex)
            };
            
            // m_DitheredTextureSet256SPP = new DitheredTextureSet
            // {
            //     owenScrambled256Tex = RTHandles.Alloc(textures.owenScrambled256Tex),
            //     scramblingTile      = RTHandles.Alloc(textures.scramblingTile256SPP),
            //     rankingTile         = RTHandles.Alloc(textures.rankingTile256SPP),
            //     scramblingTex       = RTHandles.Alloc(textures.scramblingTex)
            // };
        }

        public void Cleanup()
        {
        }
        
        internal static DitheredTextureHandleSet ImportSetToRenderGraph(RenderGraph renderGraph, DitheredTextureSet ditheredTextureSet)
        {
            var handles = new DitheredTextureHandleSet();

            handles.owenScrambled256Tex = renderGraph.ImportTexture(ditheredTextureSet.owenScrambled256Tex);
            handles.scramblingTile = renderGraph.ImportTexture(ditheredTextureSet.scramblingTile);
            handles.rankingTile = renderGraph.ImportTexture(ditheredTextureSet.rankingTile);
            handles.scramblingTex = renderGraph.ImportTexture(ditheredTextureSet.scramblingTex);

            return handles;
        }

        internal static void BindDitheredTextureSet(CommandBuffer cmd, DitheredTextureSet ditheredTextureSet)
        {
            cmd.SetGlobalTexture(PipelineShaderIDs._OwenScrambledTexture, ditheredTextureSet.owenScrambled256Tex);
            cmd.SetGlobalTexture(PipelineShaderIDs._ScramblingTileXSPP, ditheredTextureSet.scramblingTile);
            cmd.SetGlobalTexture(PipelineShaderIDs._RankingTileXSPP, ditheredTextureSet.rankingTile);
            cmd.SetGlobalTexture(PipelineShaderIDs._ScramblingTexture, ditheredTextureSet.scramblingTex);
        }
        
        public static void BindDitheredRNGData1SPP(CommandBuffer cmd)
        {
            BindDitheredTextureSet(cmd, m_DitheredTextureSet1SPP);
        }

        public static void BindDitheredRNGData8SPP(CommandBuffer cmd)
        {
            BindDitheredTextureSet(cmd, m_DitheredTextureSet8SPP);
        }
    }
}