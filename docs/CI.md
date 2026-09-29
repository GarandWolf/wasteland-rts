# CI

## What runs now

`.github/workflows/simulation.yml` restores the headless solution, pulls `data/golden/*.wrmb` through Git LFS, and runs `dotnet test Wasteland.Sim.sln` on every push and pull request. No Unity editor, no license.

This is the gate for the lockstep contract: `Fixed64` internals, integer-cm state, PCG32 / xxHash64 goldens, delayed command logs, per-tick hashes, and the headless movement/pathfinding service (`dotnet test` does **not** run `src/Wasteland.Sim.Benchmarks`). The Columbia Gorge WRMB pair is loaded by a pathfinding golden, so that job must materialize Git LFS.

## Golden map (WRMB)

`.github/workflows/golden-map.yml` is a separate job. It does **not** run `dotnet test`. After checkout it pulls only `data/golden/*.wrmb` through Git LFS, checks the sidecar SHA-256 files for the v0 and v1 pairs, runs `tools/map/wrmb_reader.py` on each pair (the validator fails the build on any error), runs `tools/map/stable_ids.py` so the v1 blob's territory, bridge and chokepoint ids and `stableKey`s match `data/golden/stable-ids.json` (including `defenseGroups`) and the v0 ids equal the v1 ids, and runs `tools/map/tests/test_reader_final_v1.py` (stale v1 without `V1_FINAL`, missing `WTRA`, raw `elementSize`, 254-group overflow, `territoryId` 0, cliff-within-1 km rule).

The four `.wrmb` files are ~37 MB together. Both workflows download them from Git LFS (GitHub LFS bandwidth quota). Keep the Python validator off `simulation.yml` so a layout-only change still fails in a cheap job. The sim loads the v0 pair in `GoldenMapTests` (unchanged pins) and the v1 pair in `GoldenMapV1Tests`.

## Headless AI-vs-AI (later)

A `Wasteland.Sim.Headless` (or equivalent) host will run playerless matches for the soak described in the AI design: seed × difficulty × personality, `HashState()` every N ticks. Wire that into this workflow (or a sibling `simulation-soak.yml`) once the AI exists; the map blobs are already in `data/golden/`. It still needs **no Unity license**. Fail the job on crash, deadlock, or hash mismatch — not on poor play.

## Unity builds (later)

Editor, PlayMode, and player builds need [GameCI](https://game.ci/) (`game-ci/unity-builder`, `game-ci/unity-test-runner`) plus a Unity license secret (`UNITY_LICENSE` / floating license). That is intentionally **not** wired up in this skeleton:

- It requires a seat and a stored secret this repo does not have yet.
- First-open still needs a human in the editor: remaining ProjectSettings YAML, Bootstrap scene, and creating/assigning the Forward+ URP assets in `Assets/_Project/Settings/` (URP 17.3.0 and Entities Graphics 1.4.21 are already in the manifest).

When we add it, the expected shape is a second workflow (e.g. `.github/workflows/unity.yml`) that:

1. Uses a Unity 6.3 LTS image matching `ProjectSettings/ProjectVersion.txt`
2. Runs EditMode tests under `Assets/_Project/Tests`
3. Optionally builds a Linux dedicated/headless player for smoke
4. Stays separate from `simulation.yml` so math / AI-vs-AI regressions still fail fast without consuming a license minute
