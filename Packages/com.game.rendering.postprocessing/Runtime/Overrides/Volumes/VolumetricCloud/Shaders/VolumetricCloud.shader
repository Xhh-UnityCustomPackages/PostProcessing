Shader "Hidden/PostProcessing/VolumetricCloud"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

    TEXTURE2D_X(_SourceTex);        float4 _SourceTex_TexelSize;
    float3 _BoundsMin, _BoundsMax;
    float _Step;

    // return 相机到容器的距离 和 返回光线是否在容器中
    //                      边界框最小值       边界框最大值     //世界相机位置      光线方向倒数
    float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 invRaydir)
    {
        float3 t0 = (boundsMin - rayOrigin) * invRaydir;
        float3 t1 = (boundsMax - rayOrigin) * invRaydir;
        float3 tmin = min(t0, t1);
        float3 tmax = max(t0, t1);

        float dstA = max(max(tmin.x, tmin.y), tmin.z); //进入点
        float dstB = min(tmax.x, min(tmax.y, tmax.z)); //出去点

        float dstToBox = max(0, dstA);
        float dstInsideBox = max(0, dstB - dstToBox);
        return float2(dstToBox, dstInsideBox);
    }

    float CloudRayMarching(float3 startPoint, float3 direction)
    {
        float3 testPoint = startPoint;
        float sum = 0.0;
        direction *= 0.5;//每次步进间隔
        //步进总长度
        for (int i = 0; i < 256; i++)
        {
            testPoint += direction;
            if (testPoint.x < 10.0 && testPoint.x > - 10.0 &&
            testPoint.z < 10.0 && testPoint.z > - 10.0 &&
            testPoint.y < 10.0 && testPoint.y > - 10.0)
                sum += 0.01;
        }
        return sum;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Off ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "Volumetric Cloud"

            HLSLPROGRAM

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    //  调整 Z 以匹配 OpenGL 的 NDC ([-1, 1])
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                float3 posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                // return float4(posWS, 1);
                
                float3 rayPos = _WorldSpaceCameraPos;
                //相机到每个像素的世界方向
                float3 worldViewDir = normalize(posWS.xyz - rayPos.xyz);
                
                float depthEyeLinear = length(posWS - _WorldSpaceCameraPos);
                float2 rayToContainerInfo = rayBoxDst(_BoundsMin, _BoundsMax, rayPos, (1 / worldViewDir));
                float dstToBox = rayToContainerInfo.x; //相机到容器的距离
                float dstInsideBox = rayToContainerInfo.y; //返回光线是否在容器中
                //相机到物体的距离 - 相机到容器的距离，这里跟 光线是否在容器中 取最小，过滤掉一些无效值
                float dstLimit = min(depthEyeLinear - dstToBox, dstInsideBox);
                // return dstLimit;

                float sumDensity = 0;
                float _dstTravelled = 0;
                for (int j = 0; j < 32; j++)
                {
                    if (dstLimit > _dstTravelled) //被遮住时步进跳过

                    {
                        sumDensity += 0.05;
                        if (sumDensity > 1)
                            break;
                    }
                    _dstTravelled += _Step; //每次步进长度

                }

                return sumDensity;
                
                float cloud = CloudRayMarching(_WorldSpaceCameraPos, worldViewDir);
                return cloud * cloud;
            }

            ENDHLSL
        }
    }
}

