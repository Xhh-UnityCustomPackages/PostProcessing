using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public class ScreenSpaceShadowsPostPass : ScriptableRenderPass
    {
        public ScreenSpaceShadowsPostPass()
        {
            profilingSampler = new ProfilingSampler("ScreenSpaceShadows Post");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                ShadowData shadowData = renderingData.shadowData;
                int cascadesCount = shadowData.mainLightShadowCascadesCount;
                bool mainLightShadows = renderingData.shadowData.supportsMainLightShadows;
                bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
                bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

                // Before transparent object pass, force to disable screen space shadow of main light
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);

                // then enable main light shadows with or without cascades
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}