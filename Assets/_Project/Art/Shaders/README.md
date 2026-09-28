Shader Graph, handwritten HLSL, and URP ShaderLab prototypes for units.

URP 17.3 is in `Packages/manifest.json`. Shader Graph 17.3 is a **transitive** dependency of that URP pin — do not add `com.unity.shadergraph` to the manifest unless it stops coming in through URP.

## Runtime shaders (compile without the editor)

- `WastelandToonStatic.shader` — vehicles / rigid meshes (`Wasteland/ToonStatic`)
- `WastelandToonVAT.shader` — infantry VAT (`Wasteland/ToonVAT`)
- `ToonOutline.shader` — full-screen depth/normal edge detection
- `Includes/` — team palette, VAT, 2-band toon lighting. Shader Graph Custom Functions should point here (`VAT_float`, `DecodeTeamIndex_float`, `ToonLighting_float`).

Hybrid / DOTS instanced properties: `_TeamIndex`, and on the VAT shader `_AnimIndex` + `_AnimTime`. Globals: `_TeamPaletteTex` (2×16 LUT), `_VATTime`.

## First-open menus

1. **Wasteland/Prototypes/Setup Toon Prototype (First Open)** — URP Forward+ assets, palette PNG, shader graph stubs, placeholder VAT, 5k scene.
2. **Wasteland/Shaders/Generate Toon Shader Graphs** — JSON stubs with Hybrid Per Instance `_TeamIndex` / `_AnimIndex` / `_AnimTime`. Open in Shader Graph, set the URP Lit target, and wire the Custom Function nodes. The handwritten shaders are what the 5k scene uses until that graph is complete.
3. **Wasteland/Art/Generate Team Palette Texture**
4. **Wasteland/VAT/Baker** and **Generate Placeholder Sample**

Keep graphs and scratch materials here. When a graph is ready to ship, move it to a named production folder under `Assets/_Project/Art/` (not into `Prototypes/`).
