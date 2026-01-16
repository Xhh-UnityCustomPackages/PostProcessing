using System;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: PostProcessFeatureRuntimeTextures", Order = 2001), HideInInspector]
    public class PostProcessFeatureRuntimeTextures : IRenderPipelineResources
    {
        public int version => 0;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
        
        [SerializeField][ResourcePath("Runtime/RenderPipelineResources/Texture/CoherentNoise/OwenScrambledNoise256.png")]
        private Texture2D m_OwenScrambled256Tex;
        public Texture2D owenScrambled256Tex
        {
            get => m_OwenScrambled256Tex;
            set => this.SetValueAndNotify(ref m_OwenScrambled256Tex, value);
        }
        
        [SerializeField][ResourcePath("Runtime/RenderPipelineResources/Texture/CoherentNoise/ScrambleNoise.png")]
        private Texture2D m_ScramblingTex;
        public Texture2D scramblingTex
        {
            get => m_ScramblingTex;
            set => this.SetValueAndNotify(ref m_ScramblingTex, value);
        }
        
        [SerializeField][ResourcePath("Runtime/RenderPipelineResources/Texture/CoherentNoise/ScramblingTile1SPP.png")]
        private Texture2D m_ScramblingTile1SPP;
        public Texture2D scramblingTile1SPP
        {
            get => m_ScramblingTile1SPP;
            set => this.SetValueAndNotify(ref m_ScramblingTile1SPP, value);
        }
        
        [SerializeField][ResourcePath("Runtime/RenderPipelineResources/Texture/CoherentNoise/ScramblingTile8SPP.png")]
        private Texture2D m_ScramblingTile8SPP;
        public Texture2D scramblingTile8SPP
        {
            get => m_ScramblingTile8SPP;
            set => this.SetValueAndNotify(ref m_ScramblingTile8SPP, value);
        }
        
        [SerializeField][ResourcePath("Runtime/RenderPipelineResources/Texture/CoherentNoise/RankingTile1SPP.png")]
        private Texture2D m_RankingTile1SPP;
        public Texture2D rankingTile1SPP
        {
            get => m_RankingTile1SPP;
            set => this.SetValueAndNotify(ref m_RankingTile1SPP, value);
        }
        
        [SerializeField][ResourcePath("Runtime/RenderPipelineResources/Texture/CoherentNoise/RankingTile8SPP.png")]
        private Texture2D m_RankingTile8SPP;
        public Texture2D rankingTile8SPP
        {
            get => m_RankingTile8SPP;
            set => this.SetValueAndNotify(ref m_RankingTile8SPP, value);
        }
    }
}