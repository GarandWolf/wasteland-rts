# Map and balance keys

Helpers in `Wasteland.Sim.Movement` so match setup, HPA* cache, and designer tables agree on what changed.

## `terrainPayloadSha256`

SHA-256 of the terrain sections **MVSL | MVLC | MVFD | MVEL** concatenated in that order. It lives in both files' META (pipeline 376 B record, offset 340). It does **not** include `WTRA`, provenance JSON, or other META fields. An art-only WTRA rebuild or a config/attribution-only META rewrite changes the file `contentHash` but not this payload hash.

The sim computes it from the present payload sections (synthetic fixtures may omit MVEL). Pipeline maps carry all four.

## WHPA hierarchy cache

The on-disk HPA* cache (`WHPA`) is keyed by `terrainPayloadSha256`, the movement-table xxHash64, `HierarchyAlgorithmVersion`, cluster size, and grid `W×H`. It is **not** keyed by the terrain file `contentHash`, so a provenance-only or WTRA-only rebuild does not invalidate a warm cache.

## `MapKey.For(map, sections)`

`map` is a parsed `WrmbFile`. `sections` are FourCC tags. The key is xxHash64 of:

1. the 32-byte `terrainPayloadSha256`
2. each requested section, in **ordinal tag order**: tag bytes, element **count**, then the section's raw bytes

Changing a byte in a named section changes the key. A section that was not named does not. Tag and count are mixed in so two sections with identical payloads but different tags or element counts do not collide.

### Per-section hash (validator report and manifest)

`tools/map/wrmb_reader.py` reports, per section of each file, `sectionXxh64`: xxHash64 (seed 0) of the section's 4 ASCII tag bytes, its element count as `uint32` little-endian, then its raw bytes. It is the same per-section contribution `MapKey.For` mixes in, but **without** the leading `terrainPayloadSha256`, so it is a stable per-section value the sim can cross-check (`XxHash64Builder`: `AddBytes(tag)`, `AddUInt(count)`, `AddBytes(bytes)`, `Digest()`). `GoldenMapV1MarshTests` asserts every section of the v1-marsh pair against the Python values. The composite `MapKey` still needs the sim's own `terrainPayloadSha256`.

## `BalanceKey.For(directory)`

xxHash64 of every file under `directory` (recursive). Paths are relative to that directory, UTF-8, forward slashes, sorted ordinal. Each file mixes path length, path bytes, content length, then content. The production tree is `data/balance/`. Any file add/remove/edit changes the key.
