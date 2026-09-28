#ifndef WASTELAND_TEAM_PALETTE_INCLUDED
#define WASTELAND_TEAM_PALETTE_INCLUDED

// Packed _TeamIndex layout (team-color-spec-v2 §2.2):
// bits 0-1 faction, 2-3 shade, 4-5 pips, 6 hatch, 7 valid, 8-31 reserved.

static const uint WASTELAND_TEAM_FACTION_MASK = 3u;
static const uint WASTELAND_TEAM_SHADE_SHIFT = 2u;
static const uint WASTELAND_TEAM_SHADE_MASK = 3u;
static const uint WASTELAND_TEAM_PIPS_SHIFT = 4u;
static const uint WASTELAND_TEAM_PIPS_MASK = 3u;
static const uint WASTELAND_TEAM_HATCH_BIT = 6u;
static const uint WASTELAND_TEAM_VALID_BIT = 7u;

struct WastelandTeamDecode
{
    uint faction;
    uint shade;
    uint pips;
    bool hatch;
    bool valid;
    uint row;
};

WastelandTeamDecode WastelandDecodeTeam(uint packed)
{
    WastelandTeamDecode d;
    d.faction = packed & WASTELAND_TEAM_FACTION_MASK;
    d.shade = (packed >> WASTELAND_TEAM_SHADE_SHIFT) & WASTELAND_TEAM_SHADE_MASK;
    d.pips = (packed >> WASTELAND_TEAM_PIPS_SHIFT) & WASTELAND_TEAM_PIPS_MASK;
    d.hatch = ((packed >> WASTELAND_TEAM_HATCH_BIT) & 1u) != 0u;
    d.valid = ((packed >> WASTELAND_TEAM_VALID_BIT) & 1u) != 0u;
    d.row = d.faction * 4u + d.shade;
    return d;
}

// 2x16 sRGB LUT: row = faction*4+shade, col 0 = primary, col 1 = accent.
void WastelandSamplePalette(
    Texture2D paletteTex,
    SamplerState paletteSamp,
    uint row,
    out float3 primary,
    out float3 accent)
{
    float2 texel = float2(1.0 / 2.0, 1.0 / 16.0);
    float u0 = 0.5 * texel.x;
    float u1 = 1.5 * texel.x;
    float v = (row + 0.5) * texel.y;
    primary = SAMPLE_TEXTURE2D_LOD(paletteTex, paletteSamp, float2(u0, v), 0).rgb;
    accent = SAMPLE_TEXTURE2D_LOD(paletteTex, paletteSamp, float2(u1, v), 0).rgb;
}

// R = primary team area, G = accent pattern, B = pip zones (0.5 bar 1, 1.0 bar 2),
// A = weathering over team paint. Pip zone is drawn when zone <= pips.
float3 WastelandApplyTeamMask(
    float3 albedo,
    float4 mask,
    float3 primary,
    float3 accent,
    uint pips,
    bool hatch,
    float3 weatheringColor,
    float2 hatchUv)
{
    float3 color = albedo;
    color = lerp(color, primary, saturate(mask.r));
    color = lerp(color, accent, saturate(mask.g));

    float pipZone = 0.0;
    if (pips >= 1u && mask.b > 0.25 && mask.b < 0.75)
    {
        pipZone = 1.0;
    }
    if (pips >= 2u && mask.b >= 0.75)
    {
        pipZone = 1.0;
    }
    color = lerp(color, accent, pipZone);

    if (hatch)
    {
        float h = abs(frac((hatchUv.x + hatchUv.y) * 18.0) - 0.5);
        float hatchMask = step(h, 0.12) * saturate(mask.r + mask.g);
        color = lerp(color, color * 0.45, hatchMask);
    }

    color = lerp(color, weatheringColor, saturate(mask.a));
    return color;
}

static const float3 WASTELAND_DEBUG_MAGENTA = float3(1.0, 0.0, 1.0);

#ifndef WASTELAND_SKIP_SG_WRAPPERS
void DecodeTeamIndex_float(
    float TeamIndexBits,
    out float Faction,
    out float Shade,
    out float Pips,
    out float Hatch,
    out float Valid,
    out float Row)
{
    WastelandTeamDecode d = WastelandDecodeTeam(asuint(TeamIndexBits));
    Faction = (float)d.faction;
    Shade = (float)d.shade;
    Pips = (float)d.pips;
    Hatch = d.hatch ? 1.0 : 0.0;
    Valid = d.valid ? 1.0 : 0.0;
    Row = (float)d.row;
}

void SampleTeamPalette_float(
    UnityTexture2D PaletteTex,
    float Row,
    out float3 Primary,
    out float3 Accent)
{
    WastelandSamplePalette(PaletteTex.tex, PaletteTex.samplerstate, (uint)Row, Primary, Accent);
}

void ApplyTeamMask_float(
    float3 Albedo,
    float4 Mask,
    float3 Primary,
    float3 Accent,
    float Pips,
    float Hatch,
    float3 WeatheringColor,
    float2 HatchUV,
    float Valid,
    out float3 OutColor)
{
    if (Valid < 0.5)
    {
        OutColor = WASTELAND_DEBUG_MAGENTA;
        return;
    }

    OutColor = WastelandApplyTeamMask(
        Albedo,
        Mask,
        Primary,
        Accent,
        (uint)Pips,
        Hatch > 0.5,
        WeatheringColor,
        HatchUV);
}
#endif

#endif
