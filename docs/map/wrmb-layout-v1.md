# WRMB map files, layout v1 (formatVersion 1, container approved)

> **Status: container and section directory APPROVED by the Lead Engineer (2026-09-28).** This is layout **v1**: `formatVersion` = 1. Header flag bit0 `DRAFT_LAYOUT` **stays set** in v1 files: the record layouts are still not frozen (v1 only adds fields). Readers must find sections through the directory by tag, never by fixed offsets, and must pick the record schema by the header `formatVersion`.
> The files under `data/golden/` are the CI golden pair for formatVersion 1 of `golden-ci-columbia-gorge-100`. They sit next to the unchanged v0 pair. Record layouts are not frozen; the committed bytes are.
>
> **Changes from v0 (summary, details in §4.1-§4.7):**
> - `TERR` +`stableKey` u64 (40 → 48 B), `BRDG` +`stableKey` u64 (32 → 40 B), `CHOK`: the 2 padding bytes after `bypassMaxGradePpt` become `bypassSurfaceClass` u8 + 1 pad byte, then +`stableKey` u64 (80 → 88 B), `RNOD`: the 2 padding bytes after `kind` become `territoryId` u16 (size unchanged, 24 B). All other records are byte-for-byte the v0 layout.
> - Ids of territories, bridges and chokepoints are pinned by the committed manifest `data/golden/stable-ids.json` (seeded from v0, so every v1 id equals its v0 id).
> - Territory partition follows the terrain movement layer (v1.2, §4.4): a border can only be a movement cell that is impassable to both foot and wheeled units (`MVLC` code 6). This changes territory, side and gate fields, not the layout.
> - **v1.2 terrain movement data (§4.6):** blocking rivers and lakes, chosen by a measurable rule with no river names, are baked into `MVLC` as code 6 and made watertight against 8-way movement. Fordable water is baked as 5 (marsh). Bridges and fords are the only crossings. `CELX.edgeBlockMask` and `CHOK.bypass*` are derived from this baked data (§4.1).
> - **v1.2 new terrain section `WTRA`** (§4.7): art-only water marks (land / blocking water / fordable water / ford) and a ford list. It is not a sim input, is not part of `terrainPayloadSha256`, and has no GRID descriptor. v0 files do not have it.
> - `Town.sourceFlags` bit4: population from the US Census 2020 (P.L. 94-171) fallback; bit5: population is a manual **design override** (not sourced data, §4.5).
> - `RNOD.territoryId` (u16, §4.4): permanent node territory (ids pinned by the manifest, i.e. keyed to `TERR.stableKey`).
> - `wrmb_reader.py` reads and validates both formatVersion 0 and 1 files.

- **Two files per map** (each a complete WRMB container with its own header, META, directory, CRCs and content hash):
  - `<map>.v1.wrmb`: **main/AI file**, `META.fileKind` = 0. Sections: META, SRCS, GRID, TOWN, TDBG, TERR, RNOD, REDG, CHOK, RIVR, BRDG, WATR, WRNG, MSET, RSRC, CELL, CELX, pools.
  - `<map>.terrain.v1.wrmb`: **terrain file**, `META.fileKind` = 1. Sections: META, SRCS, GRID, MVSL, MVLC, MVFD, MVEL, WTRA (v1.2, art only, §4.7), pools (strings for META/SRCS).
  - **Pair binding:** both METAs carry `terrainPayloadSha256` = SHA-256 of the terrain sections' bytes concatenated in the order MVSL, MVLC, MVFD, MVEL. The terrain META carries `pairedContentSha256` = the main file's header `contentHash`. The main META leaves `pairedContentSha256` zero, because the terrain file points at the main file and not the other way round. Each META names the other file in `pairedFileName`. Build order: terrain payload → main file → terrain file.
  - Both METAs share one schema. In the terrain file the AI counts are 0. Map extent, grids, projection, seeds, versions, attribution, SRCS and hashes are identical in both files, and the validator checks this.
