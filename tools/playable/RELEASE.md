# Playable release and the Gorge OSM clip

The zip credits name `gorge-clip.osm.pbf` as the ODbL source offer. That file is not in this repository. It is attached to the GitHub release whose tag matches `playable-*`.

`ATTRIBUTION.md` currently uses the placeholder tag `playable-2026-10`:

https://github.com/GarandWolf/wasteland-rts/releases/download/playable-2026-10/gorge-clip.osm.pbf

17,153,637 bytes. SHA-256 `3eebb2957d33270446cbb2f46ea4a87631086da7c6418f7700693b7dc8b87176`.

Replace `playable-2026-10` in `ATTRIBUTION.md` and in `tools/playable/verify_playable_zip.py` with the real tag before publishing. The placeholder and the release tag have to be the same string, or the link in the zip will not download the asset.

## Produce the clip

The clip is the pipeline product described in `data/golden/OSM-EXTRACT.txt`.

1. Start from the pinned Geofabrik extracts (replication timestamp 2026-09-27T20:23:36Z, sequence 4924):
   - https://download.geofabrik.de/north-america/us/oregon-260927.osm.pbf
   - https://download.geofabrik.de/north-america/us/washington-260927.osm.pbf
2. On the map-pipeline machine, rebuild from the pinned inputs: `make -C pipeline build` (`pipeline/s1_osm.py` clips the bounding polygon and filters tags; `raw/inputs.lock.json` pins the inputs).
3. Take `gorge/archive/osm/gorge-clip.osm.pbf`.
4. Check the download before attaching it:

```
test "$(wc -c < gorge-clip.osm.pbf)" -eq 17153637
sha256sum gorge-clip.osm.pbf
```

The SHA-256 must be `3eebb2957d33270446cbb2f46ea4a87631086da7c6418f7700693b7dc8b87176`. That is `META.osmClipSha256` in the v0 and v1 `.wrmb` pairs.

## Tag and attach

Do this only when a playable release is intended. These commands create a tag and a GitHub release.

```
git tag playable-2026-10
git push origin playable-2026-10
gh release create playable-2026-10 \
  --title "Wasteland RTS playable 2026-10" \
  gorge-clip.osm.pbf
```

Use the real tag in place of `playable-2026-10`, and use that same tag in the attribution link. The asset file name on the release must stay `gorge-clip.osm.pbf`. The Windows player zip is a separate Actions artifact (`wasteland-rts-playable-win64.zip`); this clip is the ODbL source offer, not the game.
