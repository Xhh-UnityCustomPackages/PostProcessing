//  Copyright(c) 2016, Michal Skalsky
//  All rights reserved.
//
//  Redistribution and use in source and binary forms, with or without modification,
//  are permitted provided that the following conditions are met:
//
//  1. Redistributions of source code must retain the above copyright notice,
//     this list of conditions and the following disclaimer.
//
//  2. Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//
//  3. Neither the name of the copyright holder nor the names of its contributors
//     may be used to endorse or promote products derived from this software without
//     specific prior written permission.
//
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
//  EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
//  OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.IN NO EVENT
//  SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
//  SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT
//  OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
//  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
//  TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
//  EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.



Shader "Hidden/PostProcessing/Common/BilateralBlur"
{
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE

        //--------------------------------------------------------------------------------------------
        // Downsample, bilateral blur and upsample config
        //--------------------------------------------------------------------------------------------
        // method used to downsample depth buffer: 0 = min; 1 = max; 2 = min/max in chessboard pattern
        #define DOWNSAMPLE_DEPTH_MODE 2
        #define UPSAMPLE_DEPTH_THRESHOLD 1.5f
        #define BLUR_DEPTH_FACTOR 0.5
        #define GAUSS_BLUR_DEVIATION 1.5
        #define HALF_RES_BLUR_KERNEL_SIZE 6
        #define THIRD_RES_BLUR_KERNEL_SIZE 5
        #define QUARTER_RES_BLUR_KERNEL_SIZE 4
        #define HALF_RES_BLUR_KERNEL_WEIGHT 5.5
        #define THIRD_RES_BLUR_KERNEL_WEIGHT 4.5
        #define QUARTER_RES_BLUR_KERNEL_WEIGHT 3.5
        //--------------------------------------------------------------------------------------------

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

        #define HORIZONTALOFFSET float2(1, 0)
        #define VERTICALOFFSET float2(0, 1)

        TEXTURE2D(_CameraDepthTexture);         SAMPLER(sampler_CameraDepthTexture);
        TEXTURE2D(_QuarterResDepthBuffer);      SAMPLER(sampler_QuarterResDepthBuffer);
        TEXTURE2D(_HalfResColor);               SAMPLER(sampler_HalfResColor);
        TEXTURE2D(_QuarterResColor);            SAMPLER(sampler_QuarterResColor);
        // TEXTURE2D(_MainTex);                    SAMPLER(sampler_MainTex);

        float4 _CameraDepthTexture_TexelSize;
        float4 _QuarterResDepthBuffer_TexelSize;
        
        
        struct v2fUpsample
        {
            float2 uv : TEXCOORD0;
            float2 uv00 : TEXCOORD1;
            float2 uv01 : TEXCOORD2;
            float2 uv10 : TEXCOORD3;
            float2 uv11 : TEXCOORD4;
            float4 vertex : SV_POSITION;
        };

        //-----------------------------------------------------------------------------------------
        // vertUpsample
        //-----------------------------------------------------------------------------------------
        v2fUpsample vertUpsample(Attributes v, float2 texelSize)
        {
            v2fUpsample o;

            float4 pos = GetFullScreenTriangleVertexPosition(v.vertexID);
            float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);

            o.vertex = pos;
            o.uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

            o.uv00 = o.uv - 0.5 * texelSize.xy;
            o.uv10 = o.uv00 + float2(texelSize.x, 0);
            o.uv01 = o.uv00 + float2(0, texelSize.y);
            o.uv11 = o.uv00 + texelSize.xy;
            return o;
        }

        //-----------------------------------------------------------------------------------------
        // BilateralUpsample
        //-----------------------------------------------------------------------------------------
        float4 BilateralUpsample(v2fUpsample input, Texture2D hiDepth, Texture2D loDepth, Texture2D loColor, SamplerState linearSampler, SamplerState pointSampler)
        {
            const float threshold = UPSAMPLE_DEPTH_THRESHOLD;
            float4 highResDepth = LinearEyeDepth(hiDepth.Sample(pointSampler, input.uv), _ZBufferParams).xxxx;
            float4 lowResDepth;

            lowResDepth[0] = LinearEyeDepth(loDepth.Sample(pointSampler, input.uv00), _ZBufferParams);
            lowResDepth[1] = LinearEyeDepth(loDepth.Sample(pointSampler, input.uv10), _ZBufferParams);
            lowResDepth[2] = LinearEyeDepth(loDepth.Sample(pointSampler, input.uv01), _ZBufferParams);
            lowResDepth[3] = LinearEyeDepth(loDepth.Sample(pointSampler, input.uv11), _ZBufferParams);

            float4 depthDiff = abs(lowResDepth - highResDepth);

            float accumDiff = dot(depthDiff, float4(1, 1, 1, 1));

            [branch]
            if (accumDiff < threshold) // small error, not an edge -> use bilinear filter

            {
                return loColor.Sample(linearSampler, input.uv);
            }
            float minDepthDiff = depthDiff[0];
            float2 nearestUv = input.uv00;
            [flatten]
            if (depthDiff[1] < minDepthDiff)
            {
                nearestUv = input.uv10;
                minDepthDiff = depthDiff[1];
            }
            [flatten]
            if (depthDiff[2] < minDepthDiff)
            {
                nearestUv = input.uv01;
                minDepthDiff = depthDiff[2];
            }
            [flatten]
            if (depthDiff[3] < minDepthDiff)
            {
                nearestUv = input.uv11;
                minDepthDiff = depthDiff[3];
            }

            return loColor.Sample(pointSampler, nearestUv);
        }
        //-----------------------------------------------------------------------------------------
        // DownsampleDepth
        //-----------------------------------------------------------------------------------------
        float DownsampleDepth(Varyings input, Texture2D depthTexture, SamplerState depthSampler)
        {
            float4 depth = depthTexture.Gather(depthSampler, input.texcoord);

            #if DOWNSAMPLE_DEPTH_MODE == 0 // min  depth
                return min(min(depth.x, depth.y), min(depth.z, depth.w));
            #elif DOWNSAMPLE_DEPTH_MODE == 1 // max  depth
                return max(max(depth.x, depth.y), max(depth.z, depth.w));
            #elif DOWNSAMPLE_DEPTH_MODE == 2 // min/max depth in chessboard pattern

                float minDepth = min(min(depth.x, depth.y), min(depth.z, depth.w));
                float maxDepth = max(max(depth.x, depth.y), max(depth.z, depth.w));

                // chessboard pattern
                int2 position = input.positionCS.xy % 2;
                int index = position.x + position.y;
                return index == 1 ? minDepth : maxDepth;
            #endif
        }
        
        //-----------------------------------------------------------------------------------------
        // GaussianWeight
        //-----------------------------------------------------------------------------------------
        #define PI 3.14159265359f
        #define GaussianWeight(offset, deviation2) (deviation2.y * exp( - (offset * offset) / (deviation2.x)))
        //-----------------------------------------------------------------------------------------
        // BilateralBlur
        //-----------------------------------------------------------------------------------------
        float4 BilateralBlur(float2 uv, const float2 direction, Texture2D depth, SamplerState depthSampler, const int kernelRadius, const float kernelWeight)
        {
            //const float deviation = kernelRadius / 2.5;
            const float dev = kernelWeight / GAUSS_BLUR_DEVIATION; // make it really strong
            const float dev2 = dev * dev * 2;
            const float2 deviation = float2(dev2, 1.0f / (dev2 * PI));
            float4 centerColor = _BlitTexture.Sample(sampler_LinearClamp, uv);
            float3 color = centerColor.xyz;
            //return float4(color, 1);
            float centerDepth = LinearEyeDepth(depth.Sample(depthSampler, uv), _ZBufferParams);

            float weightSum = 0;

            // gaussian weight is computed from constants only -> will be computed in compile time
            float weight = GaussianWeight(0, deviation);
            color *= weight;
            weightSum += weight;
            
            [unroll] for (int i = -kernelRadius; i < 0; i += 1)
            {
                float2 offset = (direction * i);
                float3 sampleColor = _BlitTexture.Sample(sampler_LinearClamp, uv, offset);
                float sampleDepth = LinearEyeDepth(depth.Sample(depthSampler, uv, offset), _ZBufferParams);

                float depthDiff = abs(centerDepth - sampleDepth);
                float dFactor = depthDiff * BLUR_DEPTH_FACTOR;	//Should be 0.5
                float w = exp( - (dFactor * dFactor));

                // gaussian weight is computed from constants only -> will be computed in compile time
                weight = GaussianWeight(i, deviation) * w;

                color += weight * sampleColor;
                weightSum += weight;
            }

            [unroll] for (i = 1; i <= kernelRadius; i += 1)
            {
                float2 offset = (direction * i);
                float3 sampleColor = _BlitTexture.Sample(sampler_LinearClamp, uv, offset);
                float sampleDepth = LinearEyeDepth(depth.Sample(depthSampler, uv, offset), _ZBufferParams);

                float depthDiff = abs(centerDepth - sampleDepth);
                float dFactor = depthDiff * BLUR_DEPTH_FACTOR;	//Should be 0.5
                float w = exp( - (dFactor * dFactor));
                
                // gaussian weight is computed from constants only -> will be computed in compile time
                weight = GaussianWeight(i, deviation) * w;

                color += weight * sampleColor;
                weightSum += weight;
            }

            color /= weightSum;
            return float4(color, centerColor.w);
        }
        /*
        float4 BilateralBlur(float2 uv, int2 direction, sampler2D depth, const int kernelRadius)
        {
            //const float deviation = kernelRadius / 2.5;
            const float dev = kernelRadius / GAUSS_BLUR_DEVIATION; // make it really strong
            const float dev2 = dev * dev * 2;
            const float2 deviation = float2(dev2, 1.0f / (dev2 * PI));
            float4 centerColor = tex2D(_MainTex, uv);
            float3 color = centerColor.xyz;
            //return float4(color, 1);
            float centerDepth = (LinearEyeDepth(tex2D(depth, uv)));

            float weightSum = 0;

            // gaussian weight is computed from constants only -> will be computed in compile time
            float weight = GaussianWeight(0, deviation);
            color *= weight;
            weightSum += weight;
        
            [unroll] for (int i = -kernelRadius; i < 0; i += 1)
            {
                float2 offset = (direction * i);
                float3 sampleColor = tex2D(_MainTex, uv + offset);
                float sampleDepth = (LinearEyeDepth(tex2D(depth,uv + offset)));

                float depthDiff = abs(centerDepth - sampleDepth);
                float dFactor = depthDiff * BLUR_DEPTH_FACTOR;	//Should be 0.5
                float w = exp(-(dFactor * dFactor));

                // gaussian weight is computed from constants only -> will be computed in compile time
                weight = GaussianWeight(i, deviation) * w;

                color += weight * sampleColor;
                weightSum += weight;
            }

            [unroll] for (i = 1; i <= kernelRadius; i += 1)
            {
                float2 offset = (direction * i);
                float3 sampleColor = tex2D(_MainTex, uv + offset);
                float sampleDepth = (LinearEyeDepth(tex2D(depth, uv + offset)));

                float depthDiff = abs(centerDepth - sampleDepth);
                float dFactor = depthDiff * BLUR_DEPTH_FACTOR;	//Should be 0.5
                float w = exp(-(dFactor * dFactor));
                weight = GaussianWeight(i, deviation) * w;
                color += weight * sampleColor;
                weightSum += weight;
            }

            color /= weightSum;
            return float4(color, centerColor.w);
        }
        */
        ENDHLSL


        // pass 0 - horizontal blur (Quater)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment horizontalFrag
            #pragma target 5.0

            float4 horizontalFrag(Varyings input) : SV_Target
            {
                return BilateralBlur(input.texcoord, HORIZONTALOFFSET, _BlitTexture, sampler_LinearClamp, QUARTER_RES_BLUR_KERNEL_SIZE, QUARTER_RES_BLUR_KERNEL_WEIGHT);
            }

            ENDHLSL
        }

        // pass 1 - vertical blur (Quater)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment verticalFrag
            #pragma target 5.0

            float4 verticalFrag(Varyings input) : SV_Target
            {
                return BilateralBlur(input.texcoord, VERTICALOFFSET, _BlitTexture, sampler_LinearClamp, QUARTER_RES_BLUR_KERNEL_SIZE, QUARTER_RES_BLUR_KERNEL_WEIGHT);
            }

            ENDHLSL
        }

        // pass 2 - horizontal blur (lores)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment horizontalFrag
            #pragma target 5.0

            float4 horizontalFrag(Varyings input) : SV_Target
            {
                return BilateralBlur(input.texcoord, HORIZONTALOFFSET, _QuarterResDepthBuffer, sampler_LinearClamp, HALF_RES_BLUR_KERNEL_SIZE, HALF_RES_BLUR_KERNEL_WEIGHT);
            }

            ENDHLSL
        }

        // pass 3 - vertical blur (lores)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment verticalFrag
            #pragma target 5.0

            float4 verticalFrag(Varyings input) : SV_Target
            {
                return BilateralBlur(input.texcoord, VERTICALOFFSET, _QuarterResDepthBuffer, sampler_LinearClamp, HALF_RES_BLUR_KERNEL_SIZE, HALF_RES_BLUR_KERNEL_WEIGHT);
            }

            ENDHLSL
        }

        // pass 4 - downsample depth to half
        Pass
        {
            Name "downsample depth to half"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            // #pragma target gl4.1
            #pragma target 5.0

            float frag(Varyings input) : SV_Target
            {
                return DownsampleDepth(input, _CameraDepthTexture, sampler_CameraDepthTexture);
            }

            ENDHLSL
        }

        // pass 5 - bilateral upsample
        Pass
        {
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vertUpsampleToFull
            #pragma fragment frag
            #pragma target 5.0

            v2fUpsample vertUpsampleToFull(Attributes v)
            {
                return vertUpsample(v, _BlitTexture_TexelSize);
            }
            float4 frag(v2fUpsample input) : SV_Target
            {
                return BilateralUpsample(input, _CameraDepthTexture, _QuarterResDepthBuffer, _HalfResColor, sampler_HalfResColor, sampler_LinearClamp);
            }

            ENDHLSL
        }

        // pass 6 - downsample depth to quarter
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma target 5.0


            float frag(Varyings input) : SV_Target
            {
                return DownsampleDepth(input, _BlitTexture, sampler_LinearClamp);
            }

            ENDHLSL
        }

        // pass 7 - bilateral upsample quarter to full
        Pass
        {
            Name "bilateral upsample quarter to full"
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vertUpsampleToFull
            #pragma fragment frag
            #pragma target 5.0

            v2fUpsample vertUpsampleToFull(Attributes v)
            {
                return vertUpsample(v, _QuarterResDepthBuffer_TexelSize);
            }
            float4 frag(v2fUpsample input) : SV_Target
            {
                return BilateralUpsample(input, _CameraDepthTexture, _QuarterResDepthBuffer, _QuarterResColor, sampler_QuarterResColor, sampler_QuarterResDepthBuffer);
            }

            ENDHLSL
        }

        // pass 8 - horizontal blur (Third)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment horizontalFrag
            #pragma target 5.0

            float4 horizontalFrag(Varyings input) : SV_Target
            {
                return BilateralBlur(input.texcoord, HORIZONTALOFFSET, _BlitTexture, sampler_LinearClamp, THIRD_RES_BLUR_KERNEL_SIZE, THIRD_RES_BLUR_KERNEL_WEIGHT);
            }

            ENDHLSL
        }

        // pass 9 - vertical blur (Third)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment verticalFrag
            #pragma target 5.0

            float4 verticalFrag(Varyings input) : SV_Target
            {
                return BilateralBlur(input.texcoord, VERTICALOFFSET, _BlitTexture, sampler_LinearClamp, THIRD_RES_BLUR_KERNEL_SIZE, THIRD_RES_BLUR_KERNEL_WEIGHT);
            }

            ENDHLSL
        }
    }
}
