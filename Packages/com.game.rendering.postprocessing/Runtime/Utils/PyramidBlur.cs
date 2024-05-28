using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public static class PyramidBlur
    {
        const int kMaxIterations = 8;
        static RTHandle[] s_TempDown;
        static RTHandle[] s_TemmpUp;
        enum PassIndex
        {
            Down = 0,
            Up = 1
        }

        static Material s_BlitMaterial;

        static class BlurShaderIDs
        {
            public static readonly int _SourceTex = Shader.PropertyToID("_SourceTex");
            public static readonly int _Offset = Shader.PropertyToID("_Offset");
        }


        public static void Initialize(Material dualBlur)
        {
            s_BlitMaterial = Material.Instantiate(dualBlur);
            if (s_TempDown == null)
                s_TempDown = new RTHandle[kMaxIterations];

            if (s_TemmpUp == null)
                s_TemmpUp = new RTHandle[kMaxIterations];
        }

        public static void Release()
        {
            CoreUtils.Destroy(s_BlitMaterial);
            for (int i = 0; i < s_TempDown.Length; i++)
            {
                if (s_TempDown[i] != null)
                    s_TempDown[i].Release();
                s_TempDown[i] = null;
            }

            for (int i = 0; i < s_TemmpUp.Length; i++)
            {
                if (s_TemmpUp[i] != null)
                    s_TemmpUp[i].Release();
                s_TemmpUp[i] = null;
            }
        }




        public static void ComputeBlurPyramid(CommandBuffer cmd, RTHandle source, RTHandle target, float blurRadius, int iterationsLimit = 2)
        {
            s_BlitMaterial.SetFloat(BlurShaderIDs._Offset, blurRadius);

            RenderTextureDescriptor desc = new();
            desc.colorFormat = target.rt.format;

            RTHandle last = source;
            float scale = 1;


            var scaledViewportSize = source.GetScaledSize(source.rtHandleProperties.currentViewportSize);
            var logh = Mathf.Log(Mathf.Max(scaledViewportSize.x, scaledViewportSize.y), 2) + blurRadius - 8;
            var logh_i = (int)logh;
            var iterations = Mathf.Clamp(logh_i, 2, kMaxIterations);
            iterations = Mathf.Min(iterations, iterationsLimit);
            Debug.LogError($"iterations {iterations} {logh_i} {logh}");

            //down
            for (int level = 0; level < iterations; level++)
            {
                scale *= 0.5f;
                desc.width = (int)(source.rt.width * scale);
                desc.height = (int)(source.rt.height * scale);

                RenderingUtils.ReAllocateIfNeeded(ref s_TempDown[level], desc, FilterMode.Point, name: $"_BlurPyramid_Down_{level}");


                var downPassTarget = iterations == 1 ? target : s_TempDown[level];
                var currentTarget = level == 0 ? source : last;
                cmd.SetGlobalTexture(BlurShaderIDs._SourceTex, currentTarget);
                cmd.Blit(currentTarget, downPassTarget, s_BlitMaterial, (int)PassIndex.Down);

                last = s_TempDown[level];
            }

            //up
            for (int level = iterations - 1; level >= 0; level--)
            {
                if (level != 0)
                {
                    desc.width = s_TempDown[level].rt.width * 2;
                    desc.height = s_TempDown[level].rt.height * 2;
                    RenderingUtils.ReAllocateIfNeeded(ref s_TemmpUp[level], desc, FilterMode.Point, name: $"_BlurPyramid_Up_{level}");
                }

                // m_BlurMaterial.SetTexture("_SourceTex", last); //不能这么设置 因为cmd是异步的
                cmd.SetGlobalTexture(BlurShaderIDs._SourceTex, last);
                if (level == 0)
                {
                    cmd.Blit(last, target, s_BlitMaterial, (int)PassIndex.Up);
                }
                else
                {
                    cmd.Blit(last, s_TemmpUp[level], s_BlitMaterial, (int)PassIndex.Up);
                }

                last = s_TemmpUp[level];
            }
        }
    }
}
