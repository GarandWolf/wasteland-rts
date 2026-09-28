# golden-ci-columbia-gorge-100 — golden map data

Golden CI map for the RTS map-data pipeline: Columbia River Gorge, 100 × 100 miles (16,093,400 cm square). Two format
versions live side by side. The v0 pair and its sidecar hashes are unchanged. The sim loader still reads v0;
format 1 is a separate sim PR.

## v0 pair (formatVersion 0, pipeline `0.3.0-golden-v0-draft`)

Two binary files form one map (WRMB container, `DRAFT_LAYOUT` flag set; the container and section directory are approved
by the Lead Engineer, record layouts may still change before they are frozen):

| File | Bytes | SHA-256 | Contents |
|---|---:|---|---|
| `golden-ci-columbia-gorge-100.v0.wrmb` | 602648 | `a36b1954ee048637073fc44f5fec9913105ff7d6ffd0c54d4f93bedca2371757` | main/AI file (META.fileKind 0): towns, territories, road graph, chokepoints (incl. `bypassMaxGradePpt`), rivers, bridges, water, minor settlements, resources, 1-mile cells |
| `golden-ci-columbia-gorge-100.terrain.v0.wrmb` | 16614424 | `dbe9c1843744450b2014751492ef5a53afe9e689b987ed4713185f2307ce9f54` | terrain file (fileKind 1): 1800 × 1800 movement slope (u16 ppt), land cover (u8), OSM ford bitset, elevation (u16 dm, for hillshade on the tactical paper map) |

Layout: [`docs/map/wrmb-layout-v0.md`](../../docs/map/wrmb-layout-v0.md).

## v1 pair (formatVersion 1 FINAL, pipeline `0.6.0-golden-v1.3`)

Same map, formatVersion 1. `DRAFT_LAYOUT` is cleared and header flag bit2 `V1_FINAL` is required. `WTRA` is required in the terrain file. Any later record change needs formatVersion 2.

| File | Bytes | SHA-256 | Contents |
|---|---:|---|---|
| `golden-ci-columbia-gorge-100.v1.wrmb` | 606784 | `30a1c6705459ed7060405286f6a5ac1f3820d47a8a2b60bbbf56cbc31b6aebe2` | main/AI file (META.fileKind 0): towns, territories, road graph, chokepoints (incl. `bypassMaxGradePpt`, `bypassSurfaceClass`, `defenseGroupId` at offset 7, `bypassWheeledBlocked` at offset 79; record 88 B), rivers, bridges, water, minor settlements, resources, 1-mile cells; stable u64 keys on territories, bridges, chokepoints |
| `golden-ci-columbia-gorge-100.terrain.v1.wrmb` | 19872384 | `b6636e6a754b738a4a867b00c03b87a524652ce4ff175339b609003c87266419` | terrain file (fileKind 1): 1800 × 1800 movement slope (u16 ppt), land cover (u8; v1.2: code 6 = baked blocking rivers/lakes, impassable, watertight; 5 = fordable water), OSM ford bitset, elevation (u16 dm); required `WTRA`: art-only water marks (0 land, 1 blocking, 2 fordable water, 3 ford) + ford list (cell index, OSM id), not a sim/path input and not in `terrainPayloadSha256` |

Also in this folder:

| File | Role |
|---|---|
| `stable-ids.json` | pinned TERR / BRDG / CHOK id ↔ u64 `stableKey` (seeded from v0, so every v1 id equals its v0 id) plus `defenseGroups` (16 groups) |
| `territory-roads.v1.json` | territory neighbour lists + 13 × 13 seat-to-seat shortest road distance (cm, length only), from the v1 main file |

Layout: [`docs/map/wrmb-layout-v1.md`](../../docs/map/wrmb-layout-v1.md).

## What changed in v1

- **Blocking water in the movement data (v1.2).** Blocking rivers are chosen by a measurable rule with no river names.
  Blocking rivers and counted lakes are baked into the terrain `MVLC` as code 6, watertight against 8-way movement.
  Bridges and fords are the only crossings.
