# wasteland-rts

Post-apocalyptic RTS on real-world maps. Unity, PC/Steam first. Working title.

Players build defenses and assault neighboring territories on 100×100 or 200×200 mile slices of real terrain (elevation, roads, towns). Large unit counts. Single-player vs AI at launch; lockstep multiplayer afterwards, on the same simulation core.

## Status

Foundation: Unity 6.3 LTS project skeleton, a deterministic simulation assembly with no `UnityEngine` dependency (including integer road/grid pathfinding), and a `dotnet test` suite that proves a scripted match hashes identically when replayed.

## Requirements

- [Unity 6.3 LTS](https://unity.com/releases/unity-6) **6000.3.24f1** (editor, for the game client)
- .NET SDK 8+ (for the sim tests; no editor required)
- Git LFS (`git lfs install`)

## Quick start

```bash
dotnet test Wasteland.Sim.sln
```

Open this folder in the Unity Hub with 6000.3.24f1. First open downloads packages (Entities 1.4.8, Entities Graphics 1.4.21, URP 17.3.0, Burst, Collections, Mathematics, Test Framework; Shader Graph 17.3.0 comes in through URP) and regenerates editor files. Create and assign the Forward+ URP assets — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Layout

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 20 Hz lockstep, delayed commands, cm state, battalions, PCG32/xxHash64, DOTS mapping
- [`docs/movement.md`](docs/movement.md) — sim-owned pathfinding
- [`docs/formats/`](docs/formats/) — WRMB map container (roads + terrain)
- [`Packages/com.wasteland.sim/`](Packages/com.wasteland.sim/) — simulation core
- [`Assets/_Project/`](Assets/_Project/) — presentation, art/shader prototypes, throwaway Prototypes assembly
- [`data/golden/`](data/golden/) — Columbia Gorge 100×100 mile WRMB goldens (Git LFS; v0 + v1)
- [`docs/map/wrmb-layout-v0.md`](docs/map/wrmb-layout-v0.md) — WRMB v0 map container (main + terrain)
- [`docs/map/wrmb-layout-v1.md`](docs/map/wrmb-layout-v1.md) — WRMB v1 map container (stable keys, WTRA)
- [`tools/map/wrmb_reader.py`](tools/map/wrmb_reader.py) — standalone WRMB reader/validator (format 0 and 1)
- [`src/Wasteland.Sim.Tests/`](src/Wasteland.Sim.Tests/) — xUnit
- [`src/Wasteland.Sim.Benchmarks/`](src/Wasteland.Sim.Benchmarks/) — pathfinding micro-benchmark (not a CI gate)

## License

All rights reserved until a license is chosen.
