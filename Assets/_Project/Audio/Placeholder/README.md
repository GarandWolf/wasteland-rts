# MVP placeholder audio

These are placeholder sounds for MVP spec §11 (MVP placeholder presentation spec v1.2). All sources are CC0. For sources, licences and edits, see `ATTRIBUTION.md`. The tone is grounded military: a field radio, boots, steel, a snare drum, a funeral bell and a solo bugle-style trumpet. There is no music bed (music is out of MVP, §12).

All files are 48 kHz, 16-bit PCM WAV, with sample peaks at -1 dBFS. Balancing levels in the mixer is left to the game side.

| File | Game event (spec §11) | Length | Ch | Notes |
|---|---|---|---|---|
| `ui_click.wav` | UI click (HUD buttons, e.g. Raise Battalion / Build Depot) | 0.09 s | mono | |
| `order_ack.wav` | Order acknowledged (move/attack command accepted) | 0.29 s | mono | Field-radio key-release squelch |
| `raise_battalion.wav` | Raise battalion (battalion spawned) | 2.50 s | stereo | Squad marching, 4 steps |
| `depot_built.wav` | Depot built (depot active tick, not the site tick) | 1.00 s | stereo | Sledgehammer on a steel stake |
| `combat_loop.wav` | Combat loop: one per contested town on screen, distance-faded | 58.15 s | stereo | **Seamless loop.** Set Loop = on in Unity and use 3D/distance rolloff. Loads as Streaming or Compressed In Memory |
| `town_captured.wav` | Town captured (by the viewer's side) | 2.30 s | stereo | Military snare cadence ending |
| `town_lost.wav` | Town lost (by the viewer's side) | 4.00 s | stereo | Funeral bell |
| `win_stinger.wav` | Win screen | 6.00 s | stereo | Reveille, opening phrase |
| `lose_stinger.wav` | Lose screen | 5.85 s | stereo | Taps, first phrase |
| `draw_stinger.wav` | Draw screen (both HQs fall on the same tick) | 3.10 s | stereo | The lose stinger cut short and faded, per spec. The HUD plays the defeat stinger for a draw. |
| `under_attack.wav` | Human seat under attack | 0.32 s | mono | Kenney `switch2.ogg`. The HUD plays it in 2D at 0.8. |
| `research_done.wav` | Tier research completed (event 6) | 0.36 s | mono | Kenney `switch3.ogg`. 2D. |
| `order_deny.wav` | Rejected human order | 0.05 s | mono | Kenney `click2.ogg`, a different click from `ui_click`. 2D. |

## Folder layout
The sources and the build script live outside `Assets/`, in `art-sources/audio/placeholder/`, so Unity doesn't import them.
- `art-sources/audio/placeholder/_sources/` holds the original downloads, kept as downloaded: Kenney UI Audio zip, Freesound HQ previews, and page snapshots as proof of licence.
- `art-sources/audio/placeholder/_tools/build.py` rebuilds the WAVs from those sources straight into this folder (needs ffmpeg with soxr, Python 3 and numpy). Run it from the repo root with `python3 art-sources/audio/placeholder/_tools/build.py`.
- `art-sources/audio/placeholder/SHA256SUMS` holds checksums of the WAVs in this folder.

Placeholder only. Replace these before release, or at minimum swap the Freesound previews for the lossless originals (see `ATTRIBUTION.md`).
