# Wasteland RTS — Architecture

Working title. PC/Steam first. Single-player vs AI at launch; lockstep multiplayer after launch, without rewriting the simulation.

## Separation of simulation and presentation

| Layer | Assembly | May use | Owns |
| --- | --- | --- | --- |
| Simulation | `Wasteland.Sim` (`noEngineReferences`) | Plain C#, integer/fixed-point only | Authoritative game state, commands, RNG, hashes |
| Presentation | `Wasteland.Presentation` | UnityEngine, Entities.Graphics, UI | Cameras, meshes, interpolation, audio, HUD |
| Bridge (future) | DOTS systems that *read* sim snapshots | Both, but write only commands | Spawning view entities, culling |

Rules:

- The simulation never reads `Time.deltaTime`, `UnityEngine.Random`, `float` physics, or input devices.
- The only way to mutate sim state is to enqueue a `SimCommand` for a specific tick.
- Presentation is allowed to be lossy, threaded, and floating-point. If the renderer hiccups, the sim does not.

`SimFixedUpdateDriver` is the first example of the presentation clock: it accumulates render time and calls `SimWorld.Step()` at a fixed rate.

## Tick rate

**20 Hz simulation** (`SimConfig.TickRateHz`), 50 ms per tick.

Why 20, not 10 or 60:

- RTS lockstep historically lives in the 10–24 Hz band. 20 Hz is enough for movement and combat resolution on a map measured in miles, and cheap enough that tens of thousands of units can step in a budget.
- Render interpolation at 60/120 Hz hides the 50 ms cadence.
- Once lockstep lands, a 50 ms command delay is a natural quantum (two ticks of buffer ≈ 100 ms, typical for LAN/near play). 10 Hz makes units feel sticky; 30+ Hz doubles CPU for little gameplay gain at this scale.

Presentation interpolates between the **previous** and **current** committed sim snapshots using a 0..1 alpha (`elapsed / TickDuration`). Do not lerp inside the sim.

## Input-command model

```
player/AI intent → SimCommand(tick, playerId, unitId, kind, args)
                 → CommandQueue (keyed by tick, canonical sort)
                 → SimWorld.Step() applies that tick's batch, then integrates
```

- Commands are **data**. They are the network payload, the replay log, and the save-game delta.
- A command's `Tick` is the sim tick on which it becomes visible. Submitting a command for a tick that has already executed throws — history is immutable.
- Same-tick commands are ordered by `(PlayerId, UnitId, Kind, args)`, not by enqueue order, so two machines that receive the same set produce the same sequence.
- Single-player can schedule commands for `world.Tick` (immediate) or `world.Tick + 1`. Multiplayer will schedule for `world.Tick + commandDelayTicks` once a lockstep session exists. The world does not know the difference.

AI is just another `PlayerId` that submits commands. It must be deterministic (seeded from `SimRandom` or from a dedicated AI stream) so a replay of the command log is sufficient. If an AI is later allowed to be non-deterministic locally, it still has to emit commands; peers execute those commands, not the AI itself.

## Fixed-point math

All sim quantities that participate in state use `Fix64` (signed **Q31.32** in a `long`):

- Range ≈ ±2.147×10⁹
- Precision 2⁻³² ≈ 2.33×10⁻¹⁰
- World units are **meters**. A 200-mile map is ~3.22×10⁵ m; positions fit with orders of magnitude of headroom.

`Fix64Vec2.Length` does **not** compute `sqrt(x*x + y*y)` in Q31.32 (that overflowed on a 200-mile diagonal). It takes the integer hypot of the raw values via 128-bit intermediates, which is the representation-correct length.

Floating-point is forbidden in sim state and sim operators. `Fix64.ToDouble()` exists for debug drawing and tests only — never write it back.

## Deterministic RNG

`SimRandom` is PCG-XSH-RR (32-bit output, 64-bit state). Same seed + same stream ⇒ same sequence on every runtime we care about. The world hashes RNG state every tick so a silent extra `NextUInt` is a desync, not a mystery.

Do not use `System.Random` or `UnityEngine.Random` in sim code. They are not portable across BCL/Unity versions.

## Deterministic iteration

- Units live in a list ordered by `Id`. Systems iterate `0..n-1`.
- Lookups go through an id→index map; they must not iterate `Dictionary` (hash order is not a sim-facing contract we want to depend on).
- Command batches are sorted before apply (see above).
- No `foreach` over unordered collections in the hot path; when a dictionary must be enumerated for tools, sort the keys first.

