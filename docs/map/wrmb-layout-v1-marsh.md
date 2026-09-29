# WRMB map files, layout v1-marsh (formatVersion 1, FINAL layout; pipeline 0.7.0 marsh follow-up)

> **v1-marsh (pipeline `0.7.0-golden-v1.4-marsh`, 2026-09-29) is a data follow-up of the merged final v1 (main 95c1cf9).** `formatVersion` stays 1 and **no record layout changes**. The merged files `golden-ci-columbia-gorge-100.v1.wrmb` / `.terrain.v1.wrmb` and `docs/map/wrmb-layout-v1.md` are untouched; this pair is `golden-ci-columbia-gorge-100.v1-marsh.wrmb` / `.terrain.v1-marsh.wrmb`. What changed (§4.6a, §4.7, §5):
> - Every fordable OSM river is a continuous, 8-way watertight `MVLC` 5 (marsh) line (+11 841 code-5 cells, 1 416 → 13 257), with gaps kept open at road bridges (392 cells) and fords (19 cells on the lines).
> - `WTRA` version 2: new value **4 = wetland**; version 1 (values 0..3) still decodes.
> - New validator match check between `WTRA` and `MVLC`, and per-section xxHash64 values in the report and manifest.
> - `CHOK.bypassWheeledBlocked` is 1 for 29 of 49 chokepoints (was 26): chokepoints 5, 13 and 21 are added. Nothing else in the main file changes; territories, ids, stable keys and `territory-roads` are identical.