- **Territories follow the movement layer (v1.2).** A border can only be a movement cell impassable to foot and wheeled
  units (`MVLC` 6). Chokepoint selection and all ids are unchanged.
- **Bypass from baked data (v1.2).** `CHOK.bypassMaxGradePpt` / `bypassSurfaceClass` read only the baked terrain data.
  65535 / 255 means no bypass.
- **Stable ids.** `stableKey` (u64 from OSM ids) on TERR / BRDG / CHOK; ids are pinned by `stable-ids.json`.
- **`CHOK.bypassSurfaceClass`:** dominant land cover along the best bypass path; 255 = no bypass.
- **`CHOK.defenseGroupId`:** u8 at offset 7; 255 = internal chokepoint. Border chokepoints that share a side pair and a 20 km road approach form groups (golden map: 16).
- **`CHOK.bypassWheeledBlocked`:** u8 at offset 79; 1 if no wheeled bypass exists.
- **`RNOD.territoryId` (u16):** permanent per-node territory; 0 is invalid in final v1.
- **`WTRA`:** required in every v1 terrain file; art-only. The sim must skip unknown tags. It is not part of `terrainPayloadSha256`.
- **`V1_FINAL`:** a formatVersion 1 file without header flag bit2 is rejected as stale.
- **Populations:** towns without a GeoNames/OSM population use the US Census 2020 P.L. 94-171 count (`Town.sourceFlags` bit4).
- **Design override (NOT sourced data):** Welches population = 250 (`Town.sourceFlags` bit5).

Both `.wrmb` files (v0 and v1) are Git LFS objects (`*.wrmb` in `.gitattributes`). Sidecar `.sha256` files stay as ordinary
text so CI can check the materialized bytes.

Each pair is bound by `META.terrainPayloadSha256` (both files) and the terrain file's `META.pairedContentSha256` (= the
main file's header contentHash) plus `pairedFileName`. The full-resolution 5400 × 5400 render heightmap is **not** part of
either pair; it will be a later separate asset.

Coordinates: absolute integer cm, origin at the SW corner; sim x east → Unity +X, sim y north → Unity +Z, elevation →
Unity +Y. Grid index = floor(coordCm × gridWidth / mapWidthCm) (cell sizes are exact rationals in the GRID section:
1-mile grid 160934/1 cm, terrain grid 80467/9 cm).

`dotnet test` loads the **v0** pair through `MovementService.Create` (`src/Wasteland.Sim.Tests/GoldenMapTests.cs`) and pins a
few road/off-road path hashes. Those pins are not changed by this v1 drop. The Unity client does not load either pair yet.
Synthetic WRMB fixtures remain for the small hill goldens.

## Sources (pinned in raw/inputs.lock.json)

- OpenStreetMap: Geofabrik Oregon + Washington extracts, replication timestamp 2026-09-27T20:23:36Z (sequence 4924). ODbL 1.0. All roads come from OSM only.
- Copernicus DEM GLO-30 (9 1-degree COG tiles, AWS public release, Last-Modified May 2022; EGM2008). Copernicus WorldDEM-30 licence.
- ESA WorldCover 10 m 2021 v200 (2 tiles, DOI 10.5281/zenodo.7254221). CC BY 4.0.
- GeoNames US.zip (Last-Modified Mon, 28 Sep 2026 01:52:21 GMT). CC BY 4.0.
- US Census Bureau 2020 Census Redistricting Data (P.L. 94-171), Oregon + Washington bulk files (or2020.pl.zip, wa2020.pl.zip; Last-Modified Thu, 12 Aug 2021 17:20:03 GMT): v1 population fallback only. Public domain.
- USGS 3DEP 1 arc-second: difference check only, not shipped (public domain).

## Validate

Python 3 stdlib only. The reader handles formatVersion 0 and 1. From the repository root:

