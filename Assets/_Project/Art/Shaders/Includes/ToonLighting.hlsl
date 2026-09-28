#ifndef WASTELAND_TOON_LIGHTING_INCLUDED
#define WASTELAND_TOON_LIGHTING_INCLUDED

// Style guide v1.6 §4: 2 light bands, soft step 0.05-0.1,
// blue-violet tinted shadows (never black), thin rim on units only.

void WastelandToonLighting(
    float3 albedo,
    float3 normalWS,
    float3 viewDirWS,
    float3 lightDirWS,
    float3 lightColor,
    float shadowAttenuation,
    float3 shadowTint,
    float shadowStrength,
    float smoothStep,
    float rimWidth,
    float rimStrength,
    float3 rimColor,
    float enableRim,
    out float3 litColor)
{
    float3 n = normalize(normalWS);
    float3 l = normalize(lightDirWS);
    float3 v = normalize(viewDirWS);

    float nDotL = saturate(dot(n, l));
    float attenuation = nDotL * saturate(shadowAttenuation);
    float edge0 = 0.5 - max(smoothStep, 0.0001) * 0.5;
    float edge1 = 0.5 + max(smoothStep, 0.0001) * 0.5;
    float ramp = smoothstep(edge0, edge1, attenuation);

    float3 shadowColor = albedo * lerp(float3(1.0, 1.0, 1.0), shadowTint, saturate(shadowStrength));
    float3 direct = lerp(shadowColor, albedo * lightColor, ramp);

    float rim = 0.0;
    if (enableRim > 0.5)
    {
        float ndv = saturate(dot(n, v));
        float width = max(rimWidth, 0.001);
        rim = smoothstep(1.0 - width, 1.0, 1.0 - ndv) * rimStrength;
        rim *= lerp(0.35, 1.0, ramp);
    }

    litColor = direct + rimColor * rim;
}

#ifndef WASTELAND_SKIP_SG_WRAPPERS
void ToonLighting_float(
    float3 Albedo,
    float3 NormalWS,
    float3 ViewDirWS,
    float3 LightDirWS,
    float3 LightColor,
    float ShadowAttenuation,
    float3 ShadowTint,
    float ShadowStrength,
    float SmoothStep,
    float RimWidth,
    float RimStrength,
    float3 RimColor,
    float EnableRim,
    out float3 LitColor)
{
    WastelandToonLighting(
        Albedo,
        NormalWS,
        ViewDirWS,
        LightDirWS,
        LightColor,
        ShadowAttenuation,
        ShadowTint,
        ShadowStrength,
        SmoothStep,
        RimWidth,
        RimStrength,
        RimColor,
        EnableRim,
        LitColor);
}
#endif

#endif
