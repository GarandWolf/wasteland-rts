Presentation assembly: UnityEngine-facing view, interpolation, UI, camera, and unit rendering.

Owns team-color and VAT **render** components (`TeamColorIndex`, `VatClipState`, `VatStartTime` as float IEEE bits), `GameClock`, the global VAT clock (interpolated sim time or GameClock), the faction LUT, checked-in toon materials, and the full-screen toon outline feature. None of that is sim state.

This assembly may reference `Wasteland.Sim`. `Wasteland.Sim` must never reference this assembly.

Do **not** reference `Wasteland.Prototypes`. Throwaway scenes and the 5k-unit test live under `Assets/_Project/Prototypes/` with their own asmdef (`autoReferenced: false`).
