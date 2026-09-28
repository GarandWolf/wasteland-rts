# golden-ci-columbia-gorge-100 — golden map data v0

Golden CI map for the RTS map-data pipeline: Columbia River Gorge, 100 × 100 miles (16,093,400 cm square), pipeline
`0.3.0-golden-v0-draft`. Two binary files form one map (WRMB container, formatVersion 0, `DRAFT_LAYOUT` flag set; the
container and section directory are approved by the Lead Engineer, record layouts may still change before v1):

| File | Bytes | SHA-256 | Contents |
|---|---:|---|---|
| `golden-ci-columbia-gorge-100.v0.wrmb` | 602648 | `a36b1954ee048637073fc44f5fec9913105ff7d6ffd0c54d4f93bedca2371757` | main/AI file (META.fileKind 0): towns, territories, road graph, chokepoints (incl. `bypassMaxGradePpt`), rivers, bridges, water, minor settlements, resources, 1-mile cells |
| `golden-ci-columbia-gorge-100.terrain.v0.wrmb` | 16614424 | `dbe9c1843744450b2014751492ef5a53afe9e689b987ed4713185f2307ce9f54` | terrain file (fileKind 1): 1800 × 1800 movement slope (u16 ppt), land cover (u8), OSM ford bitset, elevation (u16 dm, for hillshade on the tactical paper map) |

Both `.wrmb` files are Git LFS objects (`*.wrmb` in `.gitattributes`). Sidecar `.sha256` files stay as ordinary text so CI can check the materialized bytes.

The pair is bound by `META.terrainPayloadSha256` (both files) and the terrain file's `META.pairedContentSha256` (= the
main file's header contentHash) plus `pairedFileName`. The full-resolution 5400 × 5400 render heightmap is **not** part of v0;
it will be a later separate asset.

Coordinates: absolute integer cm, origin at the SW corner; sim x east → Unity +X, sim y north → Unity +Z, elevation →
Unity +Y. Grid index = floor(coordCm × gridWidth / mapWidthCm) (cell sizes are exact rationals in the GRID section:
1-mile grid 160934/1 cm, terrain grid 80467/9 cm). Layout: [`docs/map/wrmb-layout-v0.md`](../../docs/map/wrmb-layout-v0.md).

`dotnet test` loads this pair through `MovementService.Create` (`src/Wasteland.Sim.Tests/GoldenMapTests.cs`) and pins a
few road/off-road path hashes. The Unity client does not load it yet. Synthetic WRMB fixtures remain for the small hill
goldens.

## Sources (pinned in raw/inputs.lock.json)

- OpenStreetMap: Geofabrik Oregon + Washington extracts, replication timestamp 2026-09-27T20:23:36Z (sequence 4924). ODbL 1.0.
- Copernicus DEM GLO-30 (9 1-degree COG tiles, AWS public release, Last-Modified May 2022; EGM2008). Copernicus WorldDEM-30 licence.
- ESA WorldCover 10 m 2021 v200 (2 tiles, DOI 10.5281/zenodo.7254221). CC BY 4.0.
- GeoNames US.zip (Last-Modified Mon, 28 Sep 2026 01:52:21 GMT). CC BY 4.0.
- USGS 3DEP 1 arc-second: difference check only, not shipped (public domain).

## Validate

Python 3 stdlib only. From the repository root:

```bash
(cd data/golden && sha256sum -c golden-ci-columbia-gorge-100.v0.wrmb.sha256 --strict \
  && sha256sum -c golden-ci-columbia-gorge-100.terrain.v0.wrmb.sha256 --strict)
python3 tools/map/wrmb_reader.py \
  data/golden/golden-ci-columbia-gorge-100.v0.wrmb \
  --terrain data/golden/golden-ci-columbia-gorge-100.terrain.v0.wrmb
```

The sidecar `.sha256` files name the blobs without a directory prefix, so `sha256sum -c` has to run with `data/golden/` as the working directory. `git lfs install` (once per machine) is required before the `.wrmb` files materialize; otherwise you are hashing pointer files.

Checks header/magic/versions, content hash, directory alignment and per-section CRC32, GRID descriptors, per-file invariants
(dense ids, references, connected road graph, bridge-head model, territories, value ranges, grid sizes/codes), the pair
binding, and a byte-identical encode(decode(file)) round trip. Exit code 0 and `"result": "PASS"` = valid. The pipeline also
checks the files against its JSON debug exports and source arrays (`--debug-json`, `--terrain-debug-json`, `--movement-dir`).

CI runs the same two checks in `.github/workflows/golden-map.yml` after `git lfs pull --include="data/golden/*.wrmb"`.

## Licence and attribution

Map data (c) OpenStreetMap contributors, available under the ODbL (openstreetmap.org/copyright)
Elevation: produced using Copernicus WorldDEM-30 (c) DLR e.V. 2010-2014 and (c) Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved. Modified product: resampled/reprojected to the map grid, integer cm, derived slope/hillshade.
The organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of the Copernicus WorldDEM-30.
Elevation difference check only (not shipped): U.S. Geological Survey 3D Elevation Program (public domain)
(c) ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover consortium (CC BY 4.0)
Population: GeoNames (geonames.org), CC BY 4.0

The OSM-derived content (roads, bridges, fords, places/towns, minor settlements, resources, water and everything derived
from the road graph) is a Derivative Database under the ODbL 1.0; the OSM source clip is archived (see OSM-EXTRACT.txt).
Full notice: ATTRIBUTION.txt.
