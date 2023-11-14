Shader "Hidden/PostProcessing/PostProcessingDebugPass"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
    #include "Packages/com.game.rendering.postprocessing/ShaderLibrary/Debug/DebugViewEnums.cs.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            // 0
            Name "PostProcessingDebugPass"

            HLSLPROGRAM
            #pragma multi_compile_fragment _ POSTPROCESS_DEBUG_DISPLAY

            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D(_DebugTextureNoStereo);
            half4 _DebugTextureDisplayRect;

            int _DebugFullScreenMode;
            int _HiZMipMapLevel;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                half4 finalColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);

                #if defined(POSTPROCESS_DEBUG_DISPLAY)
                    half4 debugColor = 0;
                    float2 uvOffset = half2(uv.x - _DebugTextureDisplayRect.x, uv.y - _DebugTextureDisplayRect.y);
                    if (
                        (uvOffset.x >= 0) && (uvOffset.x < _DebugTextureDisplayRect.z) &&
                        (uvOffset.y >= 0) && (uvOffset.y < _DebugTextureDisplayRect.w)
                        )
                    {
                        float2 debugTextureUv = float2(uvOffset.x / _DebugTextureDisplayRect.z, uvOffset.y / _DebugTextureDisplayRect.w);
                        
                        // switch(_DebugFullScreenMode)
                        // {
                        //     case DEBUGFULLSCREENMODE_HI_Z: debugColor = SAMPLE_TEXTURE2D_LOD(_DebugTextureNoStereo, sampler_PointClamp, debugTextureUv, _HiZMipMapLevel);
                        //     default: debugColor = SAMPLE_TEXTURE2D(_DebugTextureNoStereo, sampler_PointClamp, debugTextureUv);
                        // }

                        debugColor = SAMPLE_TEXTURE2D_LOD(_DebugTextureNoStereo, sampler_PointClamp, debugTextureUv, _HiZMipMapLevel);

                        return debugColor;
                    }
                #endif

                return finalColor;
            }

            ENDHLSL
        }
    }
}