```bash
(cd data/golden && sha256sum -c golden-ci-columbia-gorge-100.v0.wrmb.sha256 --strict \
  && sha256sum -c golden-ci-columbia-gorge-100.terrain.v0.wrmb.sha256 --strict \
  && sha256sum -c golden-ci-columbia-gorge-100.v1.wrmb.sha256 --strict \
  && sha256sum -c golden-ci-columbia-gorge-100.terrain.v1.wrmb.sha256 --strict)
python3 tools/map/wrmb_reader.py \
  data/golden/golden-ci-columbia-gorge-100.v0.wrmb \
  --terrain data/golden/golden-ci-columbia-gorge-100.terrain.v0.wrmb
python3 tools/map/wrmb_reader.py \
  data/golden/golden-ci-columbia-gorge-100.v1.wrmb \
  --terrain data/golden/golden-ci-columbia-gorge-100.terrain.v1.wrmb
python3 tools/map/stable_ids.py \
  --check data/golden/golden-ci-columbia-gorge-100.v1.wrmb \
  --against data/golden/golden-ci-columbia-gorge-100.v0.wrmb \
  --manifest data/golden/stable-ids.json
python3 tools/map/tests/test_reader_final_v1.py \
  data/golden/golden-ci-columbia-gorge-100.v1.wrmb \
  data/golden/golden-ci-columbia-gorge-100.terrain.v1.wrmb
```

The sidecar `.sha256` files name the blobs without a directory prefix, so `sha256sum -c` has to run with `data/golden/` as the working directory. `git lfs install` (once per machine) is required before the `.wrmb` files materialize; otherwise you are hashing pointer files.

Checks header/magic/versions, content hash, directory alignment and per-section CRC32, GRID descriptors, per-file invariants
(dense ids, references, connected road graph, bridge-head model, territories, value ranges, grid sizes/codes; v1 also
stable-key form and bypassSurfaceClass consistency; v1.2 `WTRA`: blocking marks exactly equal `MVLC` code 6, ford marks
and list equal `MVFD`), the pair binding, and a byte-identical encode(decode(file)) round trip. Exit code 0 and
`"result": "PASS"` = valid. The validator fails the build on any error. `stable_ids.py` additionally requires every
TERR / BRDG / CHOK `(stableKey, id)` in the v1 blob to match `stable-ids.json` (including `defenseGroups`), and the v0 ids to equal the v1 ids. `tools/map/tests/test_reader_final_v1.py` proves stale v1, missing WTRA, bad `elementSize`, 254-group overflow, and `territoryId` 0 all fail.

CI runs the same checks in `.github/workflows/golden-map.yml` after `git lfs pull --include="data/golden/*.wrmb"`.

## Licence and attribution

Map data (c) OpenStreetMap contributors, available under the ODbL (openstreetmap.org/copyright)
Elevation: produced using Copernicus WorldDEM-30 (c) DLR e.V. 2010-2014 and (c) Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved. Modified product: resampled/reprojected to the map grid, integer cm, derived slope/hillshade.
The organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of the Copernicus WorldDEM-30.
Elevation difference check only (not shipped): U.S. Geological Survey 3D Elevation Program (public domain)
(c) ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover consortium (CC BY 4.0)
Terrain movement data: MVLC land cover = ESA WorldCover classes with OSM-derived blocking rivers/lakes baked in (the OSM/ODbL part is exactly the cells that differ from the WorldCover-only mode); the ford bitset (MVFD) and the art water marks + ford list (WTRA) are OSM-derived (ODbL)
Population: GeoNames (geonames.org), CC BY 4.0
Population (fallback where GeoNames has none): U.S. Census Bureau, 2020 Census Redistricting Data (P.L. 94-171) (public domain)

The OSM-derived content (roads, bridges, fords, places/towns, minor settlements, resources, water, the blocking-water
cells baked into MVLC, WTRA, and everything derived from the road graph) is a Derivative Database under the ODbL 1.0; the OSM source clip is archived (see OSM-EXTRACT.txt).
Full notice: ATTRIBUTION.txt.
