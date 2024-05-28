Shader "Hidden/PostProcessing/Moebius"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            // 0
            Name "Moebius Sobel Depth"

            HLSLPROGRAM
            #include "MoebiusPass.hlsl"
            #pragma vertex Vert
            #pragma fragment FragSobelDepth
            ENDHLSL
        }

        Pass
        {
            // 1
            Name "Moebius Sobel Depth"

            HLSLPROGRAM
            #include "MoebiusPass.hlsl"
            #pragma vertex Vert
            #pragma fragment FragSobelNormal
            ENDHLSL
        }

        Pass
        {
            // 2
            Name "Moebius Sobel Combine"

            HLSLPROGRAM
            #include "MoebiusPass.hlsl"
            #pragma vertex Vert
            #pragma fragment FragCombine
            ENDHLSL
        }
    }
}
