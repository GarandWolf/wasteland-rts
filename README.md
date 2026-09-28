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

Open this folder in the Unity Hub with 6000.3.24f1. First open downloads packages (Entities 1.4.8, Entities Graphics 1.4.21, URP 17.3.0, Burst, Collections, Mathematics, Test Framework; Shader Graph 17.3.0 comes in through URP) and regenerates editor files. Create and assign the Forward+ URP assets — see [CONTRIBUTING.md](CONTRIBUTING.md). After import, `Wasteland/Prototypes/Setup Toon Prototype (First Open)` builds the toon/VAT 5k test scene.

## Layout

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 20 Hz lockstep, delayed commands, cm state, squads, PCG32/xxHash64, DOTS mapping
- [`Packages/com.wasteland.sim/`](Packages/com.wasteland.sim/) — simulation core
- [`Assets/_Project/`](Assets/_Project/) — presentation, art/shader prototypes, throwaway Prototypes assembly
- [`src/Wasteland.Sim.Tests/`](src/Wasteland.Sim.Tests/) — xUnit (sim)
- [`src/Wasteland.Presentation.Tests/`](src/Wasteland.Presentation.Tests/) — xUnit (packing / palette, no UnityEngine)

## License

All rights reserved until a license is chosen.
