# CI

## What runs now

`.github/workflows/simulation.yml` restores the headless solution and runs `dotnet test Wasteland.Sim.sln` on every push and pull request. No Unity editor, no license.

This is the gate for the lockstep contract: fixed-point math, command logs, and per-tick hashes.

## Unity builds (later)

Editor, PlayMode, and player builds need [GameCI](https://game.ci/) (`game-ci/unity-builder`, `game-ci/unity-test-runner`) plus a Unity license secret (`UNITY_LICENSE` / floating license). That is intentionally **not** wired up in this skeleton:

- It requires a seat and a stored secret this repo does not have yet.
- First-open ProjectSettings and URP/Entities baking still need a human in the editor before a player build is meaningful.

When we add it, the expected shape is a second workflow (e.g. `.github/workflows/unity.yml`) that:

1. Uses a Unity 6.3 LTS image matching `ProjectSettings/ProjectVersion.txt`
2. Runs EditMode tests under `Assets/_Project/Tests`
3. Optionally builds a Linux dedicated/headless player for smoke
4. Stays separate from `simulation.yml` so math regressions still fail fast without consuming a license minute
