# WRMB map blob (layout v0, DRAFT)

The map pipeline’s container. Sim movement loads **two** WRMB files:

1. **Map / AI file** — road graph from `RNOD` / `REDG` (plus whatever else the AI needs).
2. **Terrain file** — movement layers `MVSL` / `MVLC` / `MVFD` and `META` for cell size.

Same container, same directory rules. `MovementService.Create(mapBlob, terrainBlob, tables)` reads roads from the first and terrain from the second. Cross-file extras are ignored.

Single source of truth for the on-disk layout is `tools/map/wrmb_reader.py`. This document is the sim contract: what we read, how we map enums, and the integer axis. formatVersion 1 record layouts are in [`../map/wrmb-layout-v1.md`](../map/wrmb-layout-v1.md).

**Status:** the sim loader accepts `formatVersion` ∈ {0, 1}. v0 files keep header flag bit0 `DRAFT_LAYOUT`. Section tags and v0 record layouts may still change. Readers **must** find sections through the directory **by tag**, never by fixed offsets. Unknown tags are skipped. A known tag with the wrong `elementSize` is refused, except v0 `META`: synthetic fixtures are 288 B (no `fileKind`) and pipeline maps are 376 B (`fileKind` immediately after `schemaVersion`). Movement accepts both; the fixture writer still emits 288.

**formatVersion 1:** header flag bit2 `V1_FINAL` is required; a v1 file without it is rejected as a stale pre-final layout. v1 terrain files must carry `WTRA` (art-only water marks). The sim checks `elementSize` and skips the section bytes — `WTRA` is not a movement input and is not part of `terrainPayloadSha256`. `CHOK` is 88 B (`defenseGroupId` at offset 7, 255 = none; `bypassWheeledBlocked` at offset 79). `RNOD.territoryId` is the u16 at offset 18. `TERR.stableKey` is a u64 at offset 40 (never parsed as a double). Map identity helpers: [`map-keys.md`](map-keys.md).

WRG1 / WTG1 are gone. Synthetic tests write WRMB fixtures via `RoadGraphBuilder` / `TerrainGridBuilder`.

## Byte-level rules

- Little-endian. No floats. Coordinates are `int32` cm in the map projection.
- Origin is the SW corner; sim **x = east**, **y = north**. Terrain row 0 is south (`index = y * width + x`).
- Sections start on 8-byte boundaries; gaps are zero-filled.
- Per-section zlib CRC-32 of the section bytes. `count × elementSize = byteLength`.
- `contentHash` is SHA-256 of the whole file with those 32 bytes zeroed.

## File header (64 B, offset 0)

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `magic` | char[4] | `"WRMB"` |
| 4 | `formatVersion` | u16 | 0 = draft; 1 = final v1 (`V1_FINAL` required) |
| 6 | `schemaVersion` | u16 | 1 |
| 8 | `headerSize` | u32 | 64 |
| 12 | `flags` | u32 | bit0 `DRAFT_LAYOUT`, bit1 `HAS_DEBUG_SECTIONS`, bit2 `V1_FINAL` (required when `formatVersion` = 1) |
| 16 | `sectionCount` | u32 | |
| 20 | `directoryOffset` | u32 | 64 |
| 24 | `fileSize` | u64 | total bytes |
| 32 | `contentHash` | u8[32] | SHA-256 with these 32 bytes zeroed |

## Section directory (40 B per entry)

| Offset | Field | Type |
|---:|---|---|
| 0 | `tag` | char[4] |
| 4 | `reserved0` | u32 (0) |
| 8 | `offset` | u64 |
| 16 | `byteLength` | u64 |
| 24 | `count` | u32 |
| 28 | `elementSize` | u32 |
| 32 | `crc32` | u32 |
| 36 | `reserved1` | u32 (0) |

Pools `I32P` / `PTSP` / `STRP` are always present (may be empty). Variable-length strings and polylines are `{u32 offset, u32 count}` into those pools.

## Roads (`RNOD` 24 B, `REDG` 64 B)

Nodes and edges are sorted by unique `id`. The graph is undirected at runtime. Bridges are **two `BridgeHead` nodes + one `bridgeSpan` edge**. `RoadGraph.CutSpan` marks a span cut without rewriting the blob.

WRMB node flag `BridgeHead = 2` and edge flag `bridgeSpan = 2` map to the sim’s bit0 flags at the WRMB boundary. Other WRMB flags are ignored by movement.

`REDG.highwayClass` (0 motorway … 7 track) maps to sim `RoadTier`: 0–1 Highway, 2–3 Paved, 4–6 Dirt, 7 Track. `surface` 0 dirt / 1 gravel / 2 paved; a span becomes `RoadSurface.BridgeDeck`.

`travelCost` and geometry polylines are pipeline fields. The sim costs an edge as `ceil(lengthCm / classSpeed)` from designer tables at match load.

## Terrain (`META` + `MVSL` / `MVLC` / `MVFD`)

`META.cellSizeCm` is the **mile-grid** cell when `moveCellsPerCell > 1` and `moveWidth == gridWidth * moveCellsPerCell` (same for height). The movement cell the sim uses is then `cellSizeCm / moveCellsPerCell` (integer). The Columbia Gorge golden uses `META.cellSizeCm` = 160934 (1 mile) so the movement cell is `160934 / 18 = 8940` cm. Synthetic fixtures that author a 160938 cm mile still get 8941. If `moveCellsPerCell == 1`, the movement cell **is** `cellSizeCm`. Nothing in the sim assumes 100 m.

| Tag | Layout |
|---|---|
| `MVSL` | `uint16` per-mille slope, `moveWidth × moveHeight`, row 0 = south |
| `MVLC` | `uint8` land-cover codes, same size |
| `MVFD` | Ford bitset, bit `i` = byte `i>>3` bit `i&7` |

WRMB land cover → sim `LandCover`: 0 nodata → Water, 1 field, 2 scrub_rock → Scrub, 3 urban, 4 forest, 5 marsh, 6 water, 7 snow_ice → Scrub. In formatVersion 1, 6 is blocking water only (impassable to foot and wheeled); 5 is marsh / fordable water (foot and tracked can cross, wheeled cannot). A set ford bit **overrides** the cover to `Ford`. `Wrmb.WriteTerrain` writes fords as movement value 5 plus the ford bit. Unknown MVLC codes are refused.

Slope is magnitude only. Per-class cost grids are computed at match load from `movement-tables-v1.json`, not baked into the blob.

World mapping (integer floor division, including negatives):

```
cellX = FloorDiv(xCm - OriginXCm, CellSizeCm)
cellY = FloorDiv(yCm - OriginYCm, CellSizeCm)
```

WRMB origin is the SW corner, so loaded grids have `OriginXCm = OriginYCm = 0`. Cell center: `Origin + cell * CellSizeCm + CellSizeCm / 2`.

## Fixture writer

`RoadGraphBuilder.ToBlob()` / `TerrainGridBuilder.ToBlob()` emit WRMB (draft flag, CRC, content hash, 8-alignment, empty pools). They exist so goldens run in CI without the map pipeline. They are not a substitute for `tools/map/wrmb_reader.py` round-trips on real maps.
