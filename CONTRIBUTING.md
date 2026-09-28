# Contributing

## Repo layout

```
Assets/_Project/          Unity presentation, scenes, authoring
Packages/com.wasteland.sim/   Deterministic sim (UPM embedded package, no UnityEngine)
src/Wasteland.Sim/        dotnet wrapper csproj that compiles the package sources
src/Wasteland.Sim.Tests/  xUnit, runs on CI without the Unity editor
docs/                     Architecture and process
ProjectSettings/          Hand-authored Unity 6.3 LTS skeleton
```

Unity auto-generated `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, and editor `.csproj` files are gitignored. The **headless** solution `Wasteland.Sim.sln` is tracked.

## Assemblies and namespaces

One assembly definition per module. No cross-talk except downward:

| Asmdef | Namespace | UnityEngine | Notes |
| --- | --- | --- | --- |
| `Wasteland.Sim` | `Wasteland.Sim.*` | **Forbidden** (`noEngineReferences`) | Commands, Fix64, world, hash |
| `Wasteland.Presentation` | `Wasteland.Presentation` | Allowed | View, interpolation, UI |

Future modules follow the same pattern: `Wasteland.Sim.Pathing`, `Wasteland.Presentation.Ui`, each with its own asmdef. Editor-only code lives in `*.Editor.asmdef` with `"includePlatforms": ["Editor"]`.

Root namespace for Unity-generated scripts is `Wasteland` (see Editor Settings).

## Folder conventions

- Runtime scripts sit next to their asmdef.
- Do not dump loose scripts under `Assets/`.
- Art goes under `Assets/_Project/Art/{Type}/...` once it exists; binary formats are Git LFS (see `.gitattributes`).
- Tests that need the editor (EditMode/PlayMode) will live under `Assets/_Project/Tests/` later. Logic tests belong in `src/Wasteland.Sim.Tests` so CI can run them without a Unity license.

## Simulation rules (non-negotiable)

1. No `float` / `double` in sim state. Use `Fix64`.
2. No `UnityEngine` in `Wasteland.Sim`.
3. All state changes go through `SimCommand` scheduled for a tick.
4. Iterate units in `UnitId` order. Do not rely on dictionary enumeration order.
5. RNG is `SimRandom` only.
6. If two runs with the same seed and command log disagree on `HashState()`, it is a bug.

Read [ARCHITECTURE.md](docs/ARCHITECTURE.md) before adding systems.

## First open in Unity

Open the **repository root** in Unity Hub with **6000.3.24f1**. This skeleton was hand-authored (no editor on the machines that created it), so the first open is a real import, not a no-op:

1. Install Git LFS (`git lfs install`) before pulling large art later.
2. Let Package Manager download Entities 1.4.8, Burst 1.8.30, Collections 2.6.8, Mathematics 1.3.3, and the Test Framework. It will write `Packages/packages-lock.json` and may bump patch versions — commit that lockfile once it looks right.
3. Unity will create `Library/`, extra `ProjectSettings/` YAML (Quality, Graphics, Physics, …), Burst AOT settings, and gitignored `.csproj` files from the asmdefs. That is expected.
4. Create `Assets/_Project/Scenes/Bootstrap.unity` (3D), save it, add it as the first scene in File > Build Settings.
5. You will likely get an Input System prompt; this repo already requests the new input backend in Player Settings. Confirm it.
6. Rendering: Linear color space is set. URP is **not** in the manifest yet — add Universal RP in the editor when you are ready for a pipeline asset, rather than checking in a half-configured SRP.
7. Enter Play Mode once on Bootstrap so Entities/Burst can JIT. There is no gameplay yet; `SimFixedUpdateDriver` is the presentation-clock example.

Do not treat `ProjectSettings/ProjectSettings.asset` as sacred after first open — Unity will fill in missing serialized fields. Keep `m_SerializationMode: 2` (Force Text) and Visible Meta Files.

## Tooling

```bash
git lfs install          # once per machine
dotnet test Wasteland.Sim.sln
```

Unity: **Unity 6.3 LTS** (`6000.3.24f1`). Open the repo root as the project. First open regenerates `Library/`, `Packages/packages-lock.json`, extra `ProjectSettings/` YAML, and `.meta` files for any assets that shipped without them.

## Pull requests

- Keep sim changes covered by a `dotnet test` case, especially anything that can desync (math, command apply, iteration order).
- Do not commit `Library/`, editor `.csproj`, or license secrets.
- Unity GameCI is not in this repository yet; see [docs/CI.md](docs/CI.md).
