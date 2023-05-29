

Shader "Hidden/PostProcessing/TemporalAntialiasingLite"
{
    HLSLINCLUDE
    #include "TemporalAntialiasingLite.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "TemporalAntialiasingLite"

            HLSLPROGRAM
            #pragma multi_compile_local_fragment _ _USEMOTIONVECTOR

            #pragma vertex VertTemporal
            #pragma fragment FragTemporal
            ENDHLSL
        }
    }
}
