# Wasteland.Sim

Embedded UPM package containing the deterministic RTS simulation.

Compiled two ways:

1. **Unity** — `Runtime/Wasteland.Sim.asmdef` with `noEngineReferences: true`
2. **Headless** — `src/Wasteland.Sim/Wasteland.Sim.csproj` (netstandard2.1) included by `Wasteland.Sim.sln`

There is no `UnityEngine` using-directive anywhere in this package. Keep it that way.