When this core moves onto DOTS, the same rule becomes: stable entity order (creation order is not enough — use a sim `UnitId` component and sort, or a dense array indexed by id).

## State hashing

`SimWorld.HashState()` runs FNV-1a 64 over, in order:

1. Tick
2. Seed
3. RNG state + stream
4. Unit count
5. Each unit in id order: id, player, position/target raw bits, speed, `HasTarget`

Bytes are little-endian so hashes match across host endianness.

In single-player this is a regression tripwire. In multiplayer it is the desync detector: after each tick, peers (or a replay checker) compare hashes. On mismatch, dump the command log and the two state snapshots.

## Save, load, and replays

Because the world is a pure function of `(seed, command log)`:

| Feature | Mechanism |
| --- | --- |
| Replay | Store seed + the command log. Re-step. |
| Save (compact) | Seed + log + current tick. Load by replaying. |
| Save (fast) | Serialize the hashed state (units, RNG, tick) **and** keep the log for that match so you can still verify. |
| Spectate / desync dump | Same as replay, plus per-tick hashes as a checksum track. |

Do not serialize presentation state (camera, selection, particles) into a sim save.

## Mapping onto DOTS / ECS

The current `SimWorld` is an intentional **headless prototype**: a list of units and a command queue, fully testable with `dotnet test`. The lockstep contract it defines is what DOTS must preserve.

Planned mapping:

| Today | DOTS later |
| --- | --- |
| `SimUnit` struct | `IComponentData` (`UnitId`, `SimPosition`, `SimTarget`, `SimSpeed`) on sim entities |
| `List<SimUnit>` scan | `ISystem` / `IJobEntity` over a sim-only `World` (or a sim `EntityQuery` with a stable sort by `UnitId`) |
| `CommandQueue` | Singleton `DynamicBuffer<SimCommand>` or a native queue consumed by a command-apply system scheduled first in the sim group |
| `Fix64` / `Fix64Vec2` | Blittable `IComponentData` fields. Burst-compile **only** integer code paths |
| Presentation | A second world, or view entities with `LinkedEntityGroup`, interpolating `SimPosition` from the last two snapshots |

Unity Entities 1.4 is already in `Packages/manifest.json` so baking, subscenes, and jobs can come in without a package shuffle. **Do not** put sim state on presentation entities, and **do not** let a `SystemBase` that reads `SystemAPI.Time.DeltaTime` write sim components.

Netcode for Entities is *not* the default path for this game. Its model is client/server ghost replication, not lockstep. Lockstep will be a thin layer that exchanges `SimCommand` batches and compares hashes. If we ever use `com.unity.netcode`, it will be for lobby/transport, not for sim authority.

## Open risks

### Burst and float determinism

Burst can generate different float results across compiler versions, CPU ISAs, and `FloatMode`. We do not Burst-compile floating-point sim. If a sim job is Burst-compiled later, it must stick to integers/`Fix64` and avoid `float`/`Unity.Mathematics` float types. `FloatMode.Deterministic` is **not** a cross-platform IEEE guarantee.

### Physics

Unity Physics and PhysX are float, SIMD-sensitive, and not lockstep-safe. They are presentation and authoring tools only (placement probes, debris). Sim combat/collision will be discrete (grid, swept AABB, or integer GJK) run at tick rate.

### Pathfinding on huge terrain

A 200×200 mile map at soldier scale cannot be a uniform navmesh in memory. Direction of travel:

- Sim pathfinding on a hierarchical graph (roads / towns / coarse cells) with **integer costs**.
- Local steering on a fine patch around the unit, still fixed-point.
- Unity NavMesh is for editor visualization and maybe presentation crowding — not the authority path.

Elevation from real DEM data should be quantized to a deterministic grid (e.g. centimeters) at import time, not sampled as float at runtime.

### DOTS structural changes

`Entity` ids are not stable across editor domain reloads or different worlds. Sim identity is `UnitId` (and later `PlayerId`), never `Entity`. Command logs refer to those ids.

### Time sources

`Time.unscaledDeltaTime` is fine for the presentation accumulator. Do not use `fixedDeltaTime` as a hidden second sim. There is one sim cadence: `SimConfig.TickRateHz`.

### Save compatibility

The command binary layout is a public format the moment we ship a replay. Version the command envelope early (a `schema` byte on the log header) even while the set of `CommandKind` values is tiny.
