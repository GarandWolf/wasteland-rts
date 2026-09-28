Throwaway scenes, spawners, and experiments. Not shipping code.

Home for:

- The 5k-unit Entities Graphics toon/VAT stress scene (`Wasteland/Prototypes/Create 5k Toon Test Scene`)
- Placeholder infantry VAT (idle / walk / fire) and procedural meshes
- Clip-churn driver, camera rig, and §6 HUD
- `VatBaker` editor window (`Wasteland/VAT/Baker`)

This folder’s assembly is `Wasteland.Prototypes` (`autoReferenced: false`). **Do not** add that name to any shipping asmdef (`Wasteland.Sim`, `Wasteland.Presentation`, or later gameplay modules). Unity will not auto-pull it into player builds just because a script exists here, but a shipping `references` entry would.

Prototypes may reference Sim and Presentation. The reverse is forbidden.

Play the 5k scene only after first-open URP setup (Forward+). See CONTRIBUTING.md and the PR checklist.
