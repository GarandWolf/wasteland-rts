# Presentation audio

Hooks for the ten MVP placeholder clips in `Placeholder/`. Spec: MVP placeholder presentation spec v1.2, §11, plus the rejected-command line in §10.

`Wasteland.Audio` does not reference `Wasteland.Sim` or `Wasteland.Presentation`. The presentation clock in `Wasteland.Presentation` does not reference this assembly yet. When that layer starts calling these hooks, add `"Wasteland.Audio"` to its `references` list. Do not reference `Wasteland.Prototypes`.

World positions are Unity world space, the same space as the `AudioListener`. This assembly does not convert sim centimeters.

## Call

Put one `SoundPlayer` and one `CombatLoopManager` in the scene. Assign `Runtime/PlaceholderSoundBank.asset` to both.

`SoundPlayer` plays one-shots. It ignores `SoundCue.CombatLoop`.

- `Play(SoundCue)` — 2D. HUD clicks and the win, lose, and draw stingers.
- `Play(SoundCue, float volumeScale)` — the same, scaled by `volumeScale`.
- `PlayLow(SoundCue)` — `Play(cue, SoundPlayer.LowVolumeScale)`. The scale is 0.25.
- `Play(SoundCue, Vector3 worldPos)` — 3D at `worldPos` when the bank entry is spatial. A 2D entry ignores the position.
- `Play(SoundCue, Vector3 worldPos, float volumeScale)` — 3D with a scale.

`order_ack` carries a 250 ms cooldown on the bank, so ordering many units at once plays the squelch once. Other cues have no cooldown. The gate uses `Time.unscaledTime`.

`CombatLoopManager.SetContestedTowns` takes the contested towns currently on screen. Each entry is an `int` id and a world position. Call it every frame, or whenever the set or the positions change. Pass null or call `Clear` when none are contested.

The manager runs one source per town, with `AudioSource.loop = true` because the importer leaves Loop off. It keeps at most 3 voices, nearest to the listener first (an optional listener override, otherwise the first `AudioListener`, otherwise this transform). A town that leaves the set fades out before its slot can be taken. Distance fade is logarithmic rolloff on the source (default min 1, max 500). The envelope fade is 0.2 s.

## When the presentation layer plays them

| Cue | Trigger |
|---|---|
| `UiClick` | A HUD control, including Raise Battalion and Build Depot. Full volume. |
| `UiClick` via `PlayLow` | A command the human seat rejected. One line of rejection text is the HUD's job. There is no separate error sound. AI rejections are silent: do not call `Play`. |
| `OrderAck` | The human issues a command. Play at issue time, not when the sim executes it at `issuedTick + commandDelayTicks`. Do not play this for an AI order. |
| `RaiseBattalion` | The battalion spawn event, at the seat HQ. |
| `DepotBuilt` | The depot's active tick, not the site tick. At the depot. |
| `CombatLoop` | One bed per contested town on screen. Feed those towns to `CombatLoopManager`. Do not send this cue to `SoundPlayer`. |
| `TownCaptured` | The viewer's side captures a town. At the town. |
| `TownLost` | The viewer's side loses a town. At the town. |
| `WinStinger` | The win result screen. 2D. |
| `LoseStinger` | The lose result screen. 2D. |
| `DrawStinger` | The draw result screen (both HQs fall on the same tick). 2D. |

Captured and lost are from the viewer's side. The other side's gain is not our capture stinger.

## Tests

These are Unity EditMode tests in `Wasteland.Audio.Tests`. CI runs `dotnet test` on the sim and the golden-map check. It does not launch the editor, so it does not run this assembly.

In Unity 6000.3.24f1: **Window > General > Test Runner > EditMode**, then **Run All** on `Wasteland.Audio.Tests`.

## Play mode

**File > New Scene**. Do not save it. On an empty GameObject, add **Wasteland > Audio > Sound Player** and **Wasteland > Audio > Combat Loop Manager**. Assign `PlaceholderSoundBank` on both. Enter Play mode. The new scene's camera has the listener. Use each component's context menu (`Preview …`, `Preview Ui Click Low`, `Preview One Contested Town Here`, `Clear Contested Towns`). Exit Play mode without saving.
