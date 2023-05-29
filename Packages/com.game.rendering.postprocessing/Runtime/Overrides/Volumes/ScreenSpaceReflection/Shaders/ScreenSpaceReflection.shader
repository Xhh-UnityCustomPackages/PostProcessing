Shader "Hidden/PostProcessing/ScreenSpaceReflection"
{
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off


        Pass
        {
            Name "ScreenSpaceReflection Test"

            HLSLPROGRAM
            
            ENDHLSL
        }
    }
}
