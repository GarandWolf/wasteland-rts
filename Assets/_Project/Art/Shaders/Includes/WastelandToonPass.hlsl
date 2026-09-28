#ifndef WASTELAND_TOON_PASS_INCLUDED
#define WASTELAND_TOON_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "WastelandToonInput.hlsl"

#define WASTELAND_SKIP_SG_WRAPPERS
#include "TeamPalette.hlsl"
#include "ToonLighting.hlsl"
#ifdef WASTELAND_VAT
#include "VAT.hlsl"
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv0 : TEXCOORD0;
    float2 uv2 : TEXCOORD2;
    float2 uv3 : TEXCOORD3;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv0 : TEXCOORD2;
    float2 hatchUv : TEXCOORD3;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void WastelandToonVertexObject(Attributes input, out float3 positionOS, out float3 normalOS)
{
    positionOS = input.positionOS.xyz;
    normalOS = input.normalOS;
#ifdef WASTELAND_VAT
    WastelandVAT_Apply(
        input.uv3.x,
        _AnimIndex,
        _AnimTime,
        _VATTime,
        _VATPos,
        sampler_VATPos,
        _VATPos_TexelSize,
        _VATNrm,
        sampler_VATNrm,
        _VATClipTex,
        sampler_VATClipTex,
        _VATRowsPerFrame,
        input.positionOS.xyz,
        input.normalOS,
        positionOS,
        normalOS);
#endif
}

Varyings WastelandToonVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionOS;
    float3 normalOS;
    WastelandToonVertexObject(input, positionOS, normalOS);

    VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
    VertexNormalInputs nrmInputs = GetVertexNormalInputs(normalOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS = nrmInputs.normalWS;
    output.uv0 = input.uv0;
    output.hatchUv = input.uv0 * max(_HatchScale, 1.0);
    output.color = input.color;
    return output;
}

float4 WastelandToonFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    WastelandTeamDecode team = WastelandDecodeTeam(asuint(_TeamIndex));
    float3 albedo = _BaseColor.rgb;
    float4 mask = _UseVertexColorMask > 0.5
        ? input.color
        : SAMPLE_TEXTURE2D(_TeamMaskTex, sampler_TeamMaskTex, input.uv0);

    if (!team.valid)
    {
        return float4(WASTELAND_DEBUG_MAGENTA, 1.0);
    }

    float3 primary;
    float3 accent;
    WastelandSamplePalette(_TeamPaletteTex, sampler_TeamPaletteTex, team.row, primary, accent);
    albedo = WastelandApplyTeamMask(
        albedo,
        mask,
        primary,
        accent,
        team.pips,
        team.hatch,
        _WeatheringColor.rgb,
        input.hatchUv);

    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

    float3 lit;
    WastelandToonLighting(
        albedo,
        input.normalWS,
        viewDirWS,
        mainLight.direction,
        mainLight.color,
        mainLight.shadowAttenuation * mainLight.distanceAttenuation,
        _ShadowTint.rgb,
        _ShadowStrength,
        _SmoothStep,
        _RimWidth,
        _RimStrength,
        _RimColor.rgb,
        _EnableRim,
        lit);

    return float4(lit, 1.0);
}

Varyings WastelandToonShadowVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionOS;
    float3 normalOS;
    WastelandToonVertexObject(input, positionOS, normalOS);

    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);
    Light mainLight = GetMainLight();
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, mainLight.direction));
#if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return output;
}

float4 WastelandToonShadowFrag(Varyings input) : SV_Target
{
    return 0;
}

Varyings WastelandToonDepthVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionOS;
    float3 normalOS;
    WastelandToonVertexObject(input, positionOS, normalOS);
    output.positionCS = TransformObjectToHClip(positionOS);
    output.normalWS = TransformObjectToWorldNormal(normalOS);
    return output;
}

float4 WastelandToonDepthFrag(Varyings input) : SV_Target
{
    return 0;
}

float4 WastelandToonDepthNormalsFrag(Varyings input) : SV_Target
{
    float3 n = NormalizeNormalPerPixel(input.normalWS);
    return float4(n, 0.0);
}

#endif
