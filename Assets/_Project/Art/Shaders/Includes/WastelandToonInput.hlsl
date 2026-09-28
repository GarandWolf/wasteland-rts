#ifndef WASTELAND_TOON_INPUT_INCLUDED
#define WASTELAND_TOON_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _ShadowTint;
    float4 _RimColor;
    float4 _WeatheringColor;
    float4 _TeamMaskTex_ST;
    float _TeamIndex;
#ifdef WASTELAND_VAT
    float4 _VATPos_ST;
    float4 _VATNrm_ST;
    float4 _VATClipTex_ST;
    float _AnimIndex;
    float _AnimTime;
    float _VATRowsPerFrame;
#endif
    float _SmoothStep;
    float _ShadowStrength;
    float _RimWidth;
    float _RimStrength;
    float _EnableRim;
    float _UseVertexColorMask;
    float _HatchScale;
    float _Cull;
    float _Cutoff;
CBUFFER_END

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float, _TeamIndex)
#ifdef WASTELAND_VAT
    UNITY_DOTS_INSTANCED_PROP(float, _AnimIndex)
    UNITY_DOTS_INSTANCED_PROP(float, _AnimTime)
#endif
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#define _TeamIndex UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _TeamIndex)
#ifdef WASTELAND_VAT
#define _AnimIndex UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _AnimIndex)
#define _AnimTime UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _AnimTime)
#endif
#endif

TEXTURE2D(_TeamMaskTex);
SAMPLER(sampler_TeamMaskTex);

TEXTURE2D(_TeamPaletteTex);
SAMPLER(sampler_TeamPaletteTex);

float _VATTime;

#ifdef WASTELAND_VAT
TEXTURE2D(_VATPos);
SAMPLER(sampler_VATPos);
float4 _VATPos_TexelSize;
TEXTURE2D(_VATNrm);
SAMPLER(sampler_VATNrm);
TEXTURE2D(_VATClipTex);
SAMPLER(sampler_VATClipTex);
#endif

#endif
