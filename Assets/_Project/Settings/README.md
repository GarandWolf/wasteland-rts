Create the URP pipeline assets here on first open (see CONTRIBUTING.md). Do not check in a half-authored YAML pipeline — Unity 6.3 URP assets are editor-generated.

Expected names after first open:

- `WastelandURP.asset` — Universal Render Pipeline Asset (URP 17.3)
- `WastelandURP_Renderer.asset` — Universal Renderer, **Rendering Path = Forward+**

Entities Graphics 1.4 requires URP **Forward+**. Other URP paths (Forward, Deferred) and the Built-in pipeline are not supported. HDRP is not in this project.

Assign `WastelandURP` in:

1. Project Settings > Graphics (default / scriptable render pipeline)
2. Project Settings > Quality (the pipeline slot on every quality level)