- **Not in v1 (as in v0):** the full-resolution 5400 × 5400 render heightmap (30 m). It will be a later, separate asset. The terrain file's MVEL (1800 × 1800, decimetres) is meant for tactical-map hillshade and AI use.
- **Schema:** Enemy AI design doc §3.1 schema v1 (`schemaVersion` = 1) plus the accepted contract changes in `ai-contract-review.md` §12: the bridge-head model, `areaCells`/`areaHa`, `seatTownId`, `exitEdgeIds`, `condition`, `climbCmAB/BA` + `elevDeltaCm`, travelCost in metre-equivalents, the `ChokepointRecord` layout from §2.2 (+ `bypassMaxGradePpt`, v1: + `bypassSurfaceClass`, `stableKey`), `CellExt`, `WaterBody`, pools and the provenance/attribution header.
- **Single source of truth in this repo:** `tools/map/wrmb_reader.py` (`SCHEMAS[formatVersion]`, `encode`, `decode`, `validate`, `validate_pair`). The map pipeline writer (`pipeline/s12_blob.py`) imports the same module; the tables below were generated from it by `pipeline/gen_layout_md.py`.
- **Reader/validator (standalone, Python stdlib only):**
  `python3 tools/map/wrmb_reader.py data/golden/golden-ci-columbia-gorge-100.v1.wrmb --terrain data/golden/golden-ci-columbia-gorge-100.terrain.v1.wrmb` (the same reader still validates the v0 pair)
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
| 12 | `flags` | u32 | bit0 `DRAFT_LAYOUT`, bit1 `HAS_DEBUG_SECTIONS` (TDBG, main file only) |
| 16 | `sectionCount` | u32 | number of directory entries |
| 20 | `directoryOffset` | u32 | 64 |
| 24 | `fileSize` | u64 | total bytes |
| 32 | `contentHash` | u8[32] | SHA-256 of the whole file with these 32 bytes zeroed |

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
| `CHOK` | main | ChokepointRecord (contract review §2.2 layout) + `bypassMaxGradePpt` (§4.1) + v1 `bypassSurfaceClass` (§4.3) + `stableKey` (§4.2) | derived |
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
- **No threshold is baked in:** the file does not store `wheeledOnly` or the 299‰ wheeled threshold. The AI derives "wheeled units cannot bypass" at load by comparing `bypassMaxGradePpt` with the designer's movement table.

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

**Id rule and manifest (`data/golden/stable-ids.json`, committed):** for each section the manifest maps key → id (plus OSM type/id, disc and a label). At build time a key found in the manifest keeps its manifest id; keys not in the manifest get the next free ids in the section's deterministic default order. The build **fails** if (a) the ids would not be dense 1..n (a manifest key vanished, which would force renumbering), (b) two records share a key, or (c) the post-build check `stable_ids.py --check <main.wrmb>` finds a manifest key whose id changed or that is missing (report: `blob/stable-ids-check.json`). Changing the manifest is a deliberate, reviewed step (`stable_ids.py --update <main.wrmb>`). The v1 manifest was seeded from the v0 golden file, so all 13 territory, 123 bridge and 49 chokepoint ids equal their v0 ids. Territory ids 1-13 stay: 1 Maupin, 2 Condon, 3 Wasco, 4 The Dalles, 5 Cascade Locks, 6 Hood River, 7 Arlington, 8 Carson, 9 White Salmon, 10 Roosevelt, 11 Goldendale, 12 Trout Lake, 13 Outlook. `BRDG` records are sorted by id (not edge order).

### 4.3 `CHOK.bypassSurfaceClass` (u8, v1)

The dominant land cover along the best bypass path of §4.1. Values use the land-cover enum (1 field, 2 scrub_rock, 3 urban, 4 forest, 5 marsh, 6 water, 7 snow_ice). The path is the one found by the minimax-bottleneck search (parent pointers; ties resolved by the same (maxGrade, cellIndex) key). Its cells, including both end cells, are counted by MVLC code; the most frequent code wins, and a tie goes to the lower code. **255** = no bypass; this holds exactly when `bypassMaxGradePpt` = 65535. The validator checks the value range and that consistency.

### 4.4 Territory partition (v1.2 movement-layer rule)

- **Seats:** chosen exactly as in v0 (soft-cost flood fill with seed spacing, oversize splits and small-territory merges). The golden map keeps the same 13 seats and ids.
- **Border rule:** a territory border can only be a terrain movement cell (1800 × 1800, about 89 m) that is impassable to **both** foot and wheeled units. In the baked data that is exactly `MVLC` code 6 (§4.6). Fords are carved passable, and slope never closes a cell to both unit types. Bridges connect territories; they never carry a territory across.
- **Cells:**
  - Each 1-mile cell is split into *pieces*: its movement cells grouped by 4-connected ground component. Thanks to §4.6 watertightness, 4-connected components equal the 8-connected ones the sim uses.
  - Pieces are grown from the seats by a multi-source Dijkstra with the v0 step costs. Pieces on a landmass with no seat (islands, sandbars; 51 pieces on the golden map) take the cheapest soft path from a labelled piece.
  - `CELX.territoryId` is the territory of the cell's largest piece (tie: lower id). An all-water cell takes the nearest labelled cell.
