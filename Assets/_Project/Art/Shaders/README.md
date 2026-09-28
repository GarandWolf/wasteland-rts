Shader Graph and handwritten HLSL prototypes for art.

URP 17.3 is in `Packages/manifest.json`. Shader Graph 17.3 is a **transitive** dependency of that URP pin — do not add `com.unity.shadergraph` to the manifest unless it stops coming in through URP.

Keep graphs and scratch materials here. When a graph is ready to ship, move it to a named production folder under `Assets/_Project/Art/` (not into `Prototypes/`).
