#ifndef SCREEN_SPACE_OCCLUSION_INPUT_INCLUDED
#define SCREEN_SPACE_OCCLUSION_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

TEXTURE2D_X(_SourceTex);
TEXTURE2D_FLOAT(_MaskDepthRT);          SAMPLER(sampler_MaskDepthRT);

float4 _Full_TexelSize;
float4 _Scaled_TexelSize;
float4 _TargetScale;
float4 _UVToView;
float4x4 _WorldToCameraMatrix;
float _Radius;
float _RadiusToScreen;
float _MaxRadiusPixels;
float _InvRadius2;
float _AngleBias;
float _AOMultiplier;
float _Intensity;
float _Thickness;
float _MaxDistance;
float _DistanceFalloff;
float _BlurSharpness;

float4x4 _CameraProjMatrix;

#endif // SCREEN_SPACE_OCCLUSION_INPUT_INCLUDED
