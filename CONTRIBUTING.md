# Contributing

## Repo layout

```
Assets/_Project/          Unity presentation, scenes, authoring
  Art/                    Shipping art (materials, meshes, textures)
  Art/Shaders/            Shader Graph / HLSL prototypes
  Prototypes/             Throwaway scenes (5k-unit test, scratch)
  Settings/               URP pipeline + renderer assets (created on first open)
  Presentation/           View / interpolation / UI
  Scenes/                 Shipping scenes (Bootstrap on first open)
Packages/com.wasteland.sim/   Deterministic sim (UPM embedded package, no UnityEngine)
src/Wasteland.Sim/        dotnet wrapper csproj that compiles the package sources
src/Wasteland.Sim.Tests/  xUnit, runs on CI without the Unity editor
src/Wasteland.Sim.Benchmarks/  pathfinding micro-benchmark (`dotnet run -c Release`, not CI)
docs/                     Architecture, movement, blob formats
docs/map/                 WRMB v0 and v1 layout specs
data/golden/              Columbia Gorge WRMB goldens (Git LFS; v0 + v1)
tools/map/                Standalone WRMB reader/validator and stable-id check (Python stdlib)
ProjectSettings/          Hand-authored Unity 6.3 LTS skeleton
```

Unity auto-generated `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, and editor `.csproj` files are gitignored. The **headless** solution `Wasteland.Sim.sln` is tracked.

## Assemblies and namespaces

One assembly definition per module. No cross-talk except downward:

| Asmdef | Namespace | UnityEngine | Notes |
| --- | --- | --- | --- |
| `Wasteland.Sim` | `Wasteland.Sim.*` | **Forbidden** (`noEngineReferences`) | Commands, battalions, cm state, PCG32, xxHash64 |
| `Wasteland.Presentation` | `Wasteland.Presentation` | Allowed | View, interpolation, UI |
| `Wasteland.Prototypes` | `Wasteland.Prototypes` | Allowed | Throwaway only. `autoReferenced: false`. Shipping asmdefs must not reference it. |

Future modules follow the same pattern: `Wasteland.Sim.Pathing`, `Wasteland.Presentation.Ui`, each with its own asmdef. Editor-only code lives in `*.Editor.asmdef` with `"includePlatforms": ["Editor"]`.

Root namespace for Unity-generated scripts is `Wasteland` (see Editor Settings).

## Folder conventions

- Runtime scripts sit next to their asmdef.
- Do not dump loose scripts under `Assets/`.
- Shipping art goes under `Assets/_Project/Art/{Type}/...`; binary formats are Git LFS (see `.gitattributes`).
- Compiled map blobs (`.wrmb`) live under `data/golden/` and are Git LFS. Keep `ATTRIBUTION.txt` next to the data.
- Shader Graph and handwritten HLSL **prototypes** live in `Assets/_Project/Art/Shaders/`. Production shaders move to a named folder under `Art/` once they leave scratch.
- Throwaway scenes (the 5k-unit Entities Graphics stress test, spawners) live in `Assets/_Project/Prototypes/`. That folder has its own asmdef; do not mix it with shipping presentation or sim.
- Tests that need the editor (EditMode/PlayMode) will live under `Assets/_Project/Tests/` later. Logic tests belong in `src/Wasteland.Sim.Tests` so CI can run them without a Unity license.

## Simulation rules (non-negotiable)

1. Sim **state**, **hashes**, and **events** are integers: centimeters (`int32`), squared distances (`int64`), parts-per-thousand. No `float` / `double` / `Fixed64` stored there.
2. `Fixed64` is internal math only (sqrt, stepping, angles). Convert to cm before writing state.
3. No `UnityEngine` in `Wasteland.Sim`.
4. All state changes go through `SimCommand` (`issuedTick + commandDelayTicks`). Move targets **battalions**.
5. Iterate battalions/units in id order. Do not rely on dictionary enumeration.
6. RNG is PCG32 (`SimRandom`). Hashing is xxHash64. Both have golden tests.
7. If two runs with the same `MatchConfig` and command log disagree on `HashState()`, it is a bug.

Read [ARCHITECTURE.md](docs/ARCHITECTURE.md) before adding systems.

## First open in Unity

Open the **repository root** in Unity Hub with **6000.3.24f1**. This skeleton was hand-authored (no editor on the machines that created it), so the first open is a real import, not a no-op:

1. Install Git LFS (`git lfs install`) before pulling large art later.
2. Let Package Manager download Entities 1.4.8, Entities Graphics 1.4.21, Universal RP 17.3.0, Burst 1.8.30, Collections 2.6.8, Mathematics 1.3.3, and the Test Framework. Shader Graph 17.3.0 is **not** in `Packages/manifest.json`; URP 17.3.0 pulls it transitively (same major.minor as the Unity 6.3 core SRP line). Package Manager will write `Packages/packages-lock.json` and may bump patch versions — commit that lockfile once it looks right.
3. Unity will create `Library/`, extra `ProjectSettings/` YAML (Quality, Graphics, Physics, …), Burst AOT settings, and gitignored `.csproj` files from the asmdefs. That is expected.
4. Create `Assets/_Project/Scenes/Bootstrap.unity` (3D), save it, add it as the first scene in File > Build Settings.
5. You will likely get an Input System prompt; this repo already requests the new input backend in Player Settings. Confirm it.
6. **Create and assign the URP pipeline.** Linear color space is already set. Pipeline YAML is editor-generated — do not check in a hand-authored half-configured SRP. In `Assets/_Project/Settings/`:
   1. Create > Rendering > URP Asset (with Universal Renderer). Name the pipeline `WastelandURP` and the renderer `WastelandURP_Renderer`.
   2. Select `WastelandURP_Renderer` and set **Rendering Path = Forward+**. Entities Graphics 1.4 supports URP **Forward+ only** (not Built-in, not URP Forward, not Deferred). HDRP is not in this project.
   3. Project Settings > Graphics: assign `WastelandURP` as the default render pipeline.
   4. Project Settings > Quality: assign `WastelandURP` on every quality level.
7. Enter Play Mode once on Bootstrap so Entities/Burst can JIT. There is no gameplay yet; `SimFixedUpdateDriver` is the presentation-clock example. Shader prototypes go in `Assets/_Project/Art/Shaders/`; the 5k-unit throwaway scene goes in `Assets/_Project/Prototypes/`.

Do not treat `ProjectSettings/ProjectSettings.asset` as sacred after first open — Unity will fill in missing serialized fields. Keep `m_SerializationMode: 2` (Force Text) and Visible Meta Files.

## Rendering package pins (Unity 6000.3.24f1)

| Package | Version | Why this pin |
| --- | --- | --- |
| `com.unity.entities` | 1.4.8 | Already in the skeleton |
| `com.unity.entities.graphics` | 1.4.21 | Changelog 1.4.21 (2026-07-10) updates the Entities dependency to 1.4.8; `package.json` depends on `com.unity.entities` 1.4.8. Latest 1.4 line on the Unity registry. |
| `com.unity.render-pipelines.universal` | 17.3.0 | Unity 6.3 **core** package (changelog: compatible with 6000.3.0b1; 6000.3 docs: core packages are fixed to the editor line). |
| `com.unity.shadergraph` | 17.3.0 (transitive) | Direct dependency of URP on the 17.x line. Not duplicated in the manifest. |

## Tooling

```bash
git lfs install          # once per machine
dotnet test Wasteland.Sim.sln
dotnet run -c Release --project src/Wasteland.Sim.Benchmarks   # pathfinding micro-benchmark, not CI
(cd data/golden && sha256sum -c golden-ci-columbia-gorge-100.v0.wrmb.sha256 --strict \
  && sha256sum -c golden-ci-columbia-gorge-100.terrain.v0.wrmb.sha256 --strict \
  && sha256sum -c golden-ci-columbia-gorge-100.v1.wrmb.sha256 --strict \
  && sha256sum -c golden-ci-columbia-gorge-100.terrain.v1.wrmb.sha256 --strict)
