Shader "Hidden/PostProcessing/CRTScreen"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            // 0
            Name "CRTScreen"

            HLSLPROGRAM
            #include "CRTScreenPass.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
