#ifndef WASTELAND_VAT_INCLUDED
#define WASTELAND_VAT_INCLUDED

// Packed _AnimIndex (team-color-spec-v2 §2.4.1):
// bits 0-5 clip, 6 hold, 7 valid, 8-15 speed*64, 16-31 reserved.
// C# stores the packed uint as float bits (math.asfloat); this shader reads asuint.
// globalTime is wrapped interpolated presentation/sim time (_VATTime), not Time.deltaTime.

static const uint WASTELAND_VAT_CLIP_MASK = 63u;
static const uint WASTELAND_VAT_HOLD_BIT = 6u;
static const uint WASTELAND_VAT_VALID_BIT = 7u;
static const uint WASTELAND_VAT_SPEED_SHIFT = 8u;
static const uint WASTELAND_VAT_SPEED_MASK = 255u;
static const float WASTELAND_VAT_WRAP = 3600.0;

void WastelandVAT_Apply(
    float vertexIndex,
    float animIndexBits,
    float animTime,
    float globalTime,
    Texture2D posTex,
    SamplerState posSamp,
    float4 posTexelSize,
    Texture2D nrmTex,
    SamplerState nrmSamp,
    Texture2D clipTex,
    SamplerState clipSamp,
    float rowsPerFrame,
    float3 bindPos,
    float3 bindNrm,
    out float3 posOS,
    out float3 nrmOS)
{
    uint a = asuint(animIndexBits);
    if ((a & (1u << WASTELAND_VAT_VALID_BIT)) == 0u)
    {
        posOS = bindPos;
        nrmOS = bindNrm;
        return;
    }

    uint clip = a & WASTELAND_VAT_CLIP_MASK;
    bool hold = ((a >> WASTELAND_VAT_HOLD_BIT) & 1u) != 0u;
    float speed = ((a >> WASTELAND_VAT_SPEED_SHIFT) & WASTELAND_VAT_SPEED_MASK) / 64.0;

    float2 clipUv = float2((clip + 0.5) / 64.0, 0.5);
    float4 c = SAMPLE_TEXTURE2D_LOD(clipTex, clipSamp, clipUv, 0);
    float frames = max(c.y, 1.0);
    float fps = max(c.z, 1.0);
    bool loop = c.w > 0.5 && !hold;

    float elapsed = fmod(globalTime - animTime + WASTELAND_VAT_WRAP, WASTELAND_VAT_WRAP);
    float f = elapsed * speed * fps;
    f = loop ? fmod(f, frames) : min(f, frames - 1.0);

    float f0 = floor(f);
    float f1 = loop ? fmod(f0 + 1.0, frames) : min(f0 + 1.0, frames - 1.0);
    float w = saturate(f - f0);

    float width = max(posTexelSize.z, 1.0);
    float col = fmod(vertexIndex, width);
    float sub = floor(vertexIndex / width);
    float rpf = max(rowsPerFrame, 1.0);

    float u = (col + 0.5) * posTexelSize.x;
    float v0 = (c.x + f0 * rpf + sub + 0.5) * posTexelSize.y;
    float v1 = (c.x + f1 * rpf + sub + 0.5) * posTexelSize.y;

    float3 p0 = SAMPLE_TEXTURE2D_LOD(posTex, posSamp, float2(u, v0), 0).xyz;
    float3 p1 = SAMPLE_TEXTURE2D_LOD(posTex, posSamp, float2(u, v1), 0).xyz;
    float3 n0 = SAMPLE_TEXTURE2D_LOD(nrmTex, nrmSamp, float2(u, v0), 0).xyz;
    float3 n1 = SAMPLE_TEXTURE2D_LOD(nrmTex, nrmSamp, float2(u, v1), 0).xyz;

    posOS = lerp(p0, p1, w);
    nrmOS = normalize(lerp(n0, n1, w));
}

#ifndef WASTELAND_SKIP_SG_WRAPPERS
void VAT_float(
    float VertexIndex,
    float AnimIndexBits,
    float AnimTime,
    float GlobalTime,
    UnityTexture2D PosTex,
    UnityTexture2D NrmTex,
    UnityTexture2D ClipTex,
    float RowsPerFrame,
    float3 BindPos,
    float3 BindNrm,
    out float3 PosOS,
    out float3 NrmOS)
{
    WastelandVAT_Apply(
        VertexIndex,
        AnimIndexBits,
        AnimTime,
        GlobalTime,
        PosTex.tex,
        PosTex.samplerstate,
        PosTex.texelSize,
        NrmTex.tex,
        NrmTex.samplerstate,
        ClipTex.tex,
        ClipTex.samplerstate,
        RowsPerFrame,
        BindPos,
        BindNrm,
        PosOS,
        NrmOS);
}
#endif

#endif
