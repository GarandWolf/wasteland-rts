Throwaway scenes, spawners, and experiments. Not shipping code.

Home for:

- A 5k-unit Entities Graphics stress scene (Forward+ URP, see `Assets/_Project/Settings/`)
- Scratch spawners, cameras, and debug HUD that must never ship

This folder’s assembly is `Wasteland.Prototypes` (`autoReferenced: false`). **Do not** add that name to any shipping asmdef (`Wasteland.Sim`, `Wasteland.Presentation`, or later gameplay modules). Unity will not auto-pull it into player builds just because a script exists here, but a shipping `references` entry would.

Prototypes may reference Sim and Presentation. The reverse is forbidden.
