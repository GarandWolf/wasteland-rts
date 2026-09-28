Shipping art: materials, meshes, textures, and production Shader Graph assets.

Layout:

- `Shaders/` — scratch Shader Graph and handwritten HLSL. Not a dump for finished production shaders.
- Add typed folders (`Meshes/`, `Textures/`, `Materials/`, …) when those assets exist.
- Binary formats go through Git LFS (see repo `.gitattributes`).

Throwaway scenes and spawners belong in `Assets/_Project/Prototypes/`, not here.
