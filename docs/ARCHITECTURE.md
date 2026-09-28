# Wasteland RTS — Architecture

Working title. PC/Steam first. Single-player vs AI at launch; lockstep multiplayer after launch, without rewriting the simulation.

Locked with the enemy AI design (`docs/ai/enemy-ai-design.md` §4.3): **20 Hz**, shared **`commandDelayTicks`**, integer **centimeters + parts-per-thousand** in sim state, **PCG32** + **xxHash64**, **squads** as first-class Move targets, events in **eventId** order.

## Separation of simulation and presentation

| Layer | Assembly | May use | Owns |
| --- | --- | --- | --- |
| Simulation | `Wasteland.Sim` (`noEngineReferences`) | Plain C#, integers, `Fixed64` for internal math only | Authoritative state, commands, RNG, hashes, events |
| Presentation | `Wasteland.Presentation` | UnityEngine, Entities Graphics, URP, UI | Cameras, meshes, interpolation, audio, HUD |
| Prototypes | `Wasteland.Prototypes` (`autoReferenced: false`) | Same as presentation | Throwaway scenes (5k-unit test). Shipping assemblies must not reference it. |
| Bridge (future) | DOTS systems that *read* sim snapshots | Both, but write only commands | Spawning view entities, culling |

Rules:

- The simulation never reads `Time.deltaTime`, `UnityEngine.Random`, `float` physics, or input devices.
- The only way to mutate sim state is to enqueue a `SimCommand`. Player and AI use the **same** command types.
- Presentation is allowed to be lossy, threaded, and floating-point. If the renderer hiccups, the sim does not.

SimFixedUpdateDriver is the first example of the presentation clock: it accumulates render time (from `GameClock` when present, otherwise unscaled) and calls `SimWorld.Step()` at a fixed rate. Presentation systems read `(tick + alpha) * tickDuration`; the sim never reads a render clock.

## Tick rate

**20 Hz** (`SimConfig.TickRateHz`), 50 ms per tick (`SimConfig.TickDurationMs`). This is locked with the AI design, not a suggestion.

Presentation interpolates between the **previous** and **current** committed sim snapshots using a 0..1 alpha (`elapsed / tickDuration`). Do not lerp inside the sim.

## Input-command model

```
intent (player or AI)
  → SimCommand(issuedTick, issuerId, squadId, kind, cm args)
  → executeTick = issuedTick + commandDelayTicks
  → CommandQueue ordered by (executeTick, issuerId, sequence)
  → SimWorld.Step() validates at execute time, then integrates
```

- **`commandDelayTicks`** is a **match-setup constant** (`MatchConfig`, default **2**). Shared by player and AI. Commands execute at `issuedTick + delay`. Difficulty may add extra delay on the AI *issue* side later; the sim delay itself stays this constant.
- **`issuerId`** is a player or AI faction id (one id space).
- **`sequence`** is stamped on submit, per issuer, so same-tick order is `(executeTick, issuerId, sequence)` regardless of enqueue order across issuers.
- **Validation is authoritative at execute time.** Illegal commands do not throw out of `Step`; they emit `CommandRejected` with a `CommandRejectReason`. The AI must handle reject, not retry in a tight loop.
- Commands whose `executeTick` is already in the past are not queued (`TooLate`).
- Command payloads are **integer centimeters** (and integer counts). No `Fixed64` on the wire.

AI is another `issuerId`. It must be deterministic (PCG32 streams derived with `XxHash64.Mix`) so a replay of the command log is sufficient.

## Sim state: centimeters and ppt

| Quantity | Type | Notes |
| --- | --- | --- |
| Position / target | `int32` cm per axis | 200 miles ≈ 3.22×10⁹ mm still fits in int32 cm |
| Squared distance / path cost | `int64` | `(long)dx * dx + (long)dy * dy` |
| Ratios (strength, scores) | `int32` parts-per-thousand | `SimConfig.PptScale` = 1000 |
| Speed | `int32` cm/s | Stepping uses `Fixed64` internally, writes cm back |

**Never store `Fixed64` in sim state, hashes, or events.** `Fixed64` (Q32.32 in a `long`) exists only for internal math: hypot, step along a heading, future angles. Convert to cm with `ToIntTowardZero()` before writing state.

## Squads

A **squad** is a first-class sim entity with a stable `Id`. Units **belong** to squads (`SimUnit.SquadId`). `Move` targets a squad, not a unit. This skeleton spawns a one-unit squad (unit id = squad id) so the command log stays small; later composition will add more units per squad without changing the Move target.

