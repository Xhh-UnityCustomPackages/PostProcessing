#ifndef DECLARE_NORMAL_INCLUDED
#define DECLARE_NORMAL_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

#define  _DEFERRED_RENDERING_PATH

#ifdef _DEFERRED_RENDERING_PATH
    TEXTURE2D_X_HALF(_GBuffer2); // encoded-normal    encoded-normal  encoded-normal  smoothness
#else
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#endif


void GetNormal(uint2 positionSS, out float3 N)
{
    #ifdef _DEFERRED_RENDERING_PATH
    half4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, positionSS);
    N = normalize(UnpackNormal(gbuffer2));
    #else
    N = LoadSceneNormals(positionSS);
    #endif
}

#endif