- **Features:**
  - Towns, road nodes and edge midpoints take the territory of the piece holding their movement cell, or of the nearest ground cell if theirs is water.
  - Bridgehead road nodes of bridges over blocking water take the piece of their S8b bridgehead cell (§4.6).
  - A dry edge whose two ends share a territory carries it.
  - `REDG.territoryId` / `RNOD.territoryId` can therefore differ from the containing cell's `CELX.territoryId` along banks.
- **`RNOD.territoryId` (u16, v1):** the node's territory under this rule. 0 = none, which never happens on the golden map. Ids are pinned by `data/golden/stable-ids.json` (§4.2).
- **CI (S9, build fails):**
  - **River check:** from the seat piece, every town, node and edge piece of a territory can be reached over ground pieces of that same territory. Pockets are exempt.
  - **Minimum size:** every territory has at least `territories.minTerritoryCells` = 150 cells.
  - **Columbia bridges:** every main-river bridge span links two different territories and two different ground components, and each head lies in the territory of its own bank. The same check runs on every bridge over blocking water whose heads are in different ground components.
  - **Passable-water carve-out** (front consistency): for land-adjacent pieces p in T and q in U, D_U(q) ≤ D_T(q) + w and D_T(p) ≤ D_U(p) + w. Here D is the unrestricted ground distance from each seat and w is the step weight. So no territory is cut off by water that units can cross.
  - **Demotion what-if:** every river in `blockingWater.demoteToFordable` is blocked again in a what-if run, and must then leave a territory below the minimum size.
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
- **Codes written to `MVLC`:** 6 for blocking water (golden map: 37 737 cells); 5 (marsh) for fordable water, that is WorldCover water outside the blocking set plus carved ford cells; all other cells keep their WorldCover code.
- **CI (build fails):**
  - 0 diagonal leaks, and 4- and 8-connected component counts are equal;
  - no ford or bridgehead cell is blocked;
  - **no foot or wheeled route crosses a blocking river away from its bridges and fords:** samples every 2 km along each river, 8-way search within a 3 km window;
  - every bridgehead is reachable from its own bank.

**Licence separation:** `MVLC` mixes ESA WorldCover (CC BY 4.0) with OSM-derived cells (ODbL). The OSM-derived cells are exactly those where `movement/landcover.u8` differs from the WorldCover-only `movement/landcover_worldcover.u8` (listed by `movement/osm_water_mask.u8`; golden map: 5440 cells). `MVFD` and `WTRA` are OSM-derived. This mirrors the ford layer separation.

### 4.7 `WTRA`: art-only water marks (terrain file, v1.2)

- **Tag and purpose:** a new optional terrain section, tag **`WTRA`** ("water art"), placed after `MVEL`. It gives artists and the tactical map the water type of every movement cell, plus a list of fords.
- **Not sim or path data:** the sim skips unknown tags, and `WTRA` is **not** in `terrainPayloadSha256` and has no GRID descriptor (the section describes itself). Adding or changing it cannot change the sim's movement data or any path hash. No spare bits of the movement sections are used.
- **Encoding** (little-endian; `elementSize` 1, `count` = byte length):
  - Header: `u32 version` (= 1), `u32 width`, `u32 height` (= the META move grid, 1800 × 1800), `u32 fordCount`.
  - Water marks: `u8 code[width × height]`, row-major, row 0 = south, same cell order as `MVLC`. Codes: **0** land, **1** blocking water, **2** fordable water, **3** ford.
  - Zero padding to an 8-byte boundary (0 bytes at 1800 × 1800).
  - Ford list: `fordCount` × 16 B records, sorted by `cellIndex`: `u32 cellIndex`, `u8 osmType` (0 none, 1 node, 2 way, 3 relation), 3 pad bytes, `u64 osmId`. Each record is one ford cell, carrying the lowest (type, id) of the OSM ford features in that cell.
  - Golden map: 954 fords, 3 255 280 B.
- **Validator** (`wrmb_reader.py`; the build fails on any error; `water_art(model)` exposes the decoded section):
  - version, dims and length;
  - every code ≤ 3;
  - **code 1 ⇔ `MVLC` = 6** (blocking water equals exactly the cells pathing treats as impassable water);
  - code 3 ⇔ `MVFD` bit set;
  - no fordable water on code-6 cells;
  - the ford list is strictly sorted and equals the `MVFD` cells, and an `osmId` is present whenever `osmType` ≠ 0;
  - with `--movement-dir`: the codes equal `movement/water_type.u8`, and the ford list equals `water_bake.json` `fordList`.

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
| 7 | `pad` | zero padding |  |
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
| 79 | `pad2` | zero padding |  |
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

