# CI

## What runs now

`.github/workflows/simulation.yml` restores the headless solution and runs `dotnet test Wasteland.Sim.sln` on every push and pull request. No Unity editor, no license.

This is the gate for the lockstep contract: `Fixed64` internals, integer-cm state, PCG32 / xxHash64 goldens, delayed command logs, per-tick hashes, and the headless movement/pathfinding service (`dotnet test` does **not** run `src/Wasteland.Sim.Benchmarks`).

## Headless AI-vs-AI (later)

A `Wasteland.Sim.Headless` (or equivalent) host will run playerless matches for the soak described in the AI design: seed × difficulty × personality, `HashState()` every N ticks. Wire that into this workflow (or a sibling `simulation-soak.yml`) once the AI and map blob exist. It still needs **no Unity license**. Fail the job on crash, deadlock, or hash mismatch — not on poor play.

## Unity builds (later)

Editor, PlayMode, and player builds need [GameCI](https://game.ci/) (`game-ci/unity-builder`, `game-ci/unity-test-runner`) plus a Unity license secret (`UNITY_LICENSE` / floating license). That is intentionally **not** wired up in this skeleton:

- It requires a seat and a stored secret this repo does not have yet.
- First-open still needs a human in the editor: remaining ProjectSettings YAML, Bootstrap scene, and creating/assigning the Forward+ URP assets in `Assets/_Project/Settings/` (URP 17.3.0 and Entities Graphics 1.4.21 are already in the manifest).

When we add it, the expected shape is a second workflow (e.g. `.github/workflows/unity.yml`) that:

1. Uses a Unity 6.3 LTS image matching `ProjectSettings/ProjectVersion.txt`
2. Runs EditMode tests under `Assets/_Project/Tests`
3. Optionally builds a Linux dedicated/headless player for smoke
4. Stays separate from `simulation.yml` so math / AI-vs-AI regressions still fail fast without consuming a license minute