Iterate squads and units in **id order**. Do not rely on dictionary enumeration.

## Deterministic RNG

`SimRandom` is **PCG32** (XSH-RR), matching `pcg32_srandom_r` in pcg-c-basic. Golden test: seed 42, stream 54 → `0xa15c02b7, 0x7b47f409, …`.

Faction streams (AI design §4.2) will be `XxHash64.Mix(matchSeed, factionId, streamTag)`. Do not use `System.Random` or `UnityEngine.Random` in sim code.

## Hashing

`SimWorld.HashState()` is **xxHash64** (in-repo, golden-tested) over little-endian integers:

1. Tick, seed, `commandDelayTicks`
2. RNG state + stream
3. Each squad in id order: id, owner, position/target **cm**, speed cm/s, strength ppt, `HasTarget`
4. Each unit in id order: id, squadId, owner

No floats, no `Fixed64` raw bits. In single-player this is a regression tripwire. In multiplayer it is the desync detector.

## Events

`SimEvent` values are appended with monotonic `eventId`. Consumers (AI tactical, tests) read them **in eventId order**. Payloads are **ids and ticks only** (plus an integer reason code on reject).

This skeleton emits `SquadSpawned`, `SquadArrived`, and `CommandRejected`. The AI design lists further kinds (`TownOwnerChanged`, `SquadSpotted`, …) to be added with those systems.

## Save, load, and replays

The world is a pure function of `(MatchConfig, command log)`:

| Feature | Mechanism |
| --- | --- |
| Replay | Store config + the command log. Re-step. |
| Save (compact) | Config + log + current tick. Load by replaying. |
| Save (fast) | Serialize hashed integer state **and** keep the log. |
| Spectate / desync dump | Replay plus per-tick xxHash64. |

Do not serialize presentation state into a sim save. Version the command envelope (`schema` byte) before shipping a replay.

## Mapping onto DOTS / ECS

The current `SimWorld` is a headless prototype. The lockstep contract it defines is what DOTS must preserve.

| Today | DOTS later |
| --- | --- |
| `SimSquad` / `SimUnit` | `IComponentData` with integer cm fields and stable ids |
| `List<T>` scan in id order | `ISystem` / `IJobEntity` sorted by sim id, never `Entity` |
| `CommandQueue` | Buffer consumed first in the sim group |
| `Fixed64` | Burst-compile **only** integer/`Fixed64` paths; never persist it |
| Presentation | View entities interpolating the last two **cm** snapshots |

Netcode for Entities is not the lockstep path. Lockstep exchanges `SimCommand` batches and compares hashes. `com.unity.netcode` would be lobby/transport at most.

## Road pathfinding (`PathOnRoads`)

**Sim-owned service, not implemented in this PR.**

AI design §4.3: `PathOnRoads(fromNode, toNode, costWeights)` returns a node list + integer total cost, or unreachable. Both player UI path preview and AI call the same function. Costs are `int64`; threat samples that feed weights are fog-filtered integers. Do **not** put a private float A* inside the AI module.

Until the road graph exists, no caller should invent a parallel pathfinder. Local formation following around a squad stays in the sim (integer/fixed internal math, cm state).

Unity NavMesh is presentation/authoring only.

## Headless AI-vs-AI runner

`dotnet test` on `Wasteland.Sim.sln` is the current headless proof (math goldens + scripted-match hashes).

A dedicated host (`Wasteland.Sim.Headless` or equivalent, AI design §8.1) will load a baked map blob, N AI factions, a seed, and a tick limit, then write per-tick `HashState()` every `HashPeriodTicks`. That runner is **not** in this PR. When it exists, CI will run AI-vs-AI determinism soaks on it (crash / deadlock / hash mismatch fail the build; poor play is a dashboard, not a red X). It stays separate from GameCI so it needs no Unity license.

## Open risks

### Burst and float determinism

Do not Burst-compile floating-point sim. `FloatMode.Deterministic` is not a cross-platform IEEE guarantee. Integer cm + `Fixed64` internals only.

### Physics

Unity Physics / PhysX are presentation. Sim collision will be discrete integer queries.

### Terrain scale

A 200×200 mile map cannot be a uniform navmesh. Hierarchical **integer-cost** road graph + local cm steering. DEM elevation quantized to cm at import.

### Identity

`Entity` ids are not stable. Sim identity is `SquadId` / `UnitId` / `issuerId`. Command logs refer to those.

### Time sources

One cadence: `SimConfig.TickRateHz`. Do not use `fixedDeltaTime` as a second sim.