## 5. Validator checks (`wrmb_reader.py`)

1. **Header and directory (each file):** magic, `formatVersion` ∈ {0, 1} (selects the schema), header size, `fileSize`, `contentHash`, 8-byte alignment, bounds, per-section CRC-32, `count × elementSize = byteLength`, `elementSize` equal to the schema.
2. **Pool spans:** every span lies inside its pool.
3. **Round trips:** `encode(decode(file)) == file`. With the debug exports: the model equals the JSON, and JSON → binary is byte-identical. With `--movement-dir`: MVSL/MVLC/MVFD/MVEL (and v1.2 WTRA) equal the pipeline's source arrays.
4. **GRID:** every descriptor points at a present section, with origin (0, 0), extent = map, row order south-first, and cell size an exact reduced rational (`num × width = extent × den`). Byte length matches width × height × elemBytes (bitset: ceil(bits / 8)), and dims match META.
5. **Per-fileKind section sets:**
   - Main: the AI sections plus a GRID for CELL/CELX, no terrain sections, `pairedContentSha256` zero.
   - Terrain: MVSL/MVLC/MVFD/MVEL with GRID descriptors, AI counts 0, MVLC codes ≤ 7, `terrainPayloadSha256` recomputed, MVEL offset/scale = META. The optional art-only `WTRA` (v1.2) is validated as in §4.7, including blocking marks = `MVLC` code 6. It is not in the payload hash.
6. **Main invariants:**
   - META counts match the sections, and the extent equals `grid × cellSizeCm`.
   - Dense sorted ids.
   - Edges have `a < b` and are sorted by (a, b).
   - Every reference is valid, id lists are sorted and unique, and neighbour lists are symmetric.
   - One connected passable road graph, and every town gate reachable.
   - Bridge-head model: each span edge has two BridgeHead ends, each BridgeHead touches a span edge, and BRDG equals the set of span edges.
   - Territories: each seat is in its own town list, each town is in exactly its own territory, and `areaCells` equals the painted CELX cells.
   - `edgeBlockMask` is symmetric.
   - Value ranges hold (incl. `bypassMaxGradePpt` ≤ 65534 or = 65535).
   - v1 only: `stableKey` non-zero, unique per section, valid OSM type (TERR node, BRDG way), disc 0 for TERR/BRDG and `kind + 1` for CHOK; `bypassSurfaceClass` ≤ 7 or 255, and 255 exactly when `bypassMaxGradePpt` = 65535. `RNOD.territoryId` is 0 or an existing TERR id. A dry edge (flags without bridge/span/crossesBlockingWater) whose two end nodes share a non-zero territory carries it. A town's gate node lies in the town's territory, unless the gate is shared by towns of several territories. Population-source bits are exclusive, and bit5 requires population > 0.
   - `attributionSha256` matches the attribution text.
7. **Pair (`--terrain`):**
   - The file kinds are (main, terrain).
   - The terrain `pairedContentSha256` equals the main `contentHash`.
   - `terrainPayloadSha256` is equal in both files and matches the payload.
   - `pairedFileName` matches the other file's actual name, in both directions.
   - META map/grid/projection/version/hash fields are identical, and SRCS are identical.
   - Every non-65535 `bypassMaxGradePpt` is ≤ max(MVSL).

## 6. Open points

- **Record layouts:** v1 still sets `DRAFT_LAYOUT`; records may change again before they are frozen. Renaming or re-ordering sections is free for readers that use the directory, and adding fields bumps `elementSize`.
- **Render heightmap:** the full-resolution 5400 × 5400 heightmap is a later separate asset (format TBD: likely a third fileKind or a plain 16-bit raster with the same GRID descriptor conventions).
- **Terrain file size:** ~16.6 MB uncompressed (+3.3 MB for v1.2 `WTRA`). Slope u16 is 6.5 MB and elevation u16 is 6.5 MB. Container-level compression or u8 slope quantization remain options.
- **Missing fields:** the gameplay fields (`supplyCapacity`, etc.) are left out, per contract change 4. They are resolved at load from the designer table keyed by `populationClass`.
- **`TerritoryBorder`** node flag and `RoadEdge.territoryId` exist, but edges are not yet split at territory crossings. `territoryId` is the territory of the edge midpoint's ground piece (§4.4).
- **String encoding:** the pool has no dedup. Names are debug/UI only.
- **Stable-key manifest check** in this repo is `tools/map/stable_ids.py` (Golden map CI). The stand-alone `wrmb_reader.py` checks key form/uniqueness only, not the manifest. The map pipeline also runs the check from `pipeline/build.py`.
