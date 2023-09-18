
Shader "Hidden/PostProcessing/Common/DualBlur"
{
    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    

    float4 _BlitTexture_TexelSize;
    half _Offset;
    
    struct VaryingsDownSample
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 uv01 : TEXCOORD1;
        float4 uv23 : TEXCOORD2;
    };
    
    
    struct VaryingsUpSample
    {
        float4 positionCS : SV_POSITION;
        float4 uv01 : TEXCOORD0;
        float4 uv23 : TEXCOORD1;
        float4 uv45 : TEXCOORD2;
        float4 uv67 : TEXCOORD3;
    };
    
    // -------------------------------------------------------------------------------
    VaryingsDownSample VertDownSample(Attributes input)
    {
        VaryingsDownSample output;
        UNITY_SETUP_INSTANCE_ID(input);

        float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
        float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

        output.positionCS = pos;
        output.uv = uv;
        
        _BlitTexture_TexelSize *= 0.5;
        output.uv01.xy = output.uv - _BlitTexture_TexelSize * float2(1 + _Offset, 1 + _Offset); //top right
        output.uv01.zw = output.uv + _BlitTexture_TexelSize * float2(1 + _Offset, 1 + _Offset); //bottom left
        output.uv23.xy = output.uv - float2(_BlitTexture_TexelSize.x, -_BlitTexture_TexelSize.y) * float2(1 + _Offset, 1 + _Offset); //top left
        output.uv23.zw = output.uv + float2(_BlitTexture_TexelSize.x, -_BlitTexture_TexelSize.y) * float2(1 + _Offset, 1 + _Offset); //bottom right
        
        return output;
    }
    
    half4 FragDownSample(VaryingsDownSample input) : SV_Target
    {
        half4 sum = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv) * 4;
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv01.xy);
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv01.zw);
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv23.xy);
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv23.zw);
        
        return sum * 0.125;
    }
    
    // -------------------------------------------------------------------------------
    VaryingsUpSample VertUpSample(Attributes input)
    {
        VaryingsUpSample output;
        UNITY_SETUP_INSTANCE_ID(input);

        float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
        float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

        output.positionCS = pos;
        
        _BlitTexture_TexelSize *= 0.5;
        _Offset = float2(1 + _Offset, 1 + _Offset);
        
        output.uv01.xy = uv + float2(-_BlitTexture_TexelSize.x * 2, 0) * _Offset;
        output.uv01.zw = uv + float2(-_BlitTexture_TexelSize.x, _BlitTexture_TexelSize.y) * _Offset;
        output.uv23.xy = uv + float2(0, _BlitTexture_TexelSize.y * 2) * _Offset;
        output.uv23.zw = uv + _BlitTexture_TexelSize * _Offset;
        output.uv45.xy = uv + float2(_BlitTexture_TexelSize.x * 2, 0) * _Offset;
        output.uv45.zw = uv + float2(_BlitTexture_TexelSize.x, -_BlitTexture_TexelSize.y) * _Offset;
        output.uv67.xy = uv + float2(0, -_BlitTexture_TexelSize.y * 2) * _Offset;
        output.uv67.zw = uv - _BlitTexture_TexelSize * _Offset;
        
        return output;
    }
    
    half4 FragUpSample(VaryingsUpSample input) : SV_Target
    {
        half4 sum = 0;
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv01.xy);
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv01.zw) * 2;
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv23.xy);
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv23.zw) * 2;
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv45.xy);
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv45.zw) * 2;
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv67.xy);
        sum += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv67.zw) * 2;
        
        return sum * 0.0833;
    }
    
    ENDHLSL
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        
        Pass
        {
            Name "DualBlur DownSample"

            HLSLPROGRAM
            #pragma vertex VertDownSample
            #pragma fragment FragDownSample
            ENDHLSL
        }
        
        Pass
        {
            Name "DualBlur UpSample"

            HLSLPROGRAM
            #pragma vertex VertUpSample
            #pragma fragment FragUpSample
            ENDHLSL
        }
    }
}