python3 tools/map/wrmb_reader.py \
  data/golden/golden-ci-columbia-gorge-100.v0.wrmb \
  --terrain data/golden/golden-ci-columbia-gorge-100.terrain.v0.wrmb
python3 tools/map/wrmb_reader.py \
  data/golden/golden-ci-columbia-gorge-100.v1.wrmb \
  --terrain data/golden/golden-ci-columbia-gorge-100.terrain.v1.wrmb
python3 tools/map/stable_ids.py \
  --check data/golden/golden-ci-columbia-gorge-100.v1.wrmb \
  --against data/golden/golden-ci-columbia-gorge-100.v0.wrmb \
  --manifest data/golden/stable-ids.json
```

Unity: **Unity 6.3 LTS** (`6000.3.24f1`). Open the repo root as the project. First open regenerates `Library/`, `Packages/packages-lock.json`, extra `ProjectSettings/` YAML, and `.meta` files for any assets that shipped without them.

## Pull requests

- Keep sim changes covered by a `dotnet test` case, especially anything that can desync (math, command apply, iteration order).
- Do not commit `Library/`, editor `.csproj`, or license secrets.
- Do not add `Wasteland.Prototypes` to a shipping asmdef.
- Unity GameCI is not in this repository yet; see [docs/CI.md](docs/CI.md).