> **Status: `formatVersion` 1 is the FINAL v1 layout** (Lead Engineer review of PR #6, 2026-09-28). The container and section directory were approved earlier the same day. v1 was never merged or consumed from the repo, so everything below *is* formatVersion 1; there are no v1 sub-revisions for readers.
> - **Final v1 means:**
>   - `MVLC` has the blocking-water semantics of §4.6: 6 = impassable blocking water, 5 = marsh / fordable water.
>   - `WTRA` is **required** in every v1 terrain file (§4.7).
>   - `CHOK.defenseGroupId` sits at offset 7 (§4.8) and `CHOK.bypassWheeledBlocked` at offset 79 (§4.9). The record size stays 88 B.
> - **Record layouts are frozen:** header flag bit0 `DRAFT_LAYOUT` is **cleared**. Any record or field change needs `formatVersion` 2.
> - **Stale-file marker:** every final v1 file sets header flag **bit2 `V1_FINAL`**. The reader **rejects** a formatVersion 1 file without it: files built before the final definition lack the bit and would otherwise be misread (for example, pad bytes read as group ids). The validator also rejects a v1 terrain file without `WTRA`. The header has no reserved slot, so a flag bit is the marker.
> - The reader checks `elementSize` on every section: record schemas, pools (4 / 8 / 1) and raw sections (MVSL 2, MVLC 1, MVFD 1, MVEL 2, WTRA 1).
> - Readers must find sections through the directory by tag, never by fixed offsets, and must pick the record schema by the header `formatVersion`.
> The files are the golden v1 for `golden-ci-columbia-gorge-100` (they supersede, but do not overwrite, the v0 pair). They are **not frozen** as the CI golden yet.
>
> **Changes from v0 (summary, details in §4.1-§4.8):**
> - `TERR` +`stableKey` u64 (40 → 48 B), `BRDG` +`stableKey` u64 (32 → 40 B), `CHOK`: the 2 padding bytes after `bypassMaxGradePpt` become `bypassSurfaceClass` u8 + 1 pad byte, then +`stableKey` u64 (80 → 88 B), `RNOD`: the 2 padding bytes after `kind` become `territoryId` u16 (size unchanged, 24 B). All other records are byte-for-byte the v0 layout.
> - Ids of territories, bridges and chokepoints are pinned by the committed manifest `pipeline/stable-ids.json` (seeded from v0, so every v1 id equals its v0 id).
> - Territory partition follows the terrain movement layer (v1.2, §4.4): a border can only be a movement cell that is impassable to both foot and wheeled units (`MVLC` code 6). This changes territory, side and gate fields, not the layout.
> - **v1.2 terrain movement data (§4.6):** blocking rivers and lakes, chosen by a measurable rule with no river names, are baked into `MVLC` as code 6 and made watertight against 8-way movement. Fordable water is baked as 5 (marsh). Bridges and fords are the only crossings. `CELX.edgeBlockMask` and `CHOK.bypass*` are derived from this baked data (§4.1).
> - **v1.2 new terrain section `WTRA`** (§4.7): art-only water marks (land / blocking water / fordable water / ford) and a ford list. It is not a sim input, is not part of `terrainPayloadSha256`, and has no GRID descriptor. v0 files do not have it.
> - `Town.sourceFlags` bit4: population from the US Census 2020 (P.L. 94-171) fallback; bit5: population is a manual **design override** (not sourced data, §4.5).
> - `RNOD.territoryId` (u16, §4.4): the node's territory; the value **equals `TERR.id`** (ids pinned by the manifest, i.e. keyed to `TERR.stableKey`). Never 0 in a final v1 file.
> - **`CHOK.defenseGroupId`** (u8 at offset 7, the v0 padding byte after `widthClass`; §4.8): border chokepoints that guard the same approach share a group. The AI garrisons by group, and the tactical map draws one bracket per group. 255 = no group (internal chokepoint).
> - **`CHOK.bypassWheeledBlocked`** (u8 at offset 79, the last padding byte; §4.9): 1 if no bypass is passable for wheeled units. The `CHOK` record is 88 B with no padding left.
> - `tools/map/wrmb_reader.py` reads and validates both formatVersion 0 and 1 files.

- **Two files per map** (each a complete WRMB container with its own header, META, directory, CRCs and content hash):
  - `<map>.v1-marsh.wrmb`: **main/AI file**, `META.fileKind` = 0. Sections: META, SRCS, GRID, TOWN, TDBG, TERR, RNOD, REDG, CHOK, RIVR, BRDG, WATR, WRNG, MSET, RSRC, CELL, CELX, pools.
  - `<map>.terrain.v1-marsh.wrmb`: **terrain file**, `META.fileKind` = 1. Sections: META, SRCS, GRID, MVSL, MVLC, MVFD, MVEL, WTRA (v1.2, art only, §4.7), pools (strings for META/SRCS).
  - **Pair binding:** both METAs carry `terrainPayloadSha256` = SHA-256 of the terrain sections' bytes concatenated in the order MVSL, MVLC, MVFD, MVEL (**not** WTRA). The header `contentHash` of each file covers the whole file, WTRA included, so an art-only WTRA change alters the terrain file's `contentHash` but not `terrainPayloadSha256`. The terrain META carries `pairedContentSha256` = the main file's header `contentHash`. The main META leaves `pairedContentSha256` zero, because the terrain file points at the main file and not the other way round. Each META names the other file in `pairedFileName`. Build order: terrain payload → main file → terrain file.
  - Both METAs share one schema. In the terrain file the AI counts are 0. Map extent, grids, projection, seeds, versions, attribution, SRCS and hashes are identical in both files, and the validator checks this.
- **Not in v1 (as in v0):** the full-resolution 5400 × 5400 render heightmap (30 m). It will be a later, separate asset. The terrain file's MVEL (1800 × 1800, decimetres) is meant for tactical-map hillshade and AI use.
- **Schema:** Enemy AI design doc §3.1 schema v1 (`schemaVersion` = 1) plus the accepted contract changes in `ai-contract-review.md` §12: the bridge-head model, `areaCells`/`areaHa`, `seatTownId`, `exitEdgeIds`, `condition`, `climbCmAB/BA` + `elevDeltaCm`, travelCost in metre-equivalents, the `ChokepointRecord` layout from §2.2 (+ `bypassMaxGradePpt`, v1: + `bypassSurfaceClass`, `stableKey`), `CellExt`, `WaterBody`, pools and the provenance/attribution header.
- **Single source of truth:** `pipeline/wrmb_reader.py` (`SCHEMAS[formatVersion]`, `encode`, `decode`, `validate`, `validate_pair`). The writer (`pipeline/s12_blob.py`) imports it, and the tables below are generated from it by `pipeline/gen_layout_md.py`.
- **Reader/validator (standalone, Python stdlib only):**
  `python3 tools/map/wrmb_reader.py golden-ci-columbia-gorge-100.v1-marsh.wrmb --terrain golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb` (the merged v1 pair validates the same way) (the same reader still validates the v0 pair)
  (pipeline form also passes `--debug-json`, `--terrain-debug-json` and `--movement-dir build/runA/movement`).

## 1. Byte-level rules

- Little-endian. No floats anywhere. Records are fixed size with explicit zero padding. `bool` is stored as a flag bit or a u8 0/1, and enums are int8.
- Every id-bearing array is dense (ids 1..n) and stored sorted by id. A reference value of 0 means "none" wherever the notes allow it.
- Variable-length arrays are `{u32 offset, u32 count}` into a shared pool of the same file, with the offset counted in *elements*. The pools are:
  - `I32P`: int32 ids
  - `PTSP`: int32 x,y pairs
  - `STRP`: UTF-8 bytes, with `count` = byte length
- The encoder fills pools in section order, then record id order, then field order, so offsets increase monotonically and identical inputs give byte-identical files. The CI round trip is binary → model/JSON → binary, and it must be byte-identical (the validator checks this).
- Sections start on 8-byte boundaries, and gaps are zero-filled.
- Unknown section tags must be skipped by readers (forward compatibility). A reader must check `elementSize` against its own record size and refuse a mismatch unless it knows the newer layout.

### 1.1 Coordinates and axes (Unity mapping)

| Sim | Unity | Notes |
|---|---|---|
| x (east) | +X | absolute int32 cm from the map's SW corner |
| y (north) | +Z | absolute int32 cm from the map's SW corner |
| elevation | +Y | int32 cm, EGM2008 (`META.verticalDatum` = 1) |

- The origin is the **SW corner** (0, 0). The map covers `[0, mapWidthCm] × [0, mapHeightCm]`. All positions are absolute integer cm, not cell-relative.
- Grids are row-major `index = row * width + col`, and row 0 is the **south** row (`GRID.rowOrder` = 0).
- Unity conversion: `unity = (x / 100, elevationCm / 100, y / 100)` metres (the engine may rescale). Nothing is mirrored: east stays +X and north stays +Z.

### 1.2 Cell size rule

`cellSizeCm` is **not** assumed to be a round number. Each gridded section has a `GRID` descriptor giving its exact cell size as a reduced rational `cellSizeNumCm / cellSizeDen` = `extentCm / width`:

- 1-mile grid (CELL, CELX): 16093400 / 100 = **160934 / 1** cm (`META.cellSizeCm` = 160934).
- Terrain grid (MVSL, MVLC, MVFD, MVEL): 16093400 / 1800 = **80467 / 9** cm (≈ 8940.78 cm; 18 cells per mile = 3 × 3 heightmap px).

Cell lookup **must** use `col = floor((xCm - originXCm) * width / extentXCm)` and `row = floor((yCm - originYCm) * height / extentYCm)` in 64-bit integers, clamped to `[0, width-1]` / `[0, height-1]` (`wrmb_reader.grid_index`). Never divide by a rounded cell size. The cell centre is `((2*col + 1) * extentXCm) / (2 * width)`.

## 2. File header (64 B, offset 0)

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `magic` | char[4] | `"WRMB"` |
| 4 | `formatVersion` | u16 | 0 = v0; **1 = v1** (adds stable keys + `bypassSurfaceClass`). The record schema is chosen by this value; readers reject unknown versions |
| 6 | `schemaVersion` | u16 | 1 (AI design doc §3.1 + contract changes) |
| 8 | `headerSize` | u32 | 64 |
| 12 | `flags` | u32 | bit0 `DRAFT_LAYOUT` (set in v0; **cleared** in final v1), bit1 `HAS_DEBUG_SECTIONS` (TDBG, main file only), bit2 `V1_FINAL` (**required** when `formatVersion` = 1; readers reject a v1 file without it). Other bits 0 |
| 16 | `sectionCount` | u32 | number of directory entries |
| 20 | `directoryOffset` | u32 | 64 |
| 24 | `fileSize` | u64 | total bytes |
| 32 | `contentHash` | u8[32] | SHA-256 of the **whole file** with these 32 bytes zeroed: every section, including `WTRA` in the terrain file. (Only `terrainPayloadSha256` leaves WTRA out.) |

## 3. Section directory (40 B per entry, at `directoryOffset`)

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `tag` | char[4] | FourCC |
| 4 | `reserved0` | u32 | 0 |
| 8 | `offset` | u64 | byte offset of the section (8-aligned) |
| 16 | `byteLength` | u64 | = count × elementSize |
| 24 | `count` | u32 | records or elements |
| 28 | `elementSize` | u32 | record size (pools: 4 / 8 / 1; raw arrays: 2 / 1 / 1 / 2) |
| 32 | `crc32` | u32 | zlib CRC-32 of the section bytes |
| 36 | `reserved1` | u32 | 0 |

## 4. Sections (order unchanged from v0; v1.2 appends WTRA after MVEL)

| Tag | File | Contents | Source layer |
|---|---|---|---|
| `META` | both | MapAiHeader (§3.1) + `fileKind`, counts, projection, vertical datum, LOD ladder level, transform seed/version, map id, pipeline version, attribution block, provenance JSON (inputs, SHA-256s, enum tables, axis/cell rules, bypass parameters), hashes of the attribution text, weights table, config and OSM clip, elevation encoding (`elevOffsetCm`, `elevUnitCm`), pair binding (`pairedFileName`, `pairedContentSha256`, `terrainPayloadSha256`) | mixed |
| `SRCS` | both | One record per pinned input (key, version/Last-Modified, licence, SHA-256) | - |
| `GRID` | both | One descriptor per gridded section in the file: tag, element type (0 struct record, 1 u8, 2 u16 LE, 3 bitset LSB-first), row order (0 = south first), width, height, origin, extent, exact cell size `cellSizeNumCm/cellSizeDen`, element bytes, value offset/scale (MVEL: `elevationCm = valueOffset + value * valueScale`) | - |
| `TOWN` | main | TownRecord (population fallback: US Census 2020, §4.5). The gameplay fields (`supplyCapacity`, `fortificationSlots`, `defaultFortification`) are resolved at load from the designer table (contract change 4) and are not in the file | OSM place nodes + GeoNames population |
| `TDBG` | main | Per-town strategicValue components P/R/C/H/X/base/abs (optional debug) | derived |
| `TERR` | main | TerritoryRecord with `seatTownId`, `areaCells`, `areaHa`, v1 `stableKey`; `resourceYield` resolved at load | derived |
| `RNOD` | main | RoadNode with the `flags` bitmask (contract change 9), a legacy `kind` enum kept for §3.1 readers and v1 `territoryId` | OSM-derived graph (+ DEM elevation, Pass nodes) |
| `REDG` | main | RoadEdge with `surface` (0 dirt / 1 gravel / 2 paved) and `condition` split, climb A→B / B→A, signed `elevDeltaCm`, travelCost in metre-equivalents, geometry, flags, `territoryId`, `controlTownId` | OSM-derived graph (+ DEM, transform) |
| `CHOK` | main | ChokepointRecord (contract review §2.2 layout) + `bypassMaxGradePpt` (§4.1) + v1 `bypassSurfaceClass` (§4.3) + `stableKey` (§4.2) + final-v1 `defenseGroupId` @7 (§4.8) and `bypassWheeledBlocked` @79 (§4.9) | derived |
| `RIVR` | main | RiverSegment (OSM waterway=river/canal; streams are not exported), simplified polylines | OSM |
| `BRDG` | main | BridgeRecord, one per bridge **span edge**. Its two ends are BridgeHead nodes (bridges over blocking rivers/lakes only; other OSM bridges are ordinary edges with flag bit0 `bridge`); v1 `stableKey` | OSM-derived |
| `WATR` / `WRNG` | main | WaterBody (OSM water ≥ 25 ha) plus its rings (first ring outer, then holes) | OSM |
| `MSET` | main | Minor settlements (OSM `place=hamlet`), not towns | OSM |
| `RSRC` | main | Resources: fuel_station / fuel_storage / refinery / industrial_area, each with its nearest road node and town | OSM |
| `CELL` | main | ElevationCell (1-mile grid 100 × 100, row 0 = south), padded to 8 B | DEM |
| `CELX` | main | CellExt: `territoryId`, steep/water/cover ppt, `landCover` (game enum below), `edgeBlockMask` (bit0 N, bit1 E, bit2 S, bit3 W; v1.2: set if the centre-to-centre segment crosses a baked blocking river or counted lake with no bridge over it touching either cell, or a cliff) | mixed |
| `MVSL` | terrain | Movement slope, uint16 per-mille grade, 1800 × 1800 | DEM (Copernicus GLO-30) |
| `MVLC` | terrain | Movement land cover, uint8 codes, 1800 × 1800. v1.2: **6 = water impassable to foot and wheeled units** (blocking rivers/lakes, watertight); fordable water = 5; all other codes passable (§4.6) | ESA WorldCover v200 + OSM-derived blocking water (the OSM/ODbL cells are exactly those that differ from the WorldCover-only array, §4.6) |
| `MVFD` | terrain | Ford flag bitset, 1800 × 1800 bits (bit i = byte i>>3, bit i&7) | OSM (kept separate from WorldCover for ODbL layer separation) |
| `MVEL` | terrain | Elevation, uint16, 1800 × 1800: `elevationCm = META.elevOffsetCm + value × META.elevUnitCm` (v0/v1: offset 0, unit 10 = **decimetres**) | DEM (Copernicus GLO-30) |
| `WTRA` | terrain | v1.2, art only (**not a sim or path input**): water-type marks, u8 per movement cell (0 land, 1 blocking water, 2 fordable water, 3 ford), plus a ford list (cell index, OSM id), §4.7 | OSM-derived (ODbL) + WorldCover water extent |
| `I32P` / `PTSP` / `STRP` | both | Shared pools (per file) | - |

### 4.1 `CHOK.bypassMaxGradePpt` (u16)

For each chokepoint this is the best cross-country (off-road) way around it, measured as the lowest possible **maximum slope** (per-mille) along a route that avoids the chokepoint. `65535` means no bypass exists in the search region: for example, only the bridge crosses, or cliffs or water close every route.

- **Grid:** the terrain file's MVSL (1800 × 1800), with 4-connected moves between cell centres. A route's grade is the maximum MVSL value over the cells it enters, including both end cells.
- **Endpoints:** the cells of the chokepoint's two approach road nodes (A and B). These are the first nodes about 1 mile of road away on each side, walking roads of at least the chokepoint's width class. They are also the detour-ratio endpoints.
- **Search region:** cells whose centre is within `R = max(8 km, 1.5 × the larger straight-line distance to an approach node)` of the chokepoint position, and **outside** the exclusion disc `X = min(1 km, 0.5 × the smaller approach-node distance)`. The exclusion forces the route around the defended spot instead of along the road through it. The endpoint cells are always allowed.
- **Closed cells and edges:**
  - Water cells (MVLC code 6) are closed. This is the only closure: since v1.2 the search reads **only** the baked terrain movement data. Blocking rivers and lakes are code-6 cells, fords are carved passable, and bridges are not cells. The v1.1 1-mile cell-edge mask is no longer used.
  - As a result, a bridge over blocking water with no other bridge, ford or river end inside the search region reports no bypass (65535 / 255).
  - **CI (build fails, S10 `ciBridgeBypass`):** every chokepoint whose own or removed edges include a bridge over a blocking river, with no other bridge over blocking water and no carved ford cell within radius R, must report 65535 / 255.
- **Algorithm:** a deterministic minimax-bottleneck Dijkstra with key (maxGrade, cellIndex), in integers only. Parameters are in the pipeline config `chokepoints.bypass` and are copied into `META.provenanceJson`.
- **4-connected search vs 8-way sim:** the search moves 4-connected (N/E/S/W), while the sim moves 8-way. A route that the sim would take diagonally past a steep cell corner is not found. `bypassMaxGradePpt` can therefore come out **higher** (more pessimistic) than the best route an 8-way unit could actually use, and never lower. Treat it as an upper bound.
- **Grade threshold:** `bypassMaxGradePpt` stores no threshold; the AI compares it with the designer's movement table at load. The separate flag `bypassWheeledBlocked` (§4.9) does bake one: the wheeled slope limit of 299‰, plus marsh closure.

### 4.2 Stable keys (v1): `TERR.stableKey`, `BRDG.stableKey`, `CHOK.stableKey` (u64)

A permanent 64-bit key derived from OpenStreetMap ids, so saves, scripts and designer tables can refer to a territory, bridge or chokepoint across rebuilds even if the record id changes.

    key = (osmType << 60) | (disc << 52) | osmId        osmType: 1 node, 2 way, 3 relation;  disc: u8;  osmId < 2^52

| Section | osmType / osmId | disc |
|---|---|---|
| `TERR` | node: OSM node id of the seat town (`place=*` node) | 0 |
| `BRDG` | way: the OSM `bridge=*` way on the span edge that crosses the blocking water (longest such way; tie → lowest way id) | 0 |
| `CHOK` Bridge | way: same bridge way as the `BRDG` record | `kind + 1` = 1 |
| `CHOK` Pass | node: the OSM node at the saddle (`mountain_pass` / DEM pass node carrying an OSM node id); else the OSM way at the pass position | 2 |
| `CHOK` Causeway / Dam / Gap / Tunnel / Junction | way: the OSM way of the edge at the chokepoint position (nearest raw way piece; tie → lowest way id) | `kind + 1` (3..7) |

The discriminator keeps a bridge's `BRDG` key and its Bridge chokepoint key distinct although they share the OSM way. Keys are unique per section and never 0. Example (golden map): The Dalles Bridge = OSM way 138322451 → `BRDG` key 2305843009352016403 (id 55), `CHOK` key 2310346608979386899 (id 7).

**Id rule and manifest (`pipeline/stable-ids.json`, committed):** for each section the manifest maps key → id (plus OSM type/id, disc and a label). At build time a key found in the manifest keeps its manifest id; keys not in the manifest get the next free ids in the section's deterministic default order. The build **fails** if (a) the ids would not be dense 1..n (a manifest key vanished, which would force renumbering), (b) two records share a key, or (c) the post-build check `stable_ids.py --check <main.wrmb>` finds a manifest key whose id changed or that is missing (report: `blob/stable-ids-check.json`). Changing the manifest is a deliberate, reviewed step (`stable_ids.py --update <main.wrmb>`). The v1 manifest was seeded from the v0 golden file, so all 13 territory, 123 bridge and 49 chokepoint ids equal their v0 ids. Territory ids 1-13 stay: 1 Maupin, 2 Condon, 3 Wasco, 4 The Dalles, 5 Cascade Locks, 6 Hood River, 7 Arlington, 8 Carson, 9 White Salmon, 10 Roosevelt, 11 Goldendale, 12 Trout Lake, 13 Outlook. `BRDG` records are sorted by id (not edge order).

**u64 keys are not doubles:** `stableKey` values use all 64 bits (OSM type in bits 63..60, so every key is ≥ 2^60). They must be read, stored and compared as unsigned 64-bit integers. **Never parse them as IEEE doubles**: JavaScript `Number`, `JSON.parse` defaults, spreadsheet cells and C# `double` all lose precision above 2^53, and distinct keys would collide. In the debug JSON exports and `stable-ids.json` they are exact decimal integers. Read them with a 64-bit integer or bigint parser (C# `ulong`, JS `BigInt`, Python `int`), or as strings.

### 4.3 `CHOK.bypassSurfaceClass` (u8, v1)

The dominant land cover along the best bypass path of §4.1. Values use the land-cover enum (1 field, 2 scrub_rock, 3 urban, 4 forest, 5 marsh, 6 water, 7 snow_ice). The path is the one found by the minimax-bottleneck search (parent pointers; ties resolved by the same (maxGrade, cellIndex) key). Its cells, including both end cells, are counted by MVLC code; the most frequent code wins, and a tie goes to the lower code. **255** = no bypass; this holds exactly when `bypassMaxGradePpt` = 65535. The validator checks the value range and that consistency.

### 4.4 Territory partition (v1.2 movement-layer rule)

- **Seats:** chosen exactly as in v0 (soft-cost flood fill with seed spacing, oversize splits and small-territory merges). The golden map keeps the same 13 seats and ids.
- **Border rule:** a territory border can only be a terrain movement cell (1800 × 1800, about 89 m) that is impassable to **both** foot and wheeled units. In the baked data that is exactly `MVLC` code 6 (§4.6). Fords are carved passable. Bridges connect territories; they never carry a territory across.
- **Slope is not a border input, by design.** Slopes of 1000‰ or more (cliffs) close a cell to foot and wheeled units alike (golden map: 717 cliff cells, none on water). Territory borders stay water-only anyway; cliffs matter only through the chokepoint and bypass data (§4.1, §4.9). The S9 cliff check below keeps cliffs away from blocking-river borders.
- **Cells:**
  - Each 1-mile cell is split into *pieces*: its movement cells grouped by 4-connected ground component. Thanks to §4.6 watertightness, 4-connected components equal the 8-connected ones the sim uses.
  - Pieces are grown from the seats by a multi-source Dijkstra with the v0 step costs. Pieces on a landmass with no seat (islands, sandbars; 51 pieces on the golden map) take the cheapest soft path from a labelled piece.
  - `CELX.territoryId` is the territory of the cell's largest piece (tie: lower id). An all-water cell takes the nearest labelled cell.
- **Features:**
  - Towns, road nodes and edge midpoints take the territory of the piece holding their movement cell, or of the nearest ground cell if theirs is water.
  - Bridgehead road nodes of bridges over blocking water take the piece of their S8b bridgehead cell (§4.6).
  - A dry edge whose two ends share a territory carries it.
  - `REDG.territoryId` / `RNOD.territoryId` can therefore differ from the containing cell's `CELX.territoryId` along banks.
- **`RNOD.territoryId` (u16, v1):** the node's territory under this rule. The value equals the `TERR.id` of that territory: a u16 holding the same id as the i32 `TERR.id` (ids 1..n, n ≤ 65535). **0 is invalid in final v1**: the validator fails any road node with `territoryId` 0. Ids are pinned by `pipeline/stable-ids.json` (§4.2).
- **CI (S9, build fails):**
  - **River check:** from the seat piece, every town, node and edge piece of a territory can be reached over ground pieces of that same territory. Pockets are exempt.
  - **Minimum size:** every territory has at least `territories.minTerritoryCells` = 150 cells.
  - **Columbia bridges:** every main-river bridge span links two different territories and two different ground components, and each head lies in the territory of its own bank. The same check runs on every bridge over blocking water whose heads are in different ground components.
  - **Passable-water carve-out** (front consistency): for land-adjacent pieces p in T and q in U, D_U(q) ≤ D_T(q) + w and D_T(p) ≤ D_U(p) + w. Here D is the unrestricted ground distance from each seat and w is the step weight. So no territory is cut off by water that units can cross.
  - **Demotion what-if:** every river in `blockingWater.demoteToFordable` is blocked again in a what-if run, and must then leave a territory below the minimum size.
  - **Cliff check** (config `territories.cliffCheck`): a cliff cell next to a blocking-river border span fails only if it is within 1 km of a bridgehead or ford. The rule is in §4.6; the validator applies the same function.
- **Chokepoint sides and gates:** territory-pair minimum cuts are recomputed on the **final** partition. An edge in a cut of ≤ `maxCutEdges` edges takes that pair; otherwise the territories of its end nodes (0/0 when they are equal).
- **Selection unchanged:** which chokepoints exist is still decided on S9 `selectionPartition`, the v0 soft partition. The pair from that partition is kept in debug `selectionCutPair`.

### 4.5 Population fallback (v1)

Towns with no GeoNames/OSM population get the US Census 2020 P.L. 94-171 redistricting-file population (POP100) of the place/CDP (summary level 160) or AIANNH area (280) with the same name within 8 km. `Town.sourceFlags` bit4 marks it; the inputs (`census2020pl/or`, `census2020pl/wa`) are pinned in `raw/inputs.lock.json` and listed in `SRCS`. US Census data is public domain; it is not road data and does not mix with the ODbL road layer. Towns without a match keep population 0.

**Design overrides (v1.1):** a committed entry in `pipeline/config.json` `towns.populationOverrides` (keyed by OSM node id, name asserted) replaces a town's population with a designer value. `Town.sourceFlags` bit5 marks it, and no other population-source bit is set. **This is not sourced data** (not GeoNames, Census or OSM). Golden map: Welches = 250, at the Game Designer's request of 2026-09-28. The sourced value was 0: there is no Census 2020 place named Welches, and GeoNames has population 0.

### 4.6 Blocking water in the terrain movement data (v1.2)

**Rule** (`pipeline/config.json` `blockingWater`, applied by S1c; no river names are used):
- **Grouping:** OSM `waterway=river` ways that are not intermittent are grouped by their `type=waterway` relation. Ways in no relation are chained at endpoints where exactly two such ways meet.
- **Measures, per group:**
  - `waterCoverPpt`: the share of the in-map centreline length that lies inside OSM river-area polygons (`natural=water` + `water=river`, or `waterway=riverbank`);
  - `lengthPptOfExtent`: the in-map length × 1000 / map extent.
- **Blocking test:** a group blocks if (`waterCoverPpt` ≥ 500 **and** `lengthPptOfExtent` ≥ 100) **or** `lengthPptOfExtent` ≥ 600. Counted lakes are OSM water areas ≥ `edgeBlock.lakeMinAreaM2` that are not river areas.
- **Demotions:** `blockingWater.demoteToFordable` can list a river that meets the rule but must stay fordable, with the reason. S9 re-checks every entry (§4.4).
- **Fixture:** `pipeline/fixtures/gorge-blocking-rivers.json` is a CI check. On the Gorge it requires Columbia, Deschutes, John Day, Klickitat and lower Yakima to meet the rule, and White Salmon, Hood, White, Zigzag and Wind to stay fordable.

  | River | Cover ‰ | Length ‰ |
  |---|---:|---:|
  | Columbia | 104 | 1237 |
  | Deschutes | 995 | 815 |
  | John Day | 996 | 1132 |
  | Klickitat | 25 | 749 |
  | Yakima | 997 | 202 |

- **Near-misses:**
  - Length side (fordable): White River 472‰, White Salmon 441‰, Lewis 344‰, Clackamas 325‰, Wind 312‰.
  - Cover side (fordable, length ≥ 100‰): Sandy 306‰ cover, Hood River 177‰, Clackamas 129‰, Muddy 113‰.
- **Golden map:** the Yakima (relation 19778840) is demoted to fordable. With it blocking, Outlook (13) would have only 21 cells.

**Bake** (S8b, `movement/water_bake.json`):
- **Blocking set:** the cells on the centrelines of blocking groups, plus the cells inside counted lakes, plus the WorldCover water components that touch them.
- **Fords:** OSM ford cells on that set are carved out (passable).
- **Watertight against 8-way movement:** no two blocked cells touch only at a corner. Such contacts are closed by blocking one of the two open cells (golden map: 286 cells thickened, plus 1 deck-extension cell). Fords and bridgeheads are never filled.
- **Bridgeheads:** for each bridge over blocking water, the head on each bank is the first bank cell (in a ground component of at least 324 cells) outward from the blocked run along the bridge line. Both heads stay passable.
- **Codes written to `MVLC`:** 6 for blocking water (golden map: 37 737 cells); 5 (marsh) for fordable water, that is WorldCover water outside the blocking set, carved ford cells and (v1-marsh, §4.6a) the fordable river lines; all other cells keep their WorldCover code.
- **CI (build fails):**
  - 0 diagonal leaks, and 4- and 8-connected component counts are equal;
  - no ford or bridgehead cell is blocked;
  - **no foot or wheeled route crosses a blocking river away from its bridges and fords:** samples every 2 km along each river, 8-way search within a 3 km window;
  - every bridgehead is reachable from its own bank.

**Cliffs next to blocking-river borders** (S9 CI and validator; config `territories.cliffCheck`; the Lead Engineer accepted this general rule on 2026-09-28, replacing the earlier 11-cell allow-list, which is removed):
- **Cliff cell:** a movement cell with `MVSL` ≥ `cliffPpt` = 1000‰ (closed to foot and wheeled).
- **Border span:** the `MVLC`-6 cells that have bank cells of at least two different territories within `spanWindowCells` = 25 cells (Chebyshev distance). A bank cell is a non-6 cell 8-adjacent to an `MVLC`-6 cell; its territory is the `CELX.territoryId` of its 1-mile cell (0 is ignored).
- **Span-adjacent:** the cliff cell is a span cell or is 8-adjacent to one.
- **Crossings:** bridgeheads (the movement cells of `RNOD` nodes flagged BridgeHead) and fords (`MVFD` cells).
- **Rule:** a span-adjacent cliff cell **fails the build only if** its straight-line distance to the nearest crossing is ≤ `maxCrossingDistM` = 1000 m. The distance is Euclidean between cell centres, in cells × the map cell size (`GRID` / META `cellSizeCm`; 80467/9 cm ≈ 89.4 m on the golden map). A cliff that far from any crossing only thickens a river line that already blocks; a cliff near a crossing could close off the approach, so it must be fixed.
- **Same code in both places:** `wrmb_reader.cliff_crossing_check` is the single implementation. S9 calls it on the pipeline arrays; the validator (§5) calls it on the decoded pair (`cliff_check_model`) and reports `info.cliffCheck`. `pipeline/config.json` values must equal the reader constants `CLIFF_*` (S9 asserts this).
- **Golden map:** 717 cliff cells; **11 span-adjacent**, all canyon-wall bank cells, none on water: 2 on the John Day (territory 3 side) and 9 on the upper Klickitat (territory 11 side). The **nearest crossing to any of them is 5 365 m** (a bridgehead, from cell 2844751); the farthest is about 9.0 km. **0 fail.**

### 4.6a Fordable rivers as marsh lines (v1-marsh)

**Rule** (S8b; no river names): fordable OSM rivers, that is every river group that is not blocking (80 groups on the Gorge, including the demoted Yakima), are rasterized as continuous `MVLC` 5 lines.
- **Line:** the cells crossed by each river way's centreline (samples every ≤ 20 m). Cells that are code 6 stay 6.
- **Open gaps:** the line cells within 1 cell of the midpoint of a road bridge over such a river (392 cells) and every ford cell (`MVFD`; 19 on the lines) stay as they were (passable ground).
- **Watertight against 8-way movement:** every 2 × 2 window whose closed cells (marsh line plus code 6) are exactly one diagonal pair and that touches the line is closed by adding the open, unprotected cell with the lower index (fixed point; golden map: 692 cells).
- **Golden map:** line 12 189 cells, +11 841 new code-5 cells (code 5: 1 416 → 13 257). The marsh line is closed to wheeled units (`bypassWheeled` closes code 5 except at fords); foot units and the sim's movement costs treat code 5 as before.
- **Effect:** `CHOK.bypassWheeledBlocked` 26 → 29 (chokepoints 5 Mount Hood Highway, 13 Lewis and Clark Trail Highway, 21 SR 223 gain a closed bypass). When a chokepoint's own bridge is tested, the gap the river line leaves open at that bridge is closed for that test only.
- **CI (S10d, build fails; config `chokepoints.marsh`):**
  - the added wheeled-blocked set is exactly {5, 13, 21} and nothing is removed;
  - no territory loses wheeled access: every seat pair that had a wheeled route before still has one, and a territory that could leave through its border bridges or fords before still can;
  - home-to-buffer routes stay near today's: each seat route of Hood River-The Dalles, Hood River-White Salmon, Goldendale-The Dalles and Goldendale-White Salmon grows by at most 1 km, and no seat pair grows by more than 6 km. Golden map: 30.22 / 4.01 / 40.67 / 60.01 km (was 30.22 / 4.01 / 40.58 / 60.01); the largest increase is about +5.4 km (Maupin to Outlook).
- **Territories:** unchanged (the partition uses `MVLC` 6 only).

**Licence separation:** `MVLC` mixes ESA WorldCover (CC BY 4.0) with OSM-derived cells (ODbL). The OSM-derived cells are exactly those where `movement/landcover.u8` differs from the WorldCover-only `movement/landcover_worldcover.u8` (listed by `movement/osm_water_mask.u8`; golden map: 5440 cells). `MVFD` and `WTRA` are OSM-derived. This mirrors the ford layer separation.

### 4.7 `WTRA`: art-only water marks (terrain file, v1.2)

- **Tag and purpose:** a terrain section (required in final v1), tag **`WTRA`** ("water art"), placed after `MVEL`. It gives artists and the tactical map the water type of every movement cell, plus a list of fords.
- **Not sim or path data:** the sim does not read `WTRA`. It is **not** in `terrainPayloadSha256` and has no GRID descriptor (the section describes itself), so changing it cannot change the sim's movement data or the payload hash. It **is** covered by the terrain file's header `contentHash` (whole file) and by its own directory CRC-32: an art-only change alters `contentHash` (and so the main/terrain pairing via `pairedContentSha256` is rebuilt) but not `terrainPayloadSha256`. No spare bits of the movement sections are used.
- **Required in final v1:** a formatVersion 1 terrain file without `WTRA` fails validation (a pre-final v1 terrain file).
- **Encoding** (little-endian; `elementSize` 1, `count` = byte length):
  - Header: `u32 version` (**2** in v1-marsh files; 1 = merged v1 files, still decoded), `u32 width`, `u32 height` (= the META move grid, 1800 × 1800), `u32 fordCount`.
  - Water marks: `u8 code[width × height]`, row-major, row 0 = south, same cell order as `MVLC`. Codes: **0** land, **1** blocking water, **2** fordable water, **3** ford, **4** wetland (version 2: an `MVLC` 5 cell that is neither river water nor a ford, that is WorldCover marsh; 32 cells on the Gorge).
  - Zero padding to an 8-byte boundary (0 bytes at 1800 × 1800).
  - Ford list: `fordCount` × 16 B records, sorted by `cellIndex`: `u32 cellIndex`, `u8 osmType` (0 none, 1 node, 2 way, 3 relation), 3 pad bytes, `u64 osmId`. Each record is one ford cell, carrying the lowest (type, id) of the OSM ford features in that cell.
  - Golden map: 954 fords, 3 255 280 B.
- **Validator** (`tools/map/wrmb_reader.py`; the build fails on any error; `water_art(model)` exposes the decoded section):
  - version, dims and length;
  - every code ≤ 3 (version 1) or ≤ 4 (version 2);
  - **code 1 ⇔ `MVLC` = 6** (blocking water equals exactly the cells pathing treats as impassable water);
  - code 3 ⇔ `MVFD` bit set;
  - no fordable water on code-6 cells;
  - the ford list is strictly sorted and equals the `MVFD` cells, and an `osmId` is present whenever `osmType` ≠ 0;
  - **match check (version 2, all cells):** every code-2 cell is `MVLC` 5 (ford cells, code 3, are exempt from being code 5); every `MVLC` 5 cell that is not river water (2) or a ford (3) is code 4 wetland; every code-4 cell is `MVLC` 5 and not a ford cell. The report has the counts in `info.wtra.marshMatch`;
  - with `--movement-dir`: the codes equal `movement/water_type.u8`, and the ford list equals `water_bake.json` `fordList`.

### 4.8 `CHOK.defenseGroupId` (u8, final v1)

**Purpose:** chokepoints that guard the same approach between the same two territories form one *defense group*. The AI garrisons by group, and the tactical map draws one bracket per group. Both read this field.

**Encoding:**
- `u8` at record offset 7, the padding byte after `widthClass` in v0. The record size stays **88 B**, and every other field keeps its offset.
- **255 = no group.** No aligned 2-byte gap was free, so the field is u8, not u16.
- **Limit: 254 groups (ids 0-253).** Id 254 is never valid, so an overflow can never alias "no group". The chokepoint global cap is 50.
- **Enforced by the validator:** `check_defense_groups` fails any map that needs more than 254 groups. `pipeline/tests/test_reader_final_v1.py` proves it with a synthetic 255-group set and with the golden file forced to group id 254; the build runs this test. S10b also fails such a build.
- v0 files wrote 0 in this byte; the v0 schema does not read it. Pre-final v1 files are rejected (no `V1_FINAL` flag).

**Rule** (config `chokepoints.defenseGroup`, stage S10b `s10b_defgroups.py`, output `ai/defense_groups.json`):
1. **Only border chokepoints are grouped:** `sideATerritoryId` ≠ `sideBTerritoryId`, both non-zero. Internal chokepoints (0/0) get 255.
2. **Link:** two border chokepoints are linked when both of these hold:
   - they have the same unordered (sideA, sideB) pair;
   - they **cover the same approach**: the shortest passable road route between them is at most `maxSpanM` = **20 000 m**. The route runs from any node of one (its `roadNodeId`, `alsoNodeIds`, and the end nodes of its own and removed edges) to any node of the other. Every third border chokepoint with a **different** side pair is removed from the graph (its nodes and its own/removed edges), so a linking route never passes through a chokepoint that guards another border.
3. **Groups** are single-linkage components: linked chains join.
4. **Ids** are dense 0..N-1, assigned in increasing order of each group's lowest member chokepoint id. This is deterministic.
5. **Permanent group key:** the minimum `CHOK.stableKey` among the members (u64, §4.2). It is **not stored** in the file; derive it with `wrmb_reader.defense_groups(model)`. The committed manifest `pipeline/stable-ids.json` records every group under `defenseGroups` (key → id, side pair, members). `stable_ids.py --check` fails the build if the groups derived from the file do not match exactly. Changing the manifest is a deliberate step (`--update`).

**Threshold choice:**
- The longest link the Gorge fixture needs is Klickitat bridge 11 to bridge 17 (16.1 km of road).
- The nearest rejected same-pair link is John Day bridge 1 to junction 42 (23.1 km).
- 20 km sits between the two.

**CI** (S10b, build fails; the validator re-checks everything except the fixture):
- all members share one side pair;
- internal chokepoints have 255, and border chokepoints have a group;
- ids are dense 0..N-1 in the documented order;
- at most 254 groups (validator and S10b);
- group keys match the manifest;
- Gorge fixture `pipeline/fixtures/gorge-defense-groups.json`: 28 and 45 share a group, 11, 17 and 19 share a group, 20 and 41 share a group, and each Columbia bridge (7, 9, 10, 14) is alone.

**Golden map:** 25 border chokepoints form 16 groups; 24 chokepoints are internal.

| Group | Key (min member stableKey) | Sides | Members (chokepoint id: kind, road) |
|---:|---|---|---|
| 0 | 2310346608915341918 | 2/3 | 1: Bridge, Shaniko-Fossil Hwy (John Day) |
| 1 | 2310346608878939605 | 1/3 | 2: Bridge, US-197 at Maupin (Deschutes); 28: Gap, Sherar's Bridge Hwy; 45: Junction, Sherars Bridge Hwy |
| 2 | 2310346608846148242 | 2/3 | 6: Bridge, Wasco-Heppner Hwy (John Day); 48: Junction, Cottonwood Rd |
| 3 | 2310346608979386899 | 4/11 | 7: Bridge, The Dalles Bridge |
| 4 | 2310346608846260059 | 3/4 | 8: Bridge, Celilo-Wasco Hwy (Deschutes mouth) |
| 5 | 2310346609387311266 | 5/8 | 9: Bridge, Bridge of the Gods |
| 6 | 2310346610378898047 | 3/11 | 10: Bridge, Sam Hill Memorial Bridge |
| 7 | 2310346608880263783 | 9/11 | 11: Bridge, Lewis and Clark Trail Hwy; 17: Bridge, SR 142; 19: Bridge, SR 142; 40: Gap, SR 142 (Klickitat) |
| 8 | 2310346608931635955 | 6/9 | 14: Bridge, Hood River Bridge |
| 9 | 2310346609017583161 | 11/12 | 20: Bridge, Glenwood Hwy (Klickitat); 41: Gap, Glenwood Hwy |
| 10 | 2319353808101921320 | 8/9 | 27: Causeway, Lewis and Clark Trail Hwy |
| 11 | 2328361007356476416 | 10/11 | 38: Gap, Roosevelt Grade Rd; 49: Junction, Lewis and Clark Trail Hwy |
| 12 | 2337368207981083403 | 2/3 | 42: Junction, Shaniko-Fossil Hwy |
| 13 | 2337368206643164723 | 1/6 | 43: Junction, Wapinitia Hwy |
| 14 | 2337368206610489027 | 2/7 | 44: Junction, Cottonwood Rd; 46: Junction, John Day Hwy |
| 15 | 2337368206796313568 | 1/4 | 47: Junction, The Dalles-California Hwy |

### 4.9 `CHOK.bypassWheeledBlocked` (u8, final v1)

- **Where:** `u8` at record offset **79**, the last padding byte (after `bypassSurfaceClass`, before `stableKey`). No field moves, the record stays **88 B**, and the `CHOK` record now has no padding left.
- **Value:** **1** if no cross-country bypass around the chokepoint is passable for wheeled units under the baked movement data, otherwise **0**.
  - It is always 1 when `bypassMaxGradePpt` = 65535; the validator checks this.
  - Values other than 0 and 1 are invalid.
- **Computation** (stage S10c `s10c_wheeled.py`, algorithm `wheeled.py`, config `chokepoints.bypassWheeled`):
  - **Region and endpoints:** the same as §4.1 — the approach-node A cell to the B cell, R and X as §4.1, endpoints always open.
  - **Moves:** **8-connected**, as the sim moves.
  - **A cell is closed to wheeled units if:**
    - `MVLC` = 6 (blocking water); or
    - `MVLC` = 5 (marsh / fordable water) and the cell is **not** an OSM ford (`MVFD`); or
    - `MVSL` > 299‰, the designer movement table's wheeled slope limit on 2026-09-28, baked here on request.
  - The flag is 1 if the A cell cannot reach the B cell.
- **Golden map:** 26 of 49 chokepoints are 1. Of these, 21 have no bypass at all (65535). The other 5 (4, 16, 34, 35, 37) have a foot bypass (grade 315-453‰) that wheeled units cannot use.

### Enums and bit fields

- **`META.fileKind`:** 0 main/AI, 1 terrain.
- **`RoadNode.flags`:** 1 TownGate, 2 BridgeHead, 4 Chokepoint, 8 Way, 16 TerritoryBorder (reserved; edges are not yet split at territory borders), 32 Pass (DEM pass node or OSM mountain_pass), 64 MapBorder, 128 Cluster.
- **`RoadNode.kind` (legacy §3.1):** 0 Junction, 1 TownGate, 2 Bridge(head), 3 Chokepoint, 4 Way. Precedence: TownGate > Chokepoint > BridgeHead > Way.
- **`RoadEdge.flags`:** 1 bridge (any OSM bridge on the edge), 2 bridgeSpan (bridge-head model span over a blocking waterway), 4 tunnel, 8 destructible, 16 repairable, 32 ford, 64 mainRiverBridge, 128 crossesBlockingWater.
- **`RoadEdge.highwayClass`:** 0 motorway, 1 trunk, 2 primary, 3 secondary, 4 tertiary, 5 unclassified, 6 residential, 7 track (grade1-2 or sole link).
- **`RoadEdge.condition`:** 0 intact, 1 damaged, 2 wrecked, 3 destroyed. The transform is a no-op in v0, so every edge is 0.
- **`Chokepoint.kind`:** 0 Bridge, 1 Pass, 2 Causeway, 3 Dam, 4 Gap, 5 Tunnel, 6 Junction.
- **`Chokepoint.flags`:** bit0 inferredBridge, bit1 onlyCrossing (detour capped), bit2 destroyedByTransform, bit3 roadless.
- **`Town.sourceFlags`:** bit0 population from GeoNames, bit1 OSM population fallback, bit2 interior collapsed to a gate, bit3 off-road (`offRoadDistanceCm` > 0), bit4 population from the US Census 2020 fallback (v1), bit5 population is a manual design override, not sourced data (v1.1). The population-source bits 0, 1, 4 and 5 are mutually exclusive.
- **`Bridge.flags`:** bit0 inferred, bit1 tunnel, bit2 damCrossing, bit3 mainRiver, bit4 lakeOnly (no river id).
- **`River.kind`:** 0 river, 1 canal. **`River.flags`:** bit0 blocksLand, bit1 intermittent.
- **`Water.kind`:** 0 lake/other, 1 reservoir, 3 river area (non-blocking; the centreline governs). **`Water.flags`:** bit0 blocksLand.
- **`Resource.kind`:** 0 fuel_station, 1 fuel_storage, 2 refinery, 3 industrial_area.
- **Land cover (`CELX.landCover`, `MVLC`, v1 `CHOK.bypassSurfaceClass`):** 0 nodata, 1 field, 2 scrub_rock, 3 urban, 4 forest, 5 marsh (v1.2 `MVLC`: also fordable water), 6 water (v1.2 `MVLC`: impassable blocking water, §4.6), 7 snow_ice (reserved but populated). No unit speeds are baked in.
- **`GRID.elemType`:** 0 struct record (elementSize from the directory), 1 u8, 2 u16 LE, 3 bitset (LSB-first). **`GRID.rowOrder`:** 0 = row 0 south.
- **`META.projectionType`:** 1 = Transverse Mercator on WGS84 (centre in micro-degrees, k in ppb, false easting/northing in cm). **`verticalDatum`:** 1 = EGM2008 (Copernicus).
- **Elevation encoding (MVEL):** `elevationCm = elevOffsetCm + value × elevUnitCm`. `elevOffsetCm` is 0 unless the map goes below sea level; the writer then picks the next lower multiple of 100 m. The u16 range covers 6553.5 m above the offset at 10 cm units. A map with more relief would need `elevUnitCm` 20, which the format allows. Each value is the floor mean of the 3 × 3 heightmap pixels (30 m) in the cell, rounded half-up to the unit.
- **`nameHash`:** FNV-1a 32 over the UTF-8 name, stored as signed int32 (debug only).

### Field tables

<!-- BEGIN GENERATED TABLES -->

#### `META`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `schemaVersion` | i32 |  |
| 4 | `fileKind` | i32 |  |
| 8 | `mapWidthCm` | i32 |  |
| 12 | `mapHeightCm` | i32 |  |
| 16 | `cellSizeCm` | i32 |  |
| 20 | `gridWidth` | i32 |  |
| 24 | `gridHeight` | i32 |  |
| 28 | `townCount` | i32 |  |
| 32 | `territoryCount` | i32 |  |
| 36 | `roadNodeCount` | i32 |  |
| 40 | `roadEdgeCount` | i32 |  |
| 44 | `chokepointCount` | i32 |  |
| 48 | `riverCount` | i32 |  |
| 52 | `bridgeCount` | i32 |  |
| 56 | `waterBodyCount` | i32 |  |
| 60 | `minorSettlementCount` | i32 |  |
| 64 | `resourceCount` | i32 |  |
| 68 | `sourceCount` | i32 |  |
| 72 | `projectionType` | i32 |  |
| 76 | `centerLatMicroDeg` | i32 |  |
| 80 | `centerLonMicroDeg` | i32 |  |
| 84 | `falseEastingCm` | i32 |  |
| 88 | `falseNorthingCm` | i32 |  |
| 92 | `scaleFactorPpb` | i32 |  |
| 96 | `verticalDatum` | i32 |  |
| 100 | `lodLadderLevel` | i32 |  |
| 104 | `moveWidth` | i32 |  |
| 108 | `moveHeight` | i32 |  |
| 112 | `moveCellsPerCell` | i32 |  |
| 116 | `transformSeed` | u64 |  |
| 124 | `mapId` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 132 | `pipelineVersion` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 140 | `transformVersion` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 148 | `attribution` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 156 | `provenanceJson` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 164 | `attributionSha256` | b32 | 32 raw bytes (SHA-256) |
| 196 | `weightsSha256` | b32 | 32 raw bytes (SHA-256) |
| 228 | `configSha256` | b32 | 32 raw bytes (SHA-256) |
| 260 | `osmClipSha256` | b32 | 32 raw bytes (SHA-256) |
| 292 | `elevOffsetCm` | i32 |  |
| 296 | `elevUnitCm` | i32 |  |
| 300 | `pairedFileName` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 308 | `pairedContentSha256` | b32 | 32 raw bytes (SHA-256) |
| 340 | `terrainPayloadSha256` | b32 | 32 raw bytes (SHA-256) |
| 372 | `pad` | zero padding |  |

Record size: **376 B**.

#### `SRCS`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `key` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 12 | `version` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 20 | `licence` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 28 | `sha256` | b32 | 32 raw bytes (SHA-256) |

Record size: **60 B**.

#### `GRID`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `sectionTag` | tag4 | 4 ASCII bytes (section FourCC) |
| 4 | `elemType` | u8 |  |
| 5 | `rowOrder` | u8 |  |
| 6 | `pad` | zero padding |  |
| 8 | `width` | i32 |  |
| 12 | `height` | i32 |  |
| 16 | `originXCm` | i32 |  |
| 20 | `originYCm` | i32 |  |
| 24 | `extentXCm` | i32 |  |
| 28 | `extentYCm` | i32 |  |
| 32 | `cellSizeNumCm` | i32 |  |
| 36 | `cellSizeDen` | i32 |  |
| 40 | `elemBytes` | i32 |  |
| 44 | `valueOffset` | i32 |  |
| 48 | `valueScale` | i32 |  |
| 52 | `pad2` | zero padding |  |

Record size: **56 B**.

#### `TOWN`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `positionCm` | pos | int32 x, int32 y (cm) |
| 12 | `territoryId` | i32 |  |
| 16 | `nameHash` | i32 |  |
| 20 | `population` | i32 |  |
| 24 | `offRoadDistanceCm` | i32 |  |
| 28 | `roadNodeId` | i32 |  |
| 32 | `strategicValue` | i16 |  |
| 34 | `populationClass` | i8 |  |
| 35 | `sourceFlags` | u8 |  |
| 36 | `neighborTownIds` | ids | u32 offset, u32 count into I32P (int32 elements, sorted) |
| 44 | `exitEdgeIds` | ids | u32 offset, u32 count into I32P (int32 elements, sorted) |
| 52 | `name` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 60 | `pad` | zero padding |  |

Record size: **64 B**.

#### `TDBG`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `townId` | i32 |  |
| 4 | `P` | i16 |  |
| 6 | `R` | i16 |  |
| 8 | `C` | i16 |  |
| 10 | `H` | i16 |  |
| 12 | `X` | i16 |  |
| 14 | `base` | i16 |  |
| 16 | `abs` | i16 |  |
| 18 | `pad` | zero padding |  |

Record size: **20 B**.

#### `TERR`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `seatTownId` | i32 |  |
| 8 | `areaCells` | i32 |  |
| 12 | `areaHa` | i32 |  |
| 16 | `centroidCm` | pos | int32 x, int32 y (cm) |
| 24 | `townIds` | ids | u32 offset, u32 count into I32P (int32 elements, sorted) |
| 32 | `neighborTerritoryIds` | ids | u32 offset, u32 count into I32P (int32 elements, sorted) |
| 40 | `stableKey` | u64 |  |

Record size: **48 B**.

#### `RNOD`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `positionCm` | pos | int32 x, int32 y (cm) |
| 12 | `elevationCm` | i32 |  |
| 16 | `flags` | u8 |  |
| 17 | `kind` | i8 |  |
| 18 | `territoryId` | u16 |  |
| 20 | `chokepointId` | i32 |  |

Record size: **24 B**.

#### `REDG`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `a` | i32 |  |
| 8 | `b` | i32 |  |
| 12 | `lengthCm` | i32 |  |
| 16 | `travelCost` | i32 |  |
| 20 | `climbCmAB` | i32 |  |
| 24 | `climbCmBA` | i32 |  |
| 28 | `elevDeltaCm` | i32 |  |
| 32 | `controlTownId` | i32 |  |
| 36 | `territoryId` | i32 |  |
| 40 | `widthClass` | i8 |  |
| 41 | `surface` | i8 |  |
| 42 | `condition` | i8 |  |
| 43 | `highwayClass` | i8 |  |
| 44 | `flags` | u16 |  |
| 46 | `pad` | zero padding |  |
| 48 | `geometry` | pts | u32 offset, u32 count into PTSP (int32 x,y pairs, cm) |
| 56 | `name` | str | u32 offset, u32 byteLength into STRP (UTF-8) |

Record size: **64 B**.

#### `CHOK`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `kind` | i8 |  |
| 5 | `flags` | u8 |  |
| 6 | `widthClass` | i8 |  |
| 7 | `defenseGroupId` | u8 |  |
| 8 | `positionCm` | pos | int32 x, int32 y (cm) |
| 16 | `elevationCm` | i32 |  |
| 20 | `roadNodeId` | i32 |  |
| 24 | `roadEdgeId` | i32 |  |
| 28 | `widthCm` | i32 |  |
| 32 | `defensibility` | i16 |  |
| 34 | `detourRatioPpt` | i16 |  |
| 36 | `approachReliefCmA` | i32 |  |
| 40 | `approachReliefCmB` | i32 |  |
| 44 | `sideATerritoryId` | i32 |  |
| 48 | `sideBTerritoryId` | i32 |  |
| 52 | `gatesTerritoryIds` | ids | u32 offset, u32 count into I32P (int32 elements, sorted) |
| 60 | `relatedTownId` | i32 |  |
| 64 | `riverId` | i32 |  |
| 68 | `alsoNodeIds` | ids | u32 offset, u32 count into I32P (int32 elements, sorted) |
| 76 | `bypassMaxGradePpt` | u16 |  |
| 78 | `bypassSurfaceClass` | u8 |  |
| 79 | `bypassWheeledBlocked` | u8 |  |
| 80 | `stableKey` | u64 |  |

Record size: **88 B**.

#### `RIVR`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `kind` | i8 |  |
| 5 | `flags` | u8 |  |
| 6 | `pad` | zero padding |  |
| 8 | `widthCm` | i32 |  |
| 12 | `polylineCm` | pts | u32 offset, u32 count into PTSP (int32 x,y pairs, cm) |
| 20 | `name` | str | u32 offset, u32 byteLength into STRP (UTF-8) |
| 28 | `pad2` | zero padding |  |

Record size: **32 B**.

#### `BRDG`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `roadEdgeId` | i32 |  |
| 8 | `nodeAId` | i32 |  |
| 12 | `nodeBId` | i32 |  |
| 16 | `riverId` | i32 |  |
| 20 | `lengthCm` | i32 |  |
| 24 | `deckElevationCm` | i32 |  |
| 28 | `widthClass` | i8 |  |
| 29 | `condition` | i8 |  |
| 30 | `flags` | u16 |  |
| 32 | `stableKey` | u64 |  |

Record size: **40 B**.

#### `WATR`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `kind` | i8 |  |
| 5 | `flags` | u8 |  |
| 6 | `pad` | zero padding |  |
| 8 | `areaHa` | i32 |  |
| 12 | `ringIds` | ids | u32 offset, u32 count into I32P (int32 elements, sorted) |
| 20 | `name` | str | u32 offset, u32 byteLength into STRP (UTF-8) |

Record size: **28 B**.

#### `WRNG`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `waterBodyId` | i32 |  |
| 8 | `role` | i8 |  |
| 9 | `pad` | zero padding |  |
| 12 | `ringCm` | pts | u32 offset, u32 count into PTSP (int32 x,y pairs, cm) |

Record size: **20 B**.

#### `MSET`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `positionCm` | pos | int32 x, int32 y (cm) |
| 12 | `nearestRoadNodeId` | i32 |  |
| 16 | `name` | str | u32 offset, u32 byteLength into STRP (UTF-8) |

Record size: **24 B**.

#### `RSRC`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `id` | i32 |  |
| 4 | `kind` | i8 |  |
| 5 | `pad` | zero padding |  |
| 8 | `positionCm` | pos | int32 x, int32 y (cm) |
| 16 | `areaM2` | i32 |  |
| 20 | `nearestRoadNodeId` | i32 |  |
| 24 | `roadNodeDistCm` | i32 |  |
| 28 | `nearestTownId` | i32 |  |
| 32 | `name` | str | u32 offset, u32 byteLength into STRP (UTF-8) |

Record size: **40 B**.

#### `CELL`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `elevationCm` | i32 |  |
| 4 | `slopePpt` | i16 |  |
| 6 | `class` | i8 |  |
| 7 | `pad` | zero padding |  |

Record size: **8 B**.

#### `CELX`

| Offset | Field | Type | Notes |
|---:|---|---|---|
| 0 | `territoryId` | i32 |  |
| 4 | `steepFractionPpt` | i16 |  |
| 6 | `waterFractionPpt` | i16 |  |
| 8 | `coverPpt` | i16 |  |
| 10 | `landCover` | i8 |  |
| 11 | `edgeBlockMask` | u8 |  |

Record size: **12 B**.

<!-- END GENERATED TABLES -->

## 5. Validator checks (`tools/map/wrmb_reader.py`)

1. **Header and directory (each file):** magic, `formatVersion` ∈ {0, 1} (selects the schema), **formatVersion 1 requires header flag bit2 `V1_FINAL`** (stale pre-final v1 files are rejected), `elementSize` equal to the schema / pool / raw element size for **every** section (WTRA = 1), header size, `fileSize`, `contentHash`, 8-byte alignment, bounds, per-section CRC-32, `count × elementSize = byteLength`, `elementSize` equal to the schema.
2. **Pool spans:** every span lies inside its pool.
3. **Round trips:** `encode(decode(file)) == file`. With the debug exports: the model equals the JSON, and JSON → binary is byte-identical. With `--movement-dir`: MVSL/MVLC/MVFD/MVEL (and v1.2 WTRA) equal the pipeline's source arrays.
4. **GRID:** every descriptor points at a present section, with origin (0, 0), extent = map, row order south-first, and cell size an exact reduced rational (`num × width = extent × den`). Byte length matches width × height × elemBytes (bitset: ceil(bits / 8)), and dims match META.
5. **Per-fileKind section sets:**
   - Main: the AI sections plus a GRID for CELL/CELX, no terrain sections, `pairedContentSha256` zero.
   - Terrain: MVSL/MVLC/MVFD/MVEL with GRID descriptors, AI counts 0, MVLC codes ≤ 7, `terrainPayloadSha256` recomputed, MVEL offset/scale = META. The optional art-only `WTRA` (v1.2) is validated as in §4.7, including blocking marks = `MVLC` code 6. It is not in the payload hash.
6. **Per-section xxHash64 (v1-marsh, report and manifest):** for every section of each file the report has `sectionXxh64.<TAG> = {count, bytes, xxh64}`. The hash is xxHash64 (seed 0, official algorithm, same as `Wasteland.Sim.Hashing.XxHash64.Hash`) over `tag` (4 ASCII bytes) | `count` (u32 little-endian, the directory element count) | the section bytes exactly as stored. The same values are in `manifest.json` `blob.sectionXxh64`. `wrmb_reader.section_xxh64(tag, count, bytes)` and `section_hashes(file_bytes)` compute them; they are for the Lead's `MapKey.For` cross-check and are not stored in the file.
7. **Main invariants:**
   - META counts match the sections, and the extent equals `grid × cellSizeCm`.
   - Dense sorted ids.
   - Edges have `a < b` and are sorted by (a, b).
   - Every reference is valid, id lists are sorted and unique, and neighbour lists are symmetric.
   - One connected passable road graph, and every town gate reachable.
   - Bridge-head model: each span edge has two BridgeHead ends, each BridgeHead touches a span edge, and BRDG equals the set of span edges.
   - Territories: each seat is in its own town list, each town is in exactly its own territory, and `areaCells` equals the painted CELX cells.
   - `edgeBlockMask` is symmetric.
   - Value ranges hold (incl. `bypassMaxGradePpt` ≤ 65534 or = 65535).
   - v1 only: `stableKey` non-zero, unique per section, valid OSM type (TERR node, BRDG way), disc 0 for TERR/BRDG and `kind + 1` for CHOK; `bypassSurfaceClass` ≤ 7 or 255, and 255 exactly when `bypassMaxGradePpt` = 65535. `RNOD.territoryId` is 0 or an existing TERR id. A dry edge (flags without bridge/span/crossesBlockingWater) whose two end nodes share a non-zero territory carries it. A town's gate node lies in the town's territory, unless the gate is shared by towns of several territories. Population-source bits are exclusive, and bit5 requires population > 0. Final v1: **no road node has `territoryId` 0**, `bypassWheeledBlocked` ∈ {0, 1} and 1 whenever `bypassMaxGradePpt` = 65535, at most 254 defense groups, and a terrain file must carry `WTRA`. v1.3 `defenseGroupId` (§4.8): 255 exactly for internal chokepoints, ids dense 0..N-1 in order of each group's lowest member id, one side pair per group; the derived groups (with keys) are reported in `info.defenseGroups`.
   - `attributionSha256` matches the attribution text.
8. **Pair (`--terrain`):**
   - The file kinds are (main, terrain).
   - The terrain `pairedContentSha256` equals the main `contentHash`.
   - `terrainPayloadSha256` is equal in both files and matches the payload.
   - `pairedFileName` matches the other file's actual name, in both directions.
   - META map/grid/projection/version/hash fields are identical, and SRCS are identical.
   - Every non-65535 `bypassMaxGradePpt` is ≤ max(MVSL).
   - v1: **cliff rule (§4.6):** no cliff cell (`MVSL` ≥ 1000‰) inside or 8-adjacent to a blocking-river border span lies within 1000 m of a bridgehead or ford. Result in `info.cliffCheck`.

## 6. Open points

- **Record layouts:** frozen for formatVersion 1 (`DRAFT_LAYOUT` cleared, `V1_FINAL` set). Any record change needs formatVersion 2. Renaming or re-ordering sections is free for readers that use the directory.
- **Render heightmap:** the full-resolution 5400 × 5400 heightmap is a later separate asset (format TBD: likely a third fileKind or a plain 16-bit raster with the same GRID descriptor conventions).
- **Terrain file size:** ~16.6 MB uncompressed (+3.3 MB for v1.2 `WTRA`). Slope u16 is 6.5 MB and elevation u16 is 6.5 MB. Container-level compression or u8 slope quantization remain options.
- **Missing fields:** the gameplay fields (`supplyCapacity`, etc.) are left out, per contract change 4. They are resolved at load from the designer table keyed by `populationClass`.
- **`TerritoryBorder`** node flag and `RoadEdge.territoryId` exist, but edges are not yet split at territory crossings. `territoryId` is the territory of the edge midpoint's ground piece (§4.4).
- **String encoding:** the pool has no dedup. Names are debug/UI only.
- **Stable-key manifest pipeline check** is part of `pipeline/build.py` (after the validator); the stand-alone `tools/map/wrmb_reader.py` checks key form/uniqueness only, not the manifest.
