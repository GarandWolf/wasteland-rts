# Wasteland.Sim

Embedded UPM package containing the deterministic RTS simulation.

Compiled two ways:

1. **Unity** — `Runtime/Wasteland.Sim.asmdef` with `noEngineReferences: true`
2. **Headless** — `src/Wasteland.Sim/Wasteland.Sim.csproj` (netstandard2.1) included by `Wasteland.Sim.sln`

There is no `UnityEngine` using-directive anywhere in this package. Keep it that way.

Sim state is integer centimeters and parts-per-thousand. `Fixed64` is internal math only. Move commands target squads.

Pathfinding lives in `Runtime/Movement/`: versioned road-graph and terrain-grid blobs, designer movement tables (integer JSON), and `MovementService` (road A*, grid A*, combined, per-tick request budget). See `docs/movement.md`.
