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
            public static readonly int _SourceTex = Shader.PropertyToID("_BlitTexture");
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
            
            RenderTextureDescriptor desc = new(source.rt.width, source.rt.height, source.rt.format);
            desc.volumeDepth = 1;
            desc.bindMS = false;
            desc.msaaSamples = 1;
            desc.dimension = TextureDimension.Tex2D;

            RTHandle last = source;
            
            var iterations = Mathf.Clamp(iterationsLimit, 1, kMaxIterations);

            //down
            for (int level = 0; level < iterations; level++)
            {
                RenderingUtils.ReAllocateHandleIfNeeded(ref s_TempDown[level], desc, FilterMode.Point, name: $"_BlurPyramid_Down_{level}");
                RenderingUtils.ReAllocateHandleIfNeeded(ref s_TemmpUp[level], desc, FilterMode.Point, name: $"_BlurPyramid_Up_{level}");

                var downPassTarget = iterations == 1 ? target : s_TempDown[level];
                var currentTarget = level == 0 ? source : last;
                cmd.SetGlobalTexture(BlurShaderIDs._SourceTex, currentTarget);
                cmd.Blit(currentTarget, downPassTarget, s_BlitMaterial, (int)PassIndex.Down);

                last = s_TempDown[level];
                desc.width = Mathf.Max(Mathf.FloorToInt(desc.width / 2), 1);
                desc.height = Mathf.Max(Mathf.FloorToInt(desc.height / 2), 1);
            }

            //up
            for (int level = iterations - 2; level >= 0; level--)
            {
                // m_BlurMaterial.SetTexture("_SourceTex", last); //不能这么设置 因为cmd是异步的
                cmd.SetGlobalTexture(BlurShaderIDs._SourceTex, last);
                cmd.Blit(last, s_TemmpUp[level], s_BlitMaterial, (int)PassIndex.Up);
                last = s_TemmpUp[level];
            }
            
            cmd.Blit(last, target, s_BlitMaterial, 1);
        }
    }
}
