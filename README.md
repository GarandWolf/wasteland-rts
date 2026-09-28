# wasteland-rts

Post-apocalyptic RTS on real-world maps. Unity, PC/Steam first. Working title.

Players build defenses and assault neighboring territories on 100×100 or 200×200 mile slices of real terrain (elevation, roads, towns). Large unit counts. Single-player vs AI at launch; lockstep multiplayer afterwards, on the same simulation core.

## Status

Foundation only: Unity 6.3 LTS project skeleton, a deterministic simulation assembly with no `UnityEngine` dependency, and a `dotnet test` suite that proves a scripted match hashes identically when replayed.

## Requirements

- [Unity 6.3 LTS](https://unity.com/releases/unity-6) **6000.3.24f1** (editor, for the game client)
- .NET SDK 8+ (for the sim tests; no editor required)
- Git LFS (`git lfs install`)

## Quick start

```bash
dotnet test Wasteland.Sim.sln
```

Open this folder in the Unity Hub with 6000.3.24f1. First open downloads packages (Entities, Burst, Collections, Mathematics, Test Framework) and regenerates editor files — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Layout

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — sim/presentation split, ticks, commands, fixed-point, hashing, DOTS mapping, risks
- [`Packages/com.wasteland.sim/`](Packages/com.wasteland.sim/) — simulation core
- [`Assets/_Project/`](Assets/_Project/) — presentation
- [`src/Wasteland.Sim.Tests/`](src/Wasteland.Sim.Tests/) — xUnit

## License

All rights reserved until a license is chosen.
