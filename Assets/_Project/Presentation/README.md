Presentation assembly: UnityEngine-facing view, interpolation, UI, camera, and unit rendering.

Owns team-color and VAT **render** components (`TeamColorIndex`, `VatClipState`, `VatStartTime`), the global VAT clock, the faction LUT, and the full-screen toon outline feature. None of that is sim state.

This assembly may reference `Wasteland.Sim`. `Wasteland.Sim` must never reference this assembly.

Do **not** reference `Wasteland.Prototypes`. Throwaway scenes and the 5k-unit test live under `Assets/_Project/Prototypes/` with their own asmdef (`autoReferenced: false`).
