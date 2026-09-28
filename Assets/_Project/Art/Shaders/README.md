Handwritten URP ShaderLab plus shared HLSL. These are the production unit shaders for Entities Graphics.

URP 17.3 is in `Packages/manifest.json`. Shader Graph 17.3 is a **transitive** dependency of that URP pin — do not add `com.unity.shadergraph` to the manifest unless it stops coming in through URP. Graphs may wrap the same includes later; they are not a runtime path.

## Runtime shaders (compile without the editor)

- `WastelandToonStatic.shader` — vehicles / rigid meshes (`Wasteland/ToonStatic`)
- `WastelandToonVAT.shader` — infantry VAT (`Wasteland/ToonVAT`)
- `ToonOutline.shader` — full-screen depth/normal edge detection (URP `SampleSceneDepth` / `SampleSceneNormals`)
- `Includes/` — team palette, VAT, 2-band toon lighting. Optional Shader Graph Custom Functions can point here (`VAT_float`, `DecodeTeamIndex_float`, `ToonLighting_float`).

Hybrid / DOTS instanced properties: `_TeamIndex`, and on the VAT shader `_AnimIndex` + `_AnimTime`, stored as float IEEE bits of packed uints (`math.asfloat` / shader `asuint`). Globals: `_TeamPaletteTex` (2×16 LUT), `_VATTime` (wrapped interpolated presentation/sim time).

Checked-in materials: `Assets/_Project/Art/Materials/M_Toon_Static.mat` and `M_Toon_VAT.mat`. First-open also adds the three shaders to Always Included Shaders.

## First-open menus

1. **Wasteland/Prototypes/Setup Toon Prototype (First Open)** — URP Forward+ (`RenderingMode.ForwardPlus`), palette PNG, Always Included Shaders, placeholder VAT, 5k scene with GameClock + material refs.
2. **Wasteland/Art/Generate Team Palette Texture**
3. **Wasteland/VAT/Baker** and **Generate Placeholder Sample**

There is no Shader Graph generator. Keep scratch here. When a material is ready to ship, it already lives under `Assets/_Project/Art/Materials/`.
