Shipping art: materials, meshes, textures, and production ShaderLab/HLSL.

Layout:

- `Shaders/` — handwritten URP ShaderLab (`Wasteland/ToonStatic`, `Wasteland/ToonVAT`, outline) and shared HLSL. Optional Shader Graph wrappers later; ShaderLab is the runtime path.
- `Materials/` — checked-in `M_Toon_Static` and `M_Toon_VAT` (referenced by the 5k spawn; shaders are also Always Included).
- Add typed folders (`Meshes/`, `Textures/`, …) when those assets exist.
- Binary formats go through Git LFS (see repo `.gitattributes`).

Throwaway scenes and spawners belong in `Assets/_Project/Prototypes/`, not here.
