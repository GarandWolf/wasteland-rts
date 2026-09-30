# Balance Notes: First Playable v0.6

**Status: every number in this folder is a starting value to tune** (first playable, data version 6). Nothing is final until playtests and headless AI-vs-AI runs. The JSON files carry no free text, so all explanations live here.

Scope: Remnant Command (player, HQ at Goldendale) vs Homestead Compact (AI, HQ at Hood River) on the real Columbia Gorge 100-mile bake (Gorge golden map **v1**, corrected build: pipeline `0.5.0-golden-v1.2`).

## v0.7 draft: per-map scenario file (no balance change)

**Status: DRAFT, in PR #13 awaiting review and merge approval.** `rules.json` is now map-agnostic. The `scenario` block moved to `scenarios/columbia_gorge_100mi_test.json`, so a second map is a second scenario file and needs no edit to `rules.json`.

- **File layout.** `schemaVersion` 1, `dataVersion`, `id` (must equal the file name), `mapFile`, `mapFormatVersion`, `mapFileSha256` (SHA-256 of the marsh main `.wrmb` file bytes, `76a801a3…107f`); `seats[]`; `matchRules`; `derived`.
- **Seats.** `slot`, `faction`, `aiPersonality` (`null` for the human seat), `homeTerritoryKey`, `startingManpower`, `startingBuildings`, `startingUnits`, `startingHoldings`. Slot 0 is the human seat (Remnant Command, Goldendale), slot 1 is the AI seat (Homestead Compact, Hood River, `homestead_compact_settler`). This is exactly who was player and who was AI in the v0.6.1 `scenario.players`. The Dalles has no seat: it is neutral because no seat starts holding it.
- **Territory references are the map's u64 `TERR.stableKey`** everywhere in the file (`homeTerritoryKey`, `startingHoldings[].territoryKey`, `matchRules.neutralPrizeTerritoryKey`, `derived.territories[].mapStableKey`, `populationOverrides[].territoryKey`, `censusFallbackPlaces[].territoryKey`). Hood River `0x1000000008FFA335`, Goldendale `0x1000000008FF3E87`, The Dalles `0x10000000B3787512`, Maupin `0x1000000008FF8922`. The snake_case `id` stays only as a label inside `derived.territories`, and the tools still print it.
- **Match rules.** `allowedFactionCount` 2, `allowedAiSeats` 1, `victory: capture_enemy_hq_territory`, `territoryOwnershipRule: hold_seat`.
- **Derived counts** (`derived`) are the old scenario numbers, unchanged: holding counts, capturable / scenery / override places, resource-site counts, and the per-territory places and sites. `tools/validate.py` (designer tooling, not yet in this repo) recomputes them from the map.
- **Key format (Lead Engineer request).** Every u64 territory key is a fixed-width hex **string**: `"0x"` + 16 upper-case hex digits (regex `^0x[0-9A-F]{16}$`), never a JSON number, because readers that parse JSON numbers as doubles corrupt anything above 2^53 (all 13 Gorge keys are about 1.15e18). Compare keys **by integer value** (parse the hex to u64), never by string, and write them back with the same width and case. The validator rejects JSON numbers, wrong width, missing `0x`, lower-case digits and a `0X` prefix, and compares against the map's `TERR.stableKey` set as integers. `tools/scenario.py` (designer tooling, not yet in this repo) converts to Python ints and rebuilds the old dict, so `econ.py`, `roads.py` and `validate.py` still see ints and snake_case ids.
- **Which hash is `mapFileSha256`.** It is the **SHA-256 of the map's main `.wrmb` file bytes** (the value in the delivery's `.sha256` sidecar, 64 lower-case hex digits). It is **not** the map header's own `contentHash` field (`d99f3de9…af5aa` for this map), which is computed inside the format and is a different value. The field was renamed from `mapContentHash` to make that unambiguous. The validator fails if the header `contentHash` is used, or if the name is the old one.
- **`matchRules.neutralPrizeTerritoryKey` (optional).** Set to The Dalles (`0x10000000B3787512`). The validator checks it is a scenario territory, is held at start by no seat, is not a seat's home, and that its map seat town matches its label. `econ.py` reads it through `tools/scenario.py` (designer tooling, not yet in this repo) and falls back to `the_dalles` if the field is absent (the v0.6 behaviour); output is identical either way.
- **`dataVersion` is now 7 in every balance JSON** (units, buildings, economy, `ai_personalities.json` (designer tooling, not yet in this repo), rules, scenario file). No balance value changed, so the tools print the same numbers. **The `BalanceKey` changes with this bump** (the key the engine and AI derive from the balance data version), and **readers must refuse any `dataVersion` < 7**: a v6 layout has the `scenario` block inside `rules.json` and integer territory keys, and cannot be read with the v7 layout or the reverse. The validator enforces `dataVersion >= 7` and equality across all files.
- **Difficulty is not in the file.** It comes from the player's pick (`ai_difficulty.json` on the AI side). The validator fails if a `difficulty` key appears.
- **`startingManpower` is copied from `economy.json → startingResources.manpower`** (300 for both seats; no per-faction figure exists). The validator keeps the two equal. Starting fuel and materiel stay only in `economy.json`.
- **`tools/scenario.py` (new; designer tooling, not yet in this repo)** loads the file and rebuilds the old `scenario` dict (territory keys resolved back to ids), so `econ.py`, `roads.py` and `validate.py` read the same values as before. `sim.py`, `race.py` and `supply.py` are untouched.
- **New validator checks.** No `scenario` key in `rules.json`; exact top-level, seat and `matchRules` key sets; `id` equals the file name; slots 0..N-1; one seat per faction; `allowedAiSeats` equals the seats with a personality; faction, personality (same faction, `activeInFirstPlayable`), unit (same faction) and building ids exist; each home key is a scenario territory and is held at start; no territory is held by two seats; `startingManpower` matches `economy.json`; `mapFileSha256` equals the SHA-256 of the map named by the road export and its `.sha256` sidecar; `mapFile` matches the map file name; the scenario's territory keys equal the map's `TERR.stableKey` set; each home key's seat town matches its label; the old derived-count checks all still run; the hash and the territory keys are the only strings exempt from the snake_case rule (see the key format below); the scenario `dataVersion` equals `rules.json`.
- **Numbers.** `validate.py` (designer tooling, not yet in this repo) passes. `sim.py` (44 lines, equals `golden_expected.txt`), `econ.py` (Normal and Brutal, plain, `--remnant-commit`, `--condon`, `--remnant-commit --condon`, `--hq-sweep`), `race.py`, `roads.py` and `supply.py` output is byte-identical to v0.6.1: The Dalles race (Compact captures at 8:30 Normal), unit timings, manpower and pop-cap curves, and the Condon relay (node 368).

## v0.6.1 hash re-pin (no data change)

- **Final Gorge v1 map pair** (PR #6 of GarandWolf/wasteland-rts, merge commit `95c1cf9`, pipeline `0.6.0-golden-v1.3`, formatVersion 1 FINAL): `golden-ci-columbia-gorge-100.v1.wrmb` (606,784 B, SHA-256 `8474c3f4…48164`, replaces `a5fef7f5…67fe6`) and `golden-ci-columbia-gorge-100.terrain.v1.wrmb` (19,872,384 B, `8feb24e8…183d1`, replaces `688187c4…f626`). `territory-roads.v1.json` is unchanged (`97817328…2d983`). The files were fetched from the repo's LFS objects at that commit and match the files in `rts-map/gorge/deliver/` byte for byte.
- **Only change:** the main-file pin in `tools/validate.py`. The readers needed no change (CHOK stays 88 B, `defenseGroupId` u8 @7, `bypassWheeledBlocked` u8 @79, header flag bit2 `V1_FINAL` set, `WTRA` present in the terrain file).
- `validate.py` passes, and `sim.py --check`, `race.py`/`econ.py` (Normal, Brutal, `--remnant-commit`, `--condon`) and `supply.py` reproduce every v0.6 number exactly. `dataVersion` stays 6. No balance JSON changed.

## Changes in v0.6 (from v0.5)

- **Corrected Gorge v1 map.** The Map Pipeline re-delivered the v1 files in `rts-map/gorge/deliver/` under the same names (pipeline `0.5.0-golden-v1.2`): `golden-ci-columbia-gorge-100.v1.wrmb` (SHA-256 `a5fef7f5…67fe6`), `golden-ci-columbia-gorge-100.terrain.v1.wrmb` (`688187c4…f626`) and `territory-roads.v1.json` (`97817328…2d983`). All three hashes were checked, and the delivered `wrmb_reader.py` passes both files (`"result": "PASS"`). `tools/validate.py` now pins the main file and the road export by SHA-256, so a later re-delivery fails validation until the tools are re-run on it.
- **Node territory now comes from the map file.** v1.2 `RNOD` records carry `territoryId` (the permanent per-node territory under the movement-layer rule), which maps to `TERR.stableKey` through the pinned territory id. `tools/supply.py` reads it directly and no longer uses the `build/runA/territories/territories.json` workaround. The loader checks that every node's territory is a real `TERR` id, that every `REDG` edge's territory matches one of its end nodes, and that every town sits in its road node's territory. All three checks pass. `tools/validate.py` fails if a formatVersion ≥ 1 map has no `RNOD.territoryId`. This closes open item 12 from v0.5.
- **Road distances: reloaded, unchanged.** All 169 seat-to-seat distances in the new `territory-roads.v1.json` match v0.5 (and v0) to the centimetre. Every travel time in `timings.md` stands.
- **Neighbour lists changed** (borders now follow blocking rivers: the Columbia, Deschutes, John Day and Klickitat block, while the Yakima, White and White Salmon are fordable):
  - **Goldendale (#11) and Hood River (#6) are no longer adjacent.** The two start territories do not share a border any more. Every border contact between the homes now runs through The Dalles (#4) or White Salmon (#9), and both of those border both homes.
  - **The Dalles (#4) and White Salmon (#9) are now adjacent**.
  - **Cascade Locks (#5) and White Salmon (#9) are no longer adjacent.**
  - No balance tool or rule uses adjacency for movement, capture or supply, so no curve moves. The only effect is on which neighbours the territory-level AI can list (section 7f).
- **Places: Klickitat moved from Trout Lake to White Salmon.** It is a hamlet (population 362) on the White Salmon side of the blocking Klickitat River. One industrial area moves with it. White Salmon is now 1 hamlet / 2 villages, with 3 industrial areas (was 0/2, 2). Trout Lake is now 1 hamlet / 1 village, with 3 industrial areas (was 2/1, 4). No other town or resource site changed territory. The totals are unchanged: 34 capturable places (19 hamlets, 13 villages, 2 towns), 2 scenery places, 65 fuel stations, 8 depots and 129 industrial areas.
- **Welches is now baked into the map as a design override** (population 250, `Town.sourceFlags` bit5; the map README marks it as a design override, not sourced data). `rules.json → scenario.populationOverrides` keeps the record. The validator accepts either the old form (map population 0) or the baked form (bit5 set, no Census bit4, population equal to the override). It still counts `capturablePlacesFromMap` from sourced populations only (33 + 1 override = 34), and it fails if the map carries a bit5 override that `rules.json` does not list. Lyle stays a hamlet (499) by the tier rule.
- **Starting holdings: unchanged.** The Compact holds all of Hood River (#6): 4 hamlets (Government Camp, Rhododendron, Welches, Mosier) and 1 town (Hood River). The Remnant holds all of Goldendale (#11): 3 hamlets (Wishram, Maryhill, Lyle) and 2 villages (Dallesport, Goldendale). Both territories have the same places as in v0.5. Hood River already included Government Camp and Mosier (since v0.4), and Rhododendron and Welches (since v0.5). The validator now also checks that each start equals its whole territory.
- **Curves: every opening number is identical to v0.5**, on Normal and Brutal, with and without `--remnant-commit`, plus `--condon` and `--hq-sweep` (the output was diffed line by line against the v0.5 tools). Condon relay: node 368 (OR-206, Wasco) is still the legal site. Hood River is still outside Remnant HQ supply. See sections 7b to 7f.
- **No unit, building, economy or combat value changed.** The only data edits are the White Salmon and Trout Lake place and site counts, plus `dataVersion` 6 in every file. `sim.py` is untouched, and the golden file still matches (44 lines).

## Changes in v0.5 (from v0.4)

- **Map v1.** `tools/roads.py` now reads `rts-map/gorge/deliver/territory-roads.v1.json`, and `tools/supply.py` and `tools/validate.py` open the file it names, `golden-ci-columbia-gorge-100.v1.wrmb` (formatVersion 1, read with the delivered `wrmb_reader.py`, which reads v0 and v1). The v1 road export has the same distances and neighbour lists as v0. Only `source` and the new `stableKey` fields differ, so **every road distance and travel time is unchanged**.
- **Permanent keys.** Every `rules.json` territory now has `mapStableKey`, which equals the map's `TERR.stableKey` (derived from the seat's OSM node). `roads.py` matches export territories by that key and cross-checks `bakeIndex` and the seat name. `supply.py` reports bridges and chokepoints by their v1 `stableKey`. No balance file keyed anything by bridge or chokepoint before, so nothing else needed re-keying. `CHOK.bypassSurfaceClass` is new in the map, but no balance tool uses it yet.
- **Territory borders follow the rivers.** On the v1 map, 41 road edges and 43 road nodes moved territory under the river-bank rule. The v1 `RNOD` record has **no territory field**, so `supply.py` reads the bank-aware node territories from the pipeline build that produced the delivered file (`rts-map/gorge/build/runA/territories/territories.json`). It uses them only if that build's main file has the delivered file's SHA-256 and its edge and town territories match the delivered `REDG`/`TOWN` exactly. Otherwise it falls back to the 1-mile cell territory and prints a warning. Open item for the Map Pipeline: add a node territory to the file. **Effect on balance:** OR-206 node 359 (the chokepoint at the bridge head) is now in **Condon** territory, not Wasco (section 7e). Town and resource-site territories did not change.
- **Places: 34 capturable (was 30).**
  - The map now gives Rhododendron (262, Hood River), Glenwood (179, Trout Lake) and Celilo Village (85, The Dalles) US Census 2020 populations, so all three are hamlets.
  - **Welches is a designer override.** The map still has population 0 for it. By designer decision it is a capturable hamlet at population **250**. It is recorded as `populationSource: designer_override` in `rules.json → scenario.populationOverrides`, which is kept separate from the three Census figures in `scenario.censusFallbackPlaces` (`us_census_2020_pl`). Hood River's `designerOverridePlaces` is 1, and `scenario.designerOverridePlaces` is 1.
  - The counts are 33 capturable places from the map (`capturablePlacesFromMap`) + 1 override = `capturablePlaces` **34**: 19 hamlets, 13 villages and 2 towns. Scenery drops from 6 to 2 (Antelope and Shaniko, both in Wasco). `sceneryPlacesPendingPopulationFix` goes from 4 to 0.
  - **Correction: Lyle is a hamlet.** Lyle (Goldendale territory, population 499 in both v0 and v1) falls in the `economy.json` hamlet band (50–499), but v0.3 and v0.4 counted it as a village. The new map-based validator caught this. Goldendale is now 3 hamlets and 2 villages (was 2/3), and the Remnant's starting manpower falls by 6/min. If Lyle should play as a village, it needs a designer override like Welches (population ≥ 500).
  - Territories that changed: The Dalles 1/2/1 (+0 scenery; was 0/2/1 +1), Hood River 4/0/1 (+0; was 2/0/1 +2), Trout Lake 2/1/0 (+0; was 1/1/0 +1), and Goldendale 3/2/0 (was 2/3/0). Starting holdings follow: the Remnant holds 3 hamlets and 2 villages, and the Compact holds 4 hamlets and 1 town.
- **Depot placement rules** (`rules.json → supply`):
  - `maxDepotsPerNode: 1` and `depotMinSpacingCm: 804670` (5 road-miles, measured along the road graph, whoever owns the depots).
  - Lead Engineer rule: a depot can go on **any road node inside a territory the builder owns**, and that node must be **connected to the builder's supply network when the command executes**. That means inside HQ range, or inside range of one of the builder's existing depots, by road distance.
  - These are recorded as the enums `depotSpacingMeasure: road_graph_shortest_path`, `depotSpacingCountsAllFactions`, `depotPlacementNode: any_road_node_in_owned_territory`, `depotPlacementRequiresConnectedToOwnSupply` and `depotPlacementCheckedAt: command_execute`. The HQ is not a depot and does not count toward spacing.
  - `tools/supply.py` enforces all four rules (`DepotBoard.check`). **The Condon relay still works**: one depot at OR-206 node 368 in Wasco territory (section 7e).
- **`tools/validate.py`** now recomputes each territory's hamlet, village, town, scenery and site counts from the v1 map's `TOWN`/`RSRC` records with the `economy.json` tier rule, after applying `populationOverrides`. It checks that:
  - each override is a real override (map population 0, no Census flag) and is labelled `designer_override`;
  - the Census entries match the map;
  - capturable places = map count + overrides (33 + 1 = 34);
  - territory `mapStableKey` values match the map;
  - the depot rule fields are positive integers.
- **No unit, building, economy or combat value changed.** `dataVersion` is 5 in every file. `sim.py` is untouched, and the golden file still matches (44 lines).
- **Curves** (sections 7b to 7e): the race for The Dalles, Remnant fuel and armour, and Condon are **unchanged**. The Compact's manpower curve moves (new Hood River hamlets), and the Remnant has 6 manpower/min less (Lyle). See "What changed in the curves compared with v0.4" in section 7d.

## Changes in v0.4 (from v0.3)

- **Real road distances and adjacency.** Every estimated road distance is gone. `tools/roads.py` reads the Map Pipeline export `rts-map/gorge/deliver/territory-roads.v0.json` (13 × 13 seat-to-seat shortest road lengths in cm, plus each territory's neighbour list) straight from the file; nothing is typed in by hand. The export's territory id is `bakeIndex`, and the loader checks every seat name against the `rules.json` id. No JSON data file held distances or adjacency before, so none needed a value replaced; the estimates lived in `tools/econ.py` (hand-set capture seconds) and in this document.
- **`tools/econ.py` capture times are now derived**, not typed in: travel time = road cm ÷ (movement-class base speed × the movement table's secondary-paved surface ‰), garrison clearing and contested captures come from the new `tools/race.py`, which uses `sim.py`'s spec `damage`, `pick_target` and `reload_ticks`, and capture timers come from `rules.json`. `run()` also tracks battalion losses, keeps a per-second stock trace, and takes an `end_sec`. It is still all integers.
- **The Compact Settler AI now races to The Dalles and walls it** (decision 4 from v0.3). Its first 4 builds are wheeled rifle battalions. See section 7.
- **New `tools/supply.py`** checks HQ and depot reach against the real roads. It uses the export, plus the baked road graph named in the export's `source` field for road-node depot sites. It adds the Condon answer (section 7e) and the Hood River finding (section 7f).
- **`tools/validate.py`** now also checks that every data file has the same `dataVersion` and that the road export loads and matches the `rules.json` territories.
- **No balance value changed.** Remnant HQ fuel stays at 45/min (section 7a fuel check), and depot range stays at 30 road-miles. `dataVersion` is 4 in every file. **No battle changed**: `sim.py` is untouched and the golden file matches.

Old estimates against the real export (road-miles):

| Leg | v0.3 estimate | Real |
|---|---|---|
| Goldendale – The Dalles (Dalles Bridge, US-197) | ~35 | **32.8** |
| Hood River – The Dalles (I-84) | 17.5 | **21.2** |
| Goldendale – Wasco (Sam Hill Bridge, US-97) | ~30 | **21.8** |
| Goldendale – Roosevelt | ~40 | **39.6** |
| Hood River – Cascade Locks | ~20 | **19.6** |
| Hood River – White Salmon | "walk" | **2.6** |
| Goldendale – Condon | ~70 via Wasco | **62.6** (via Wasco: 21.8 + 40.8) |
| The Dalles – Condon | – | **69.0** (via Wasco: 28.1 + 40.8) |
| Goldendale – Hood River | – | **44.4** (WA-14, Hood River Bridge) |

## Changes in v0.3 (from v0.2)

- **Naming.** A **battalion** is the sim entity. A **task force** is a group of battalions that the AI manages. The word "squad" is gone:
  - `squadTargetSize` is now `taskForceTargetSize`.
  - `retreatThresholdPpt` is the task force's total HP in ‰, with dead battalions counting as 0.
  - `minGarrisonPerTown` now counts **battalions** (Settler 4, Warlord 2, Raider 0).
- **High ground clarified** in the normative spec (1.0 and 1.1). `tools/sim.py` now places battalions on an `ElevationLayer` of about 89 m cells and reads the cell value, with no interpolation. There are 3 new golden damage vectors: the same cell, a 1 cm move across a boundary, and the reverse. **No battle result changed.**
- **Real territories** replace the 10-territory estimate. The map now has 13 territories, 14 hamlets, 14 villages, 2 towns and 6 scenery places (v0.2 assumed 15/13/2). The 10-minute curves were re-run.
- **Starting setup (section 7):**
  - The Compact starts at **Hood River** (territory 6) and the Remnant at **Goldendale** (territory 11, Washington side).
  - **The Dalles (4) starts neutral** and is the main prize between them.
  - The starting holdings are now listed by territory id, with the places each territory contains.
  - The HQ now counts as an owned town for the **Tier II requirement** (`hqSatisfiesOwnedTierRequirement`).
  - HQ income is now per faction: the Remnant HQ gives 45 fuel/min, the Compact and Road Clans HQs 20.
- `buildings.json` no longer carries HQ income (duplicate removed). `dataVersion` is now 3.

## 1. Combat resolution spec (normative)

This section is the contract. `tools/sim.py` (functions `damage`, `pick_target` and `fight`) implements it line for line. `tools/golden_expected.txt` holds the expected outputs: 17 damage vectors and 27 battles. An engine implementation must reproduce every line.

### 1.0 Conventions
- Every quantity is a signed 64-bit integer (`int64`). There are no floats, no fixed-point fractions and no RNG in combat.
- `floor(x / y)` is integer division. Every operand in the damage chain is non-negative, so truncation and floor give the same result. In each step, **multiply first, then divide**.
- `A` is the firing entity and `T` is its target. "Entity" means a unit or an armed structure.
- Elevation is read from the baked terrain **elevation layer** (cells of about 89 m; the exact `cellSizeCm` comes from the map bake header). Positions are non-negative integer cm, so `cell = (floor(x / cellSizeCm), floor(y / cellSizeCm))`, and elevation is that cell's stored value in **decimetres**. It is **not interpolated**, and it is independent of the 250 m movement grid.

### 1.1 (a) Damage per volley
Inputs computed before the chain:
- `col(T)` = `armored` if `T.targetClass` is `armored` or `structure`, otherwise `soft`.
- `attackPpt` = clamp(1000 + sum of the modifiers below, 0, 4000), where the modifiers are:
  - +1000 (`indirectVsStructureBonusPpt`) if `A.weapon.indirect` and `T` is a structure;
  - -200 (`armourAttackInUrbanPpt`) if `A.targetClass` = `armored`, `A.weapon.indirect` = false, and `T` stands in an urban cell;
  - +50 × `A.veterancyLevel` (0 to 3).
- `defensePpt` of `T` = max(sum of the terms below, -500), where the terms are:
  - +300 if `T` is a unit with `targetClass` = `soft` in an urban cell;
  - +200 if `T` is a unit with `targetClass` = `soft` in a forest cell (a cell is urban or forest, never both);
  - +100 if `T`'s cell grade is 300 to 599‰;
  - +200 × L if `T` (unit or structure) is inside the capture zone of a holding its own player owns, and that holding has fortification level L;
  - -200 if `T` is on a ford cell.

The chain, in this fixed order, with a floor after every step:
```
1. d = base            base = A.weapon.damageVsArmored if col(T) == armored
                            else A.weapon.damageVsSoft   (structures use damageVsArmored)
2. d = floor(d * attackPpt / 1000)
3. d = floor(d * (1000 - T.armorPpt) / 1000)
4. d = floor(d * 1000 / (1000 + min(defensePpt, maxDefensePpt)))          maxDefensePpt = 1000
5. if A.elevationDm - T.elevationDm >= 500:                        (thresholdDm = 500 dm = 50 m)
       d = floor(d * (1000 + highGroundPpt) / 1000)                        highGroundPpt = 150
6. d = max(d, 1)
```
**High-ground clarification (v0.3).** `A.elevationDm` and `T.elevationDm` are the elevation-layer cell values under each battalion's current position, compared exactly as `attackerDm - targetDm >= 500`. There is no interpolation, averaging or line-of-sight sampling. Two battalions in the **same cell never get the bonus**, whatever their positions within it, because the difference is 0. A 1 cm move across a cell boundary can switch the bonus on or off. The test is `>=`: exactly 500 dm qualifies and 499 does not. There is no uphill penalty; the lower unit simply lacks the bonus. Golden damage vectors 3 to 7 cover these cases: 500 vs 499 dm, the same cell, two positions 1 cm apart across a boundary, and the reverse.

### 1.2 (b) Simultaneous resolution within a tick
Each tick's combat phase runs these steps in order:
0. **Supply update**, only when `tick % 40 == 0` (`recheckIntervalTicks`). For each unit, in ascending id:
   - If the unit is out of supply and has no `cutDetectedTick` yet, set `cutDetectedTick = tick`. Refuel and rearm stop from this moment.
   - If it is out of supply and `tick − cutDetectedTick >= 1200` (`graceTicks`), set `penalized = true`.
   - If it is back in supply, clear `cutDetectedTick`, set `penalized = false` and rearm to `ammoCapacity`. Rearming charges the owner `ammoRefillMaterielPerAmmoPpt`.
   Structures have no supply state and unlimited ammo.
1. **Decide.** Every living armed entity `A`, in ascending entity id:
   - Skip `A` if `A.cooldown > 0`, if `A` is a unit with `ammo == 0`, or if its weapon has `deployTicks > 0` and it is not deployed.
   - Choose `T` by rule (c), using the **start-of-tick** state. If there is no valid target, skip (`cooldown` stays 0, so `A` stays ready).
   - Compute `d` by rule (a), using the start-of-tick state, and add it: `pending[T] += d`.
   - Set `A.cooldown = effectiveReload(A)`. That is `reloadTicks` normally, or `floor(reloadTicks * 1000 / 500)` when `penalized` (fireRatePpt = 500, which doubles the reload).
   - If `A` is a unit, `A.ammo −= 1`.
2. **Apply.** For every `T` with a pending entry, `T.hp −= pending[T]`, all at once.
3. **Remove.** Every entity with `hp <= 0` is removed at the end of the tick. An entity killed this tick has already fired in step 1.
4. **Cool down.** For every surviving entity with `cooldown > 0`, `cooldown −= 1`.
   So an entity that fires on tick `t` with reload `R` fires again on tick `t + R` at the earliest.

### 1.3 (c) Deterministic target selection
**Valid targets** for `A` are enemy entities that meet all of these conditions:
- alive at the start of the tick;
- visible to `A`'s player on this tick (the simulation fog state, updated every 5 ticks);
- `dx² + dy² <= rangeCm²`, and `dx² + dy² >= minRangeCm²` when `minRangeCm > 0`, with positions in cm;
- not an `air` unit, unless `A.weapon.canTargetAir`.
Non-combat units (convoys, engineers) are valid targets.

If `A` has a player or AI attack order on a target that is valid, that target is used. Otherwise, pick the valid target with the smallest key:
```
pref(A) = armored if A.weapon.damageVsArmored > A.weapon.damageVsSoft
          soft    if A.weapon.damageVsSoft    > A.weapon.damageVsArmored
          none    if equal
key(T)  = ( 0 if pref(A) == none or col(T) == pref(A) else 1,   # best damage column
            T.hp at start of tick,                              # lowest current HP
            T.entityId )                                        # lowest entity id
```
The target is chosen again for every volley; there is no stickiness. Many shooters may pick the same target in one tick, and overkill is wasted. That waste is intended and deterministic.

### 1.4 (d) Iteration order and integer bounds
Entities are always iterated in ascending entity id. The damage sum in step 2 is commutative, but the fixed order keeps logs and state hashes identical. The loader must reject data outside these limits (`rules.json → combat.limits`), which guarantees that no int64 intermediate can overflow:

| Quantity | Limit |
|---|---|
| maxHp | ≤ 1,000,000 |
| damageVsSoft, damageVsArmored | 0 … 65,535 |
| armorPpt | 0 … 1000 |
| attackPpt (after clamp) | 0 … 4000 |
| defensePpt (after clamp) | -500 … unbounded, but the chain uses min(defensePpt, 1000) |
| elevation | -5,000 … 90,000 dm |
| rangeCm, minRangeCm | ≤ 64,373,760 (400 mi) |
| attackers hitting one target in one tick | ≤ 400 (4 players × 100 cap) |

Worst-case intermediates:
| Step | Worst case | Value |
|---|---|---|
| 2 | 65,535 × 4,000 | 262,140,000 |
| 3 | 262,140 × 1,000 | 262,140,000 |
| 4 | 262,140 × 1,000, divided by at least 500 | 262,140,000 → 524,280 |
| 5 | 524,280 × 1,150 | 602,922,000 → 602,922 |
| pending sum per target | 400 × 602,922 | 241,168,800 |
| hp after damage | ≥ −241,168,800 | |
| distance² on a 200 mi map | 2 × 32,186,880² | 2,071,990,488,268,800 |
| range² | 64,373,760² | 4,143,980,976,537,600 |

The largest intermediate is 4,143,980,976,537,600 (range²). That is about 2,200 times below the int64 maximum of 9,223,372,036,854,775,807. The damage chain alone also fits in int32, but it must use int64 anyway.

**Reference-battle abstractions (not part of the spec):** `tools/sim.py` treats every enemy as in range and visible, except that direct-fire weapons wait `approach_ticks` (1200) in town assaults while artillery fires. Attackers stand in one elevation cell and defenders in another, and deploy state is ignored (artillery counts as deployed).

## 2. Field reference (text moved out of the JSON)

General:
- Units: time is sim ticks at 20 ticks/s, distance is whole cm, elevation is dm, and fractions are ‰ in keys ending in `Ppt`. See `timings.md` for seconds.
- `null` is used only for `weapon` on unarmed units and structures (engineers, convoys, walls, economy and production buildings).
- `requires: none`, `requiresBuilding: none` and `buildingScope: none` mean "no prerequisite".
- `maxPerPlayer`, `maxPerTerritory` and `maxPerSite` set to 0 mean "no limit".
- `techTierByFaction` gives the tier at which each faction listed in `factions` can build the structure. The Compact gets the artillery position at Tier I.
- `buildSlotCost` is the number of town build slots used.

AI weights (`ai_personalities.json`): every `…Ppt` weight runs from 0 to 1000 (formerly `weightRange`).

Holding tiers (`economy.json → holdingTiers`, formerly `tierLookup`):
- A place gets the first tier where `populationMin <= population <= populationMax`.
- A population below `scenery.populationBelow` (50) is scenery and cannot be captured.
- If population is missing, the OSM `place` tag decides (`hamlet`, `village`, `town` or `city`).
- v0.5 (baked into the map from v0.6): a `scenarios/columbia_gorge_100mi_test.json → derived.populationOverrides` entry (v0.7; was `rules.json → scenario`) replaces the map population **before** the tier lookup. These entries are designer decisions (`populationSource: designer_override`), not census figures. The only one is Welches (map town 9, Hood River), set to 250, which makes it a hamlet. Map populations that come from the US Census 2020 fallback (`Town.sourceFlags` bit4) are listed in `derived.censusFallbackPlaces` for reference and are not overrides.

Capture timer (formerly `timerFormula`): `ticks = floor(baseTicks * (1000 + min(level * 250, 500)) / 1000)`, where `baseTicks` comes from `capture.timerTicksByTier`. Hamlets have `maxFortificationLevel` 0.

Damage formula and target selection (formerly `damageFormula` and `targetSelection`): see section 1. `damageChainOrder` and `targetSelectionOrder` in `rules.json` are enum lists that restate that order, and the loader can assert them.

Difficulty (formerly `difficultyNote`): `incomeMultiplierPpt` = 1000 + `incomeBonusPpt`. Brutal's +100‰ on manpower, fuel and materiel is the only numeric AI advantage. How AI decisions scale with difficulty lives in the AI engineer's `ai_difficulty.json`.

Movement (formerly `movement.source`): speeds come from `movement-table-v0.1.md`. `air: 1000` cm/tick is a placeholder, because the movement table does not give an air speed.

HQ income: `economy.json → hqIncomePerMinByFaction` is the only source. v0.3 removed the duplicate `incomePerMin` from the HQ record in `buildings.json` (no other building used it). Tech: `hqSatisfiesOwnedTierRequirement: true` on Tier II means the owner's HQ counts as an owned town **for the Tier II requirement only**, not for income, build slots or capture.

Territories: `scenarios/columbia_gorge_100mi_test.json → derived.territories` (v0.7; was `rules.json → scenario.territories`) is the real Gorge bake. `id` is the seat's snake_case name, `bakeIndex` is the baker's number, and `seatTier` is the tier of the seat place. `territoryOwnershipRule: hold_seat` means whoever holds the seat owns the territory and its sites. The other places are ordinary holdings that pay manpower to whoever holds them. `mapStableKey` (v0.5) is the map's permanent `TERR.stableKey`, and tools match map territories by it. `places`, `sceneryPlaces` and `sites` are recomputed from the map by `tools/validate.py`. `designerOverridePlaces` counts the places whose tier comes from a designer override. `capturablePlaces` = `capturablePlacesFromMap` + `designerOverridePlaces`.

Pop cap (formerly `perTierBonusField`): cap = `popCap.hqBonus` + the sum of `holdingTiers[].popCapBonus` over owned holdings, limited to `popCap.hardCap`.

Supply: `outOfSupply.fireRatePpt: 500` is the −500‰ fire-rate penalty and `speedPpt: 700` is the −300‰ speed penalty. `rearmOnRecheckWhenInSupply` means ammo is topped up at each supply recheck.

Depot placement (v0.5, rule confirmed by the Lead Engineer). A `supply_depot` order is legal only if, when the command **executes**, all of the following hold:
1. The target is a road node (`depotPlacementNode: any_road_node_in_owned_territory`; a town slot is the town's gate node). The node lies in a territory the builder owns, using the map's per-node territory (`RNOD.territoryId`, from v0.6 read from the map file).
2. The node is connected to the builder's supply network (`depotPlacementRequiresConnectedToOwnSupply`). It must be within `hqRangeCm` of the builder's HQ, or within `depotRangeCm` of one of the builder's existing depots, measured along the road graph.
3. The node holds fewer than `maxDepotsPerNode` (1) depots of **any** faction.
4. Every existing depot of **any** faction is at least `depotMinSpacingCm` (804,670 cm = 5 road-miles) away by shortest road-graph distance (`depotSpacingMeasure: road_graph_shortest_path`, `depotSpacingCountsAllFactions: true`). The HQ is not a depot, so it does not count.

A depot placed first therefore denies nearby sites to both sides. `tools/supply.py` (`DepotBoard`) implements these checks.

## 3. AI personalities (task-force semantics)

- **Battalion:** the sim entity. Everything in `units.json` is a battalion or convoy.
- **Task force:** a group of battalions that the AI manages together. `taskForceTargetSize` is the number of battalions the AI tries to put in each task force.
- **`retreatThresholdPpt`:** the task force's total HP in ‰, computed as `floor(1000 × Σ member hp / Σ member maxHp)`. The sum covers every battalion assigned to the task force. **Dead battalions count as 0 hp** but keep their maxHp in the denominator. The task force retreats toward the nearest friendly in-supply road node when this value falls **below** the threshold.
- **`minGarrisonPerTown`:** the minimum number of **battalions** the AI keeps inside the capture zone of each owned town or city. Villages and hamlets have no minimum.
- **`attackForceRatioPpt`:** the AI commits a task force only when its value is at least this ‰ of the estimated defender value, using last-seen data only.

| Personality | Active | Aggr | Risk | Fortify | Expand | Raid | Retreat (task force HP ‰) | Min garrison (battalions) | Task force size | Attack ratio ‰ |
|---|---|---|---|---|---|---|---|---|---|---|
| homestead_compact_settler | True | 200 | 150 | 850 | 450 | 100 | 500 | 4 | 4 | 1800 |
| remnant_command_warlord | False | 800 | 600 | 300 | 550 | 250 | 300 | 2 | 5 | 1300 |
| road_clans_raider | False | 600 | 700 | 100 | 400 | 900 | 600 | 0 | 3 | 1000 |

Suggested starting values for the AI engineer's `ai_difficulty.json` (not in this data set):

| Difficulty | Commands per min | Replan interval (ticks) |
|---|---|---|
| easy | 20 | 600 |
| normal | 50 | 400 |
| hard | 80 | 240 |
| brutal | 120 | 120 |

## 4. Economy model and the real Gorge territories

Site income is counted per owned territory, up to a cap. Territory ownership means holding the seat.

| Site | Yield per site / min | Counted per territory (max) | Needs | Territory max / min |
|---|---|---|---|---|
| fuel_station | 1 fuel | 4 | nothing (passive) | 4 |
| fuel_storage_depot | 30 fuel | 2 | 1 fuel pump per site | 60 |
| refinery | 60 fuel | 1 | 1 fuel pump per site | 60 |
| industrial_area | 15 materiel | 4 | 1 salvage yard per territory | 60 |

HQ income per minute (manpower / fuel / materiel): remnant_command 10/45/50; homestead_compact 10/20/50; road_clans 10/20/50.

### 4a. The 13 territories and what each is worth once owned

Values assume every place is held and every building is up: 1 salvage yard, plus a pump on each counted depot. The Compact's ×1150‰ manpower is not applied here. Places come from the corrected v1 map (v1.2), with Welches as a designer override (v0.5, baked into the map from v0.6).

| # | Territory | Seat tier | Places h/v/t (+scenery) | Fuel stn (counted) | Depots (counted) | Industrial (counted) | Manpower/min | Fuel/min | Materiel/min | Start |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | maupin | hamlet | 2/0/0 (+0) | 2 (2) | 0 (0) | 5 (4) | 4 | 2 | 60 | neutral |
| 2 | condon | village | 1/1/0 (+0) | 3 (3) | 2 (2) | 4 (4) | 10 | 63 | 60 | neutral |
| 3 | wasco | hamlet | 4/0/0 (+2) | 4 (4) | 0 (0) | 7 (4) | 8 | 4 | 60 | neutral |
| 4 | the_dalles | town | 1/2/1 (+0) | 8 (4) | 0 (0) | 10 (4) | 42 | 4 | 60 | neutral |
| 5 | cascade_locks | village | 0/1/0 (+0) | 2 (2) | 2 (2) | 1 (1) | 8 | 62 | 15 | neutral |
| 6 | hood_river | town | 4/0/1 (+0) | 19 (4) | 4 (2) | 13 (4) | 32 | 64 | 60 | **Compact HQ** |
| 7 | arlington | village | 0/1/0 (+0) | 1 (1) | 0 (0) | 0 (0) | 8 | 1 | 0 | neutral |
| 8 | carson | village | 0/3/0 (+0) | 5 (4) | 0 (0) | 21 (4) | 24 | 4 | 60 | neutral |
| 9 | white_salmon | village | 1/2/0 (+0) | 5 (4) | 0 (0) | 3 (3) | 18 | 4 | 45 | neutral |
| 10 | roosevelt | hamlet | 1/0/0 (+0) | 0 (0) | 0 (0) | 4 (4) | 2 | 0 | 60 | neutral |
| 11 | goldendale | village | 3/2/0 (+0) | 15 (4) | 0 (0) | 50 (4) | 22 | 4 | 60 | **Remnant HQ** |
| 12 | trout_lake | village | 1/1/0 (+0) | 1 (1) | 0 (0) | 3 (3) | 10 | 1 | 45 | neutral |
| 13 | outlook | hamlet | 1/0/0 (+0) | 0 (0) | 0 (0) | 8 (4) | 2 | 0 | 60 | neutral |

These match the v1 map plus the override: 19 hamlets (18 from the map + Welches), 13 villages, 2 towns, 2 scenery places, 65 fuel stations, 8 depots and 129 industrial areas. That is **34 capturable places** (33 from the map + 1 designer override), and `tools/validate.py` recomputes every row from the map.
- v0.6: Klickitat (hamlet, 362) moved from Trout Lake to White Salmon with one industrial area (the Klickitat River now blocks). Totals are unchanged.
- The v0.5 changes are The Dalles +Celilo Village (hamlet, Census), Hood River +Rhododendron (Census) and +Welches (designer override, 250), Trout Lake +Glenwood (Census), and Goldendale's Lyle corrected from village to hamlet (population 499).
- Only 3 territories have fuel storage depots: Condon (2), Cascade Locks (2) and Hood River (4, of which 2 count).

## 5. Time-to-kill examples (1 v 1, no return fire, spec damage)

| Attacker | Target | Situation | attackPpt | Dmg/volley | Volleys | TTK ticks | TTK s |
|---|---|---|---|---|---|---|---|
| remnant_armoured_battalion | compact_militia | open | 1000 | 150 | 6 | 500 | 25 |
| remnant_armoured_battalion | compact_militia | walled town L2 (urban 300 + walls 400) | 800 | 70 | 13 | 1200 | 60 |
| remnant_armoured_battalion | compact_militia | walled town L2, armour 50 m higher | 800 | 80 | 12 | 1100 | 55 |
| compact_militia | remnant_armoured_battalion | open | 1000 | 4 | 350 | 20940 | 1047 |
| compact_at_battalion | remnant_armoured_battalion | open | 1000 | 82 | 18 | 1360 | 68 |
| at_emplacement | remnant_armoured_battalion | open | 1000 | 55 | 26 | 2000 | 100 |
| remnant_armoured_battalion | at_emplacement | open | 1000 | 55 | 26 | 2500 | 125 |
| remnant_armoured_battalion | at_emplacement | in walled town L2 (urban cell) | 800 | 31 | 46 | 4500 | 225 |
| remnant_sp_artillery | bunker | open | 2000 | 60 | 42 | 8200 | 410 |
| remnant_sp_artillery | compact_militia | walled town L2 | 1000 | 70 | 13 | 2400 | 120 |
| artillery_position | remnant_armoured_battalion | open | 1000 | 49 | 29 | 5600 | 280 |
| artillery_position | remnant_motor_rifles | open | 1000 | 136 | 9 | 1600 | 80 |
| remnant_motor_rifles | compact_militia | open | 1000 | 50 | 18 | 1020 | 51 |
| remnant_motor_rifles | compact_militia | open, rifles 50 m higher | 1000 | 57 | 16 | 900 | 45 |
| compact_militia | remnant_motor_rifles | open | 1000 | 28 | 43 | 2520 | 126 |
| remnant_drone_detachment | compact_towed_artillery | open | 1000 | 40 | 18 | 2040 | 102 |
| remnant_mech_battalion | compact_militia | open | 1000 | 90 | 10 | 720 | 36 |

## 6. Balance-intent checks (re-run for v0.3; unchanged in v0.4, v0.5 and v0.6)

Value = manpower + fuel + materiel. Compact fortification structures are counted at the Compact price (750‰), and walls count toward the defender. Every row is reproduced by `tools/sim.py`. **No battle changed from v0.2**: the high-ground change only alters the damage-vector lines in the golden file.

### 6a. Remnant armour beats Compact militia in the open

| Battle | Atk value | Def value | Ratio ‰ | Winner | Duration s | Atk units / hp left | Def units / hp left | vs v0.2 |
|---|---|---|---|---|---|---|---|---|
| 1 armour vs 5 militia, open | 380 | 425 | 894 | **attacker** | 145 | 1 / 820 | 0 / 0 | unchanged |
| 3 armour vs 13 militia, open | 1140 | 1105 | 1031 | **attacker** | 125 | 3 / 3040 | 0 / 0 | unchanged |
| 3 armour vs 16 militia, open | 1140 | 1360 | 838 | **attacker** | 190 | 2 / 2268 | 0 / 0 | unchanged |
| 5 motor rifles vs 8 militia, open | 700 | 680 | 1029 | **defender** | 195 | 0 / 0 | 1 / 150 | unchanged |
| 5 motor rifles vs 8 militia, rifles on high ground (+50 m) | 700 | 680 | 1029 | **attacker** | 153 | 1 / 920 | 0 / 0 | unchanged |
| 5 motor rifles vs 8 militia, militia on high ground (+50 m) | 700 | 680 | 1029 | **defender** | 108 | 0 / 0 | 3 / 2700 | unchanged |

Armour wins in the open even when slightly out-valued. Motor rifles vs militia at equal value is a narrow militia win (since v0.2). High ground (50 m, different cells) decides that even matchup either way.

### 6b. Armour struggles against AT emplacements

| Battle | Atk value | Def value | Ratio ‰ | Winner | Duration s | Atk units / hp left | Def units / hp left | vs v0.2 |
|---|---|---|---|---|---|---|---|---|
| 2 armour vs 3 AT emplacements | 760 | 585 | 1299 | **defender** | 68 | 0 / 0 | 3 / 3045 | unchanged |
| 3 armour vs 3 AT emplacements | 1140 | 585 | 1948 | **defender** | 132 | 0 / 0 | 2 / 1480 | unchanged |
| 4 armour vs 3 AT emplacements | 1520 | 585 | 2598 | **attacker** | 130 | 2 / 2360 | 0 / 0 | unchanged |
| 5 armour vs 3 AT emplacements | 1900 | 585 | 3247 | **attacker** | 90 | 4 / 4665 | 0 / 0 | unchanged |
| 2 armour vs 4 AT battalions, open | 760 | 680 | 1117 | **defender** | 44 | 0 / 0 | 2 / 1458 | unchanged |
| 4 armour + 1 SP artillery vs 3 AT emplacements | 1810 | 585 | 3094 | **attacker** | 155 | 3 / 3870 | 0 / 0 | unchanged |

Armour alone needs about 2.6× the emplacements' value.

### 6c. A walled Compact town holds against about 1.5× its value

Garrison: 6× compact_militia, 2× compact_at_battalion, 1× bunker, 1× at_emplacement, 1× artillery_position, 1× town_walls_l1, 1× town_walls_l2. Total value 1719.
The attacker mix is filled in the order armour, rifles, mech, SP artillery, rifles, armour, mech.

| Battle | Atk value | Def value | Ratio ‰ | Winner | Duration s | Atk units / hp left | Def units / hp left | vs v0.2 |
|---|---|---|---|---|---|---|---|---|
| walled L2, target 1000‰: 2x remnant_armoured_battalion, 2x remnant_motor_rifles, 1x remnant_mech_battalion, 1x remnant_sp_artillery | 1620 | 1719 | 942 | **defender** | 200 | 0 / 0 | 7 / 9300 | unchanged |
| walled L2, target 1250‰: 2x remnant_armoured_battalion, 2x remnant_motor_rifles, 2x remnant_mech_battalion, 1x remnant_sp_artillery | 1910 | 1719 | 1111 | **defender** | 252 | 0 / 0 | 5 / 7360 | unchanged |
| walled L2, target 1500‰: 3x remnant_armoured_battalion, 3x remnant_motor_rifles, 2x remnant_mech_battalion, 1x remnant_sp_artillery | 2430 | 1719 | 1413 | **defender** | 540 | 0 / 0 | 2 / 2706 | unchanged |
| walled L2, target 1750‰: 3x remnant_armoured_battalion, 3x remnant_motor_rifles, 3x remnant_mech_battalion, 1x remnant_sp_artillery | 2720 | 1719 | 1582 | **attacker** | 590 | 2 / 1820 | 0 / 0 | unchanged |
| walled L2, target 2000‰: 3x remnant_armoured_battalion, 4x remnant_motor_rifles, 3x remnant_mech_battalion, 2x remnant_sp_artillery | 3150 | 1719 | 1832 | **attacker** | 435 | 4 / 4445 | 0 / 0 | unchanged |
| walled L2, target 2500‰: 5x remnant_armoured_battalion, 4x remnant_motor_rifles, 4x remnant_mech_battalion, 2x remnant_sp_artillery | 4200 | 1719 | 2443 | **attacker** | 280 | 9 / 10681 | 0 / 0 | unchanged |
| unwalled town: 2x remnant_armoured_battalion, 2x remnant_motor_rifles, 1x remnant_mech_battalion, 1x remnant_sp_artillery | 1620 | 1382 | 1172 | **defender** | 224 | 0 / 0 | 4 / 7208 | unchanged |
| unwalled town: 2x remnant_armoured_battalion, 2x remnant_motor_rifles, 2x remnant_mech_battalion, 1x remnant_sp_artillery | 1910 | 1382 | 1382 | **defender** | 284 | 0 / 0 | 3 / 5212 | unchanged |
| unwalled town: 3x remnant_armoured_battalion, 3x remnant_motor_rifles, 2x remnant_mech_battalion, 1x remnant_sp_artillery | 2430 | 1382 | 1758 | **attacker** | 460 | 2 / 2065 | 0 / 0 | unchanged |

The walled town holds at 1.41× (2 defenders left) and falls narrowly at 1.58×. Without walls, the same 2,430-value force wins.

### 6d. Supply cut matters within about 2 minutes

Supply is rechecked every 40 ticks and the grace period is 1200 ticks (60 s). After the grace period the reload doubles and speed drops to 700‰. Refuel and rearm stop at the first recheck after the cut.

| Unit | Ammo (volleys) | Full-rate fire time | Fuel | Moving burn/min | Moving time on a full tank |
|---|---|---|---|---|---|
| remnant_armoured_battalion | 24 | 2400 ticks (120 s) | 45 | 20 | 135 s |
| remnant_mech_battalion | 30 | 2400 ticks (120 s) | 40 | 18 | 133 s |
| remnant_motor_rifles | 30 | 1800 ticks (90 s) | 30 | 10 | 180 s |
| remnant_sp_artillery | 12 | 2400 ticks (120 s) | 40 | 18 | 133 s |
| compact_rifle_battalion | 30 | 1800 ticks (90 s) | 25 | 8 | 187 s |
| compact_militia | 40 | 2400 ticks (120 s) | 0 | 0 | n/a (foot) |

| Battle | Atk value | Def value | Ratio ‰ | Winner | Duration s | Atk units / hp left | Def units / hp left | vs v0.2 |
|---|---|---|---|---|---|---|---|---|
| 2x assault on walled town, attackers supplied | 3150 | 1719 | 1832 | **attacker** | 435 | 4 / 4445 | 0 / 0 | unchanged |
| 2x assault on walled town, attackers cut at tick 0 | 3150 | 1719 | 1832 | **defender** | 448 | 0 / 0 | 3 / 5924 | unchanged |
| 3 armour vs 16 militia, armour supplied | 1140 | 1360 | 838 | **attacker** | 190 | 2 / 2268 | 0 / 0 | unchanged |
| 3 armour vs 16 militia, armour cut at contact | 1140 | 1360 | 838 | **defender** | 420 | 0 / 0 | 6 / 5100 | unchanged |
| 3 armour vs 16 militia, armour cut 60 s before contact | 1140 | 1360 | 838 | **defender** | 300 | 0 / 0 | 8 / 6900 | unchanged |
| 1.5x assault on walled town, defenders cut 60 s earlier | 2430 | 1719 | 1413 | **attacker** | 740 | 1 / 420 | 0 / 0 | unchanged |

A cut turns a winning 2× assault and armour's open-field win into losses. A walled town that is cut off loses narrowly at 1.41×.

## 7. Starting setup and 10-minute curves (real Gorge bake)

### 7a. Starting setup

| Faction | Home territory (id, bake #) | Places held at start | HQ income M/F/Mat | Start stock M/F/Mat | Starting buildings | Starting battalions |
|---|---|---|---|---|---|---|
| remnant_command | goldendale (#11) | 3 hamlets, 2 villages, 0 towns (all places in the territory) | 10/45/50 | 300/400/500 | hq, muster_camp, heavy_works | 1× remnant_engineers, 2× remnant_motor_rifles, 1× remnant_recon, 1× remnant_supply_convoy |
| homestead_compact | hood_river (#6) | 4 hamlets (incl. Welches by designer override), 0 villages, 1 town (all places in the territory) | 10/20/50 | 300/400/500 | hq, muster_camp, town_walls_l1 | 1× compact_engineers, 4× compact_militia, 1× compact_scouts, 1× compact_supply_convoy |

All other territories start neutral, including **The Dalles (#4)**. Neutral villages hold 1 militia and neutral towns hold 2 (`holdingTiers[].neutralGarrison`); hamlets have no garrison.

**Tech decision: the HQ counts as a town for the Tier II requirement** (`tech.tier2.hqSatisfiesOwnedTierRequirement: true`). I chose this over making The Dalles the required first objective, because gating armour behind The Dalles breaks the Remnant's intent:
- **Timing.** Goldendale to The Dalles is 32.8 road-miles, which the starting motor rifles cover in 4:19. Clearing the 2-militia garrison takes about 1 min and the town timer is 2 min, so even uncontested The Dalles falls at about 7:26. Tier II would then finish at about 9:00 and the first armour at about 10:00. The Remnant would spend the whole opening as a motor-rifle army. It would have no armour to buy, so the fuel pressure at 7 to 9 minutes would never happen.
- **Race.** The Compact is 21.2 road-miles from The Dalles and gets there first (section 7b). A tech gate would reward a Compact rush onto the one objective the Remnant cannot lose.

The Dalles stays the main prize for other reasons (section 7g). The rule is symmetric: every HQ satisfies it. Tier III still needs a real city and is unavailable on this map.

**Fuel check (the Remnant has no depot at home).** Goldendale has 15 fuel stations (4 count) and 0 depots. The nearest depots are Condon (62.6 road-mi via Wasco, section 7e) and Cascade Locks, which is behind the Compact's home. v0.4 criterion: 45 stays unless the reference curve sits at 0 fuel for more than 60 s before 10:00. **It does not**: in the real-distance reference run, per-second fuel touches 0 at 4:24, 6:30 and 9:03, for at most 2 s each time, which is just a purchase draining the stock. So the Remnant HQ stays at **45 fuel/min**. For the record, `python3 tools/econ.py --hq-sweep` (reference build, Remnant loses The Dalles):

| Remnant HQ fuel/min | Fuel stock at 0…10 min | Longest run at 0 | Armour / SP artillery / mech finished |
|---|---|---|---|
| 40 | 400, 322, 151, 60, 87, 116, 27, 55, 14, 41, 66 | 2 s | 3:32, 6:08, SP 8:20, no mech |
| 42 | 400, 324, 155, 66, 95, 6, 38, 68, 28, 55, 2 | 2 s | 3:32, 5:49, SP 7:54, no mech |
| **45** | 400, 327, 161, 75, 107, 21, 54, 17, 48, 78, 27 | 2 s | **3:32, 5:24, SP 7:20, mech 9:48** |
| 48 | 400, 330, 167, 84, 119, 36, 72, 38, 71, 24, 53 | 2 s | 3:32, 5:24, SP 6:59, mech 9:01 |
| 50 | 400, 332, 171, 90, 127, 46, 84, 52, 7, 40, 71 | 2 s | 3:32, 5:24, SP 6:59, mech 8:33 |

Resulting home fuel: Remnant 45 + 4 = 49 gross, Compact 20 + 4 + 30 (one pumped depot) = 54, with a second Hood River pump available for 60 more.

**How the reference schedules are built (v0.4).** Nothing below is hand-timed. `tools/econ.py` derives every capture:
- **Travel** = export road cm ÷ (base speed × 850‰ wheeled, 950‰ tracked, 1000‰ foot). That is the movement table's secondary-paved row, a single, slightly pessimistic value, because the export gives road length only (no surface or climb). Wheeled units move 1,020 cm/tick (7.6 mi/min), tracked units 760 (5.7 mi/min) and foot 200 (1.5 mi/min).
- **Garrison clearing** comes from `tools/race.py` with `sim.py` spec damage. The neutral garrison stands in the urban holding (+300 defense) and attackers stand outside it. For example, 2 motor rifles + recon clear 2 town militia in 67 s, 2 militia clear a village militia in 52 s, and 1 rifle battalion clears it in 88 s.
- **Capture timers** come from `rules.json`. Places inside one territory are taken one after another, with a 15 s hop between them (an assumption kept from v0.3, because the export has seats only). After the seat, the model takes hamlets before villages, so The Dalles' new Celilo Village hamlet comes first (v0.5).
- **The contested Dalles** is one `race.py` fight. Everyone is hostile to everyone, including the neutral garrison, targets follow the spec rule, and the timer runs only when a single faction holds the zone.
- **Production times** come from a first production-only pass. No capture happens before 3:53, so early build times do not depend on captures.
- **Engineer travel is not modelled** for construction (as in v0.3), except in the Condon timeline (7e).

Reference plans:
- **Remnant** (v0.3 player build):
  - Starting 2 motor rifles + recon leave for The Dalles at 0:00.
  - Rifle 3 (0:31) takes Wasco's 4 hamlets.
  - Rifle 4 (1:01) takes Roosevelt.
- **Compact Settler** (new):
  - Builds rifle battalions ×4 first.
  - Scouts + rifles 1–2 leave for The Dalles at 1:01, and rifle 3 follows at 1:31.
  - Rifle 4 takes Cascade Locks.
  - Militia 5–6 walk to White Salmon, while the 4 starting militia stay as the Hood River garrison (`minGarrisonPerTown` 4).
  - If it takes The Dalles, `town_walls_l1` is its next construction.
  - Trout Lake (22.8 mi from White Salmon, 15 min on foot) drops out of the 10-minute plan.
  - v0.6: White Salmon's new Klickitat hamlet is 23.7 road-mi from the White Salmon seat (15:55 on foot), so the militia do not reach it before 10:00 and it stays out of the plan. It is 21.3 road-mi from Goldendale, inside Remnant HQ range, but it is a hamlet in a territory the Remnant does not own (no territory income).

### 7b. The race for The Dalles (Normal and Brutal; v0.6 re-run on the corrected map: identical)

| | Leaves | Road-mi | Arrives | Notes |
|---|---|---|---|---|
| Remnant: 2 motor rifles + recon | 0:00 | 32.8 | **4:19** | starting force |
| Compact: scouts + 2 rifle battalions | 1:01 | 21.2 | **3:48** | fights the 2 neutral militia first |
| Compact: rifle battalion 3 | 1:31 | 21.2 | **4:18** | |
| (Remnant armour 1, tracked, if sent at 3:32) | 3:32 | 32.8 | 9:19 | not in the reference |

**Result (reference, Normal and Brutal alike; income does not change unit timing):**
- The Compact arrives 31 s ahead but cannot clear the garrison before the Remnant contests the zone. To capture uncontested it would have to arrive by about 0:55, which is impossible when rifle 2 is only done at 1:01.
- In the three-way fight both militia die by 4:43. The Remnant loses its recon at 4:33 and its rifles at 5:24 and 6:30. The Compact loses its scouts and 2 rifles.
- **The zone is clear for the Compact at 6:31, and The Dalles is captured by the Compact at 8:30.** One rifle survives at 775/1000 hp.
- **Walls L1 finish at 9:50 on Normal (v0.4: 9:45) and 9:16 on Brutal.** The Remnant would then need 150 s (fort level 1) plus +200‰ garrison defense to retake it.
- The Compact takes The Dalles' new Celilo Village hamlet at 9:16, then the villages at 11:59 and 14:42 (v0.4: villages at 11:13 and 13:56, with no hamlet).

**This race is knife-edge.** It turns on the third wheeled battalion on each side (`race.py`, same arrivals):

| Variant | Winner | Capture |
|---|---|---|
| Reference: Compact 2 rifles + scouts + rifle 3; Remnant 2 rifles + recon | **Compact** | 8:30 |
| Compact without rifle 3 | Remnant | 7:55 |
| Compact adds rifle 4 (4:48) | Compact | 7:51 |
| **Remnant also sends rifle 3 (arrives 4:50), `--remnant-commit`** | **Remnant** | **8:02** (2 rifles survive) |
| Remnant sends rifles 3 + 4 | Remnant | 7:46 |

### 7c. Remnant Command at Goldendale (reference build; identical on Normal and Brutal; v0.6: identical to v0.5)

| Min | Gross M / F / Mat | Net M/min | Net F/min | Net Mat/min | Stock M | Stock F | Stock Mat | Battalions | Territories |
|---|---|---|---|---|---|---|---|---|---|
| 0 | 32 / 49 / 50 | 32 | 38 | 47 | 300 | 400 | 500 | 5 | 1 |
| 1 | 32 / 49 / 110 | 32 | 36 | 106 | 192 | 327 | 385 | 6 | 1 |
| 2 | 32 / 49 / 110 | 32 | 34 | 105 | 224 | 161 | 190 | 7 | 1 |
| 3 | 32 / 49 / 110 | 32 | 34 | 105 | 216 | 75 | 75 | 7 | 1 |
| 4 | 34 / 53 / 110 | 34 | 33 | 103 | 248 | 107 | 179 | 8 | 2 |
| 5 | 36 / 53 / 110 | 36 | 35 | 103 | 242 | 21 | 62 | 7 | 2 |
| 6 | 38 / 53 / 110 | 38 | 32 | 102 | 280 | 54 | 165 | 7 | 2 |
| 7 | 42 / 53 / 110 | 42 | 34 | 103 | 280 | 17 | 87 | 6 | 3 |
| 8 | 42 / 53 / 110 | 42 | 30 | 100 | 322 | 48 | 188 | 7 | 3 |
| 9 | 42 / 53 / 110 | 42 | 30 | 100 | 364 | 78 | 288 | 7 | 3 |
| 10 | 42 / 53 / 110 | 42 | 26 | 98 | 326 | 27 | 258 | 8 | 3 |

At 10:00 it owns goldendale, roosevelt and wasco, with holdings of 8 hamlets, 2 villages and 0 towns. v0.5: manpower is 6/min lower throughout because Lyle is a hamlet (section 4a). Fuel, materiel and every event time are identical to v0.4.

Events (m:ss):
- **Production:** rifles 0:31 and 1:01; salvage yard @goldendale 0:41; tier2 2:31; **armour 3:32 and 5:24; SP artillery 7:20; mech 9:48**.
- **Captures:** Wasco seat 3:53 and its other hamlets 4:39, 5:25 and 6:11; Roosevelt 6:44.
- **Losses:** recon 4:33, rifles 5:24 and 6:30 (all at The Dalles).
- **Fuel lows (per second):** 0 at 4:24, 6:30 and 9:03, each for 2 s at most. The minute stock is 21 at 5:00, 17 at 7:00 and 27 at 10:00.

**Commit variant** (`--remnant-commit`: rifle 3 goes to The Dalles, rifle 4 takes Wasco, no Roosevelt):
- The Remnant wins The Dalles at 8:02. It takes the Celilo Village hamlet at 8:48 and the villages at 10:37 and 12:26 (v0.4: villages at 9:51 and 11:40). Wasco falls from 4:23 to 6:41.
- Armour at 3:32 and 5:27, SP artillery at 7:24, no mech by 10:00.
- Fuel is 0 for ≤2 s at 4:27, 6:34, 8:12 and 9:46. The minute stock is 19 at 5:00, 14 at 7:00, 25 at 9:00 and 7 at 10:00.
- Net at 10:00 is 66 manpower / 32 fuel / 159 materiel, after the Dalles salvage yard at 8:52 (v0.4: 78 manpower; −6 Lyle, −8 because the second Dalles village is not held yet, +2 Celilo).
- Fuel pressure is stronger here, because owning The Dalles adds no fuel, but it is still not stuck at 0.

### 7d. Homestead Compact at Hood River (Settler AI, races to The Dalles; v0.6: identical to v0.5)

Normal:

| Min | Gross M / F / Mat | Net M/min | Net F/min | Net Mat/min | Stock M | Stock F | Stock Mat | Battalions | Territories |
|---|---|---|---|---|---|---|---|---|---|
| 0 | 48 / 24 / 50 | 48 | 20 | 45 | 300 | 400 | 500 | 7 | 1 |
| 1 | 48 / 54 / 50 | 48 | 49 | 44 | 188 | 339 | 214 | 8 | 1 |
| 2 | 55 / 54 / 110 | 55 | 47 | 102 | 77 | 346 | 96 | 10 | 1 |
| 3 | 55 / 54 / 110 | 55 | 46 | 100 | 12 | 392 | 146 | 12 | 1 |
| 4 | 55 / 54 / 110 | 55 | 46 | 99 | 7 | 438 | 135 | 13 | 1 |
| 5 | 55 / 54 / 110 | 55 | 47 | 97 | 2 | 485 | 208 | 13 | 1 |
| 6 | 55 / 54 / 110 | 55 | 48 | 97 | 17 | 503 | 145 | 13 | 1 |
| 7 | 64 / 58 / 110 | 64 | 51 | 95 | 14 | 551 | 216 | 13 | 2 |
| 8 | 73 / 60 / 110 | 73 | 53 | 94 | 86 | 404 | 10 | 14 | 3 |
| 9 | 110 / 64 / 110 | 110 | 57 | 94 | 175 | 459 | 104 | 14 | 4 |
| 10 | 112 / 64 / 110 | 112 | 57 | 94 | 286 | 516 | 86 | 14 | 4 |

Brutal (×1100‰ income):

| Min | Gross M / F / Mat | Net M/min | Net F/min | Net Mat/min | Stock M | Stock F | Stock Mat | Battalions | Territories |
|---|---|---|---|---|---|---|---|---|---|
| 0 | 52 / 26 / 55 | 52 | 22 | 50 | 300 | 400 | 500 | 7 | 1 |
| 1 | 52 / 59 / 55 | 52 | 54 | 49 | 192 | 341 | 219 | 8 | 1 |
| 2 | 60 / 59 / 121 | 60 | 52 | 113 | 85 | 354 | 109 | 10 | 1 |
| 3 | 60 / 59 / 121 | 60 | 51 | 110 | 25 | 405 | 171 | 13 | 1 |
| 4 | 60 / 59 / 121 | 60 | 51 | 110 | 25 | 456 | 171 | 13 | 1 |
| 5 | 60 / 59 / 121 | 60 | 52 | 107 | 25 | 508 | 254 | 14 | 1 |
| 6 | 60 / 59 / 121 | 60 | 51 | 105 | 45 | 531 | 202 | 14 | 1 |
| 7 | 70 / 63 / 121 | 70 | 56 | 105 | 49 | 584 | 282 | 14 | 2 |
| 8 | 80 / 66 / 121 | 80 | 59 | 105 | 129 | 443 | 87 | 14 | 3 |
| 9 | 121 / 70 / 121 | 121 | 63 | 105 | 228 | 504 | 80 | 14 | 4 |
| 10 | 123 / 70 / 187 | 123 | 63 | 171 | 350 | 517 | 90 | 14 | 4 |

At 10:00 it owns cascade_locks, hood_river, the_dalles and white_salmon, with holdings of 5 hamlets, 3 villages and 2 towns.

Captures and builds:
- **Normal:** White Salmon 6:44 and 8:51, Cascade Locks 7:04, The Dalles 8:30, Celilo Village 9:16, walls L1 9:50. Militia 3:07, AT 4:28, towed artillery 6:27, tier II 9:24.
- **Brutal:** White Salmon 6:32 and 8:39, Cascade Locks 7:04, The Dalles 8:30, walls L1 9:16, Celilo Village 9:16, Dalles salvage yard 9:56. Militia 2:55, AT 4:10, towed artillery 6:00, tier II 8:40.

**Compact manpower, minutes 2 to 5** (stock at the minute mark; v0.4 in brackets):

| | 2 | 3 | 4 | 5 |
|---|---|---|---|---|
| Normal | 77 (67) | 12 (57) | 7 (47) | **2** (37) |
| Brutal | 85 (75) | 25 (10) | 25 (5) | 25 (**0**) |
| Normal, Remnant commits (Compact loses the race) | 77 (67) | 12 (57) | 45 (25) | 40 (15) |

Four rifle battalions cost 320 manpower up front. With the two new Hood River hamlets (Rhododendron, Census, and Welches, designer override), home manpower is 55/min on Normal (v0.4: 50), or 48 before the recruitment office (v0.4: 43).
- The Settler spends that extra income at once. Militia 2 finishes at 3:07 (v0.4: 3:24), the AT battalion at 4:28 (4:51) and the towed artillery at 6:27 (8:19). So the stock at each minute mark is **lower** in minutes 3 to 5 on Normal, bottoming at 2 at 5:00 (v0.4: 27 at 6:00).
- On Brutal the stock now sits at 25 through minutes 3 to 5, instead of running dry at 5:00.
- The extra early units take materiel ahead of tier II, which slips to 9:24 on Normal (v0.4: 7:26) and 8:40 on Brutal (6:55). That delays Normal walls L1 by 5 s.
- The Compact is still fuel-rich (516 at 10:00) and materiel-short (10 at 8:00 on Normal).

**What changed in the curves compared with v0.5 (v0.6, corrected map):** nothing. Road distances, starting holdings, the Hood River and Goldendale places, and every unit value are the same, so every row above is reproduced exactly on Normal and Brutal. That covers the race (3:48 / 4:18 / 4:19 arrivals, Compact captures at 8:30; with `--remnant-commit` the Remnant captures at 8:02), Remnant fuel and armour (3:32 / 5:24, SP 7:20, mech 9:48, fuel at 0 for ≤ 2 s at 4:24, 6:30 and 9:03, same `--hq-sweep`), Compact manpower at minutes 2 to 5 (Normal 77 / 12 / 7 / 2, Brutal 85 / 25 / 25 / 25, commit 77 / 12 / 45 / 40), Compact tier II (9:24 Normal, 8:40 Brutal) and walls L1 (9:50 / 9:16). The Klickitat move touches only White Salmon's hamlet count and industrial sites. The Compact holds White Salmon without a salvage yard there, and the hamlet is out of reach before 10:00.

**What changed in the curves compared with v0.4 (v0.5 notes, kept for the record):**
- **Race to The Dalles: unchanged.** Arrivals are 3:48 / 4:18 / 4:19, the zone clears at 6:31, and the Compact captures at 8:30. With `--remnant-commit` the Remnant still wins at 8:02, and every row of the variant table is the same. Road distances did not change, and neither did the rifle build times.
- **Remnant fuel and armour: unchanged.** Armour 3:32 / 5:24, SP artillery 7:20, mech 9:48. Fuel touches 0 at 4:24, 6:30 and 9:03 (≤ 2 s each), and the `--hq-sweep` output is identical, so 45 fuel/min stays.
- **Remnant manpower: −6/min** throughout (38 → 32 at start, 48 → 42 at 10:00; stock at 10:00 326 vs 386). This comes from the Lyle correction. No Remnant event moves.
- **Compact manpower, minutes 2 to 5:** see the table above. The curve is higher at 2:00 and then lower at the minute marks on Normal, because the Settler buys earlier. At 10:00 it is 112/min (v0.4: 105).
- **Compact timings:** White Salmon is 17 s earlier on Normal (6:44, 8:51) and 14 s earlier on Brutal. Normal walls L1 are at 9:50 (+5 s). Tier II is about 2 minutes later. The Dalles' villages come 46 s later (11:59, 14:42) because the new Celilo Village hamlet is taken first, at 9:16.
- **Condon: unchanged** (section 7e). The relay depot is at node 368, done at 8:52, and the capture is at 13:44 (fast 11:25).
- Pop cap: see section 8.

**What changed in the curves compared with v0.3 (v0.4 notes, kept for the record):**
- **The Dalles changes hands.** In v0.3 the Remnant took it unopposed at 7:30. Now the Compact wins it at 8:30 and walls it by about 9:45, unless the Remnant commits its third rifle, in which case the Remnant wins it at 8:02.
- **The Remnant's economy is smaller at 10:00:** 48 manpower/min (v0.3: 88) and 110 materiel (v0.3: 170), because it lacks The Dalles. It fields the same heavy units at the same times (armour 3:32 and 5:24, SP artillery 7:20), and the mech now finishes at 9:48, because 3 lost battalions cost no upkeep and no materiel goes into Dalles buildings.
- **Wasco is faster:** the seat falls at 3:53 (v0.3: 5:30), because the road is 21.8 miles and not 30. Roosevelt: 6:44 (6:30).
- **The Compact's manpower is tighter in minutes 2 to 5** (Normal 67 → 37; v0.3 was 56 to 66). Its manpower at 10:00 is higher (105/min, with The Dalles).

### 7e. Condon: supply hops and timing

Run `python3 tools/supply.py` and `python3 tools/econ.py --condon`. Rules: HQ range 40 road-miles, depot range 30 (kept), and from v0.5 one depot per node, 5 road-mile spacing, owned territory and connected at execute. **v0.5 re-run: the timeline below is unchanged.** Distances are identical, the relay node (368) is still Wasco territory and legal, and the Remnant's fuel and materiel curves did not move. **v0.6 re-run on the corrected map (node territory now read from `RNOD.territoryId`): still unchanged.** Node 368 is Wasco (`RNOD.territoryId` 3), node 360 is Wasco and node 359 is Condon. The relay works with one depot at node 368.

- **Route.** Goldendale → Condon is 62.6 road-miles, and the route runs through Wasco (21.8 + 40.8 = 62.6 exactly). The Dalles → Condon is 69.0, and that route also runs through Wasco (28.1 + 40.8). So "via The Dalles" is just a 6.3-mile detour that joins the Wasco road, and a depot at The Dalles covers only 30 of its 69.0 miles.
- **Seat-town depots alone can never supply Condon.** No seat lies within 30 road-miles of Condon (nearest: Arlington 37.5, Wasco 40.8). A depot at the Wasco seat stops 10.8 miles short, and adding more seat depots does not help.
- **What works: HQ + 1 road-node depot (one depot hop).** `supply_depot.placement` is `town_slot_or_road_node`, and under the v0.5 rules (section 2) the node must be in a territory the Remnant owns and connected to its supply. On the Wasco–Heppner Highway (OR-206), the v1 map has these road nodes inside HQ range (≤ 40 mi from Goldendale) and within 30 mi of Condon. The route by edge territory is Goldendale 0–12.0 mi, Wasco 12.0–36.7 and Condon 36.7–62.6 (v0.6 map; v0.5 printed 36.8); the Sam Hill Memorial Bridge is at 12.4 mi.
  - **node 368:** 34.0 mi from Goldendale, 28.6 to Condon, Wasco territory. **Legal. This is the relay site.**
  - **node 360** (Wasco-side bridge head of the OR-206 John Day River bridge, `BRDG` key 2305843009218777746): 36.7 mi from Goldendale, 25.9 to Condon, Wasco territory. It is legal on its own, but it is only 2.7 road-mi from node 368, so **only one of 368 and 360 can hold a depot** (5-mile spacing).
  - **node 359** (the Condon-side bridge head, chokepoint `CHOK` key 2310346608846148242): 36.8 mi from Goldendale, 25.8 to Condon. **Changed in v0.5: under the v1 river-bank rule this node is in Condon territory** (its 1-mile cell is still Wasco). The v1.2 map's own `RNOD.territoryId` agrees (v0.6). The Remnant cannot build there before it owns Condon.

  The usable window on Wasco nodes is now 32.6 to 36.7 road-miles from Goldendale (v0.4: to 36.8). A depot needs Wasco owned and the node inside the Goldendale HQ trace, both of which hold from the Wasco seat capture at 3:53.
- **Condon relay under the v0.5 depot rules** (unchanged in v0.6: spacing 804,670 cm, 1 per node, owned and connected node; `python3 tools/supply.py`, `DepotBoard`). **It still works on the corrected map.**
  - A depot at node 368 is legal when the engineers arrive: Wasco is owned, the node is 34.0 mi from the HQ (inside the 40-mile trace), and there is no other depot within 5 road-miles.
  - A second depot on node 368 is rejected (`maxDepotsPerNode`). A second one at node 360 is rejected by spacing (2.7 mi). Node 359 is rejected because it is Condon territory.
  - The optional chain (Wasco seat depot, then node 368) is legal, because they are 12.2 road-mi apart. A Compact depot at The Dalles seat does not block node 368: they are 40.3 road-mi apart by road.
  - Only Wasco and Condon territory lie within 5 road-mi of node 368 (the nearest Condon node is 2.8 mi away). So a rival depot could deny the site only from Condon territory, which means the enemy would have to own Condon and have it connected to its supply first. That does not happen in the reference.
  - Once the Remnant owns Condon, its later depots inside Condon must also be ≥ 5 road-mi from node 368.
- **Longer chains also work:** HQ → Wasco-seat depot → OR-206 depot → Condon is 2 depots ("3 hops"). But the Wasco depot is redundant, because the HQ already covers that stretch. The Dalles' depot adds nothing toward Condon. **So Condon needs one depot, fewer than the three hops the design allows.** With depot range kept at 30, Condon depends entirely on road-node placement: if depots were town-slot only, Condon would be unreachable.
- **Earliest realistic timing** (on the reference build; engineers are tracked, and the depot must wait for 50 fuel / 200 materiel):

| Step | Reference (Wasco rifle drives on) | Fast (rifle 4 skips Roosevelt) |
|---|---|---|
| Engineers leave Goldendale (after home salvage yard) | 0:41 | 0:41 |
| Engineers at node 368 (34.0 mi) | 6:41 | 6:41 |
| Depot done (queue waits for 200 materiel) | 8:52 | 8:52 |
| Motor rifles leave for Condon | 6:11 from Wasco (40.8 mi) | 1:01 from Goldendale (62.6 mi) |
| Arrive Condon | 11:34 | 9:15* |
| Garrison (1 militia) cleared | 12:44 | 10:25 |
| **Condon seat captured** | **13:44** | **11:25** |
| Engineers reach Condon (28.6 mi from depot) | 13:56 | 13:56 |
| Fuel pumps (2 × 150 materiel) | 14:37, 15:17 | 14:37, 15:17 |

\*The fast rifle leaves HQ supply at about 6:30 and runs 2:20 ahead of the depot, so it is slowed (700‰) and cannot refuel until 8:52. Treat 11:25 as a lower bound.

The realistic answer is **capture at about 13:45, with +60 fuel/min flowing from about 15:15**. That is inside the intended 12 to 18 minute window, and the Remnant fuel stock then climbs from 8 at 11:00 to 566 at 20:00.
- **Raidability.** The whole operation hangs on one road: the Sam Hill Memorial Bridge (a main-river bridge chokepoint, `BRDG` key 2305843010751527551, `CHOK` key 2310346610378898047) to Biggs, then OR-206. The depot is a 2,000 hp structure 34 miles out, and the Compact's HQ reaches The Dalles (21.2 mi), 28.1 road-miles from Wasco. If the Compact holds The Dalles (reference outcome), a Compact depot there covers Wasco (28.1). That makes the Condon line very raidable, as intended, but it is shallower (1 depot) than the design brief assumed.

### 7f. Is Hood River inside Remnant HQ supply? (v0.6: still no)

**No.** Goldendale → Hood River is 44.4 road-miles (Centerville Highway to WA-14 to the Hood River Bridge), 4.4 beyond the 40-mile HQ range. It is also the same 44.4 in the other direction, so the Compact HQ does not reach Goldendale either.
- **v0.6: territories 11 and 6 are no longer adjacent.** On the corrected map, Goldendale's neighbours are Arlington, Outlook, Roosevelt, The Dalles, Trout Lake, Wasco and White Salmon, and Hood River's are Cascade Locks, Maupin, The Dalles and White Salmon. The homes share no border, and The Dalles and White Salmon are the only territories next to both. (v0.5 listed 11 and 6 as adjacent across the Columbia. Adjacency never meant supply, so nothing numeric changes.) The White Salmon seat, at the Washington end of the bridge, is 41.8 and also outside.
- The HQ trace's nearest in-range node to the Hood River seat (node 743, 37.1 road-mi) is in **White Salmon** territory (`RNOD.territoryId` 9, a chokepoint node).
- The HQ trace's nearest in-range road node is 5.3 miles in a straight line from the Hood River seat. That is more than the 1.5-mile town zone plus the 2-mile unit link, so **Remnant units fighting in Hood River's capture zone are out of HQ supply**. After the 60 s grace they fire at half rate, move at 700‰, and cannot refuel, rearm or reinforce (section 6d: a cut turns a winning 2× assault into a loss).
- A Remnant assault on Hood River needs a forward depot first. The Dalles (21.2 mi) or White Salmon (2.6 mi) both put Hood River well inside depot range.
- v0.6, `tools/supply.py` under the depot rules (owned, connected to the Goldendale HQ trace, 1 per node, spacing):
  - With White Salmon owned, 22 legal nodes cover the Hood River seat. The nearest to the HQ is node 963, a chokepoint node 17.9 road-mi from Goldendale and 29.7 to Hood River. The corrected White Salmon territory now reaches east to Klickitat (up to the blocking Klickitat River), close to Goldendale.
  - With The Dalles owned, 61 legal nodes cover it. The nearest is node 614, 25.4 road-mi from Goldendale and 28.6 to Hood River.
  - Owning White Salmon still means taking its seat at 41.8 road-mi, outside HQ supply. **This makes The Dalles doubly important:** it is the Remnant's supply bridgehead toward Hood River, and the reference race loses it.
- Symmetrically, the Compact cannot assault Goldendale from its HQ alone.

### 7g. Key objectives on this layout

1. **The Dalles (#4): the prize and the hinge.**
   - It is the only neutral town: 24 + 16 + 2 (Celilo Village hamlet, v0.5) manpower/min, 4 fuel, and 60 materiel with a yard.
   - It is the I-84 / US-197 junction and the south end of The Dalles Bridge. It is 21.2 road-mi from Hood River and 32.8 from Goldendale.
   - The Compact now races for it and, in the reference, wins at 8:30 and walls it by about 9:45.
   - For the Remnant it is the only supply bridgehead toward Hood River (7f): Goldendale's HQ reaches The Dalles with 7.2 miles to spare, and a depot there covers Hood River.
2. **Condon (#2): the Remnant's fuel valve.**
   - Its 2 depots (60 fuel/min pumped) are the only depots the Remnant can reach without going through Hood River.
   - It needs one road-node depot on OR-206 inside Wasco territory (node 368; 7e), which is still legal under the v0.5 spacing and one-per-node rules. It captures at about 13:45 in the reference.
3. **Wasco (#3): the stepping stone and the second bridge.**
   - It is 4 cheap hamlets at the south end of the Sam Hill (US-97) bridge, 21.8 road-mi from Goldendale, and taken by 3:53 to 6:11.
   - Owning it is what permits the Condon depot. Cutting the Biggs junction or the bridge severs the Condon line.
4. **White Salmon (#9) and Cascade Locks (#5): the Compact's flanks.**
   - White Salmon is 2.6 mi from Hood River across the Hood River Bridge, and 41.8 from Goldendale. v0.6: it now borders The Dalles and no longer borders Cascade Locks, and it includes the Klickitat hamlet (21.3 road-mi from Goldendale).
   - Cascade Locks (19.6 mi on I-84) holds the Compact's second pair of depots.
   - The Compact takes both by about 7:00.

Secondary objectives:
- **Carson (#8):** 3 villages and 21 industrial areas; 7.9 mi from Cascade Locks, 19.5 from Hood River.
- **Maupin (#1):** 2 hamlets; 40.0 mi south of The Dalles.
- **Arlington (#7), Roosevelt (#10) and Outlook (#13):** low value, mostly map-fill. Roosevelt (39.6) is just inside Remnant HQ range.

## 8. Pop cap check

Cap = 30 + 1 per hamlet + 2 per village + 5 per town, hard cap 100.
- At the start, the Remnant has 37 (v0.4: 38; Lyle) and the Compact 39 (v0.4: 37; Rhododendron and Welches).
- At 10:00 in the reference runs, the Remnant has 42 (without The Dalles) and the Compact 51. In the commit variant they have 47 and 45 (v0.4: 43 / 48 and 49 / 43).
- Owning the whole map gives **85** (19 hamlets, 13 villages, 2 towns). v0.4 expected 86 "after the population fix", but that figure counted Lyle as a village.
- The hard cap of 100 is not reachable on this map.

## 9. Designer decisions needed

1. **HQ counts as a town for Tier II** (chosen, unchanged).
2. **Asymmetric HQ fuel** (Remnant 45, Compact 20), kept. With real distances the Remnant curve dips to 0 only for moments (≤ 2 s) before 10:00.
3. **Territory ownership = hold the seat** (`hold_seat`), unchanged.
4. **The Compact now races for The Dalles, and wins against the v0.3 player build.** It arrives 31 s earlier (21.2 vs 32.8 road-mi), and its third rifle decides the three-way fight. The Remnant keeps The Dalles only if the player commits a third rifle there instead of expanding. The Dalles is walled by about 9:45, and Remnant armour 1 could reach it at 9:19 if sent at 3:32. Decide whether this knife-edge is the intended opening, or whether to slow the Settler's rush (for example, rifles not first in its build) or give the Remnant a faster start.
5. **Condon needs only one depot** (on OR-206 in Wasco territory), with depot range kept at 30. It still lands at about 13:45, inside the 12 to 18 minute window. It relies on road-node depot placement: if depots become town-slot only, Condon is unreachable at range 30.
6. **Hood River is outside Remnant HQ supply** (44.4 > 40). A Remnant win needs a forward depot at The Dalles or White Salmon first. This is intended if The Dalles should be the gate, and it is symmetric.
7. **The Compact's early manpower is very tight** with the rifle-first rush. v0.5 minute stock is Normal 12 / 7 / 2 at 3:00 to 5:00 and Brutal 25. The two new home hamlets add 5 manpower/min, but the Settler spends it on earlier militia, AT and artillery, and tier II slips to 9:24 on Normal. If the Settler should also flood militia early, raise town manpower or give it White Salmon at start.
8. **Travel times still assume one surface value** (secondary paved), because the export is length only. The bake's `REDG.highwayClass` could give per-leg speeds if the pipeline exports travel cost.
9. **Welches is capturable as a hamlet by designer override** (population 250; decided for v0.5; from v0.6 the map bakes the 250 with the design-override flag, and the validator accepts that form). The map has no population for it, because it has no Census 2020 place of its own and sits inside the Mount Hood Villages CDP. The 250 is a design value, not a census figure, and is recorded as `populationSource: designer_override`. If the Map Pipeline ever ships a real (sourced) population, the validator will flag the override (a Census flag or a mismatched population), and the designer should then drop or keep it deliberately.
10. **Lyle counts as a hamlet** (population 499, hamlet band 50–499). v0.3 and v0.4 counted it as a village. v0.5 follows the tier rule, which costs the Remnant 6 manpower/min and 1 pop cap. Confirm this, or add a designer override if Lyle should play as a village.
11. **Depot spacing (5 road-miles, any owner) and one per node** (v0.5). The Condon relay survives with node 368. A first depot also denies sites around it to both sides, which makes an early forward depot a territorial move.
12. **Node territory is in the map file (resolved in v0.6).** The v1.2 `RNOD.territoryId` is read directly, and the build-output workaround is gone.
13. **The start territories are no longer adjacent** (v0.6, corrected map). Hood River and Goldendale share no border, and The Dalles and White Salmon are the only territories next to both. No numeric effect in the tools. If the AI's target list or a "neighbour" rule was meant to let the Settler or the player strike the enemy home territory directly, it now has to go through one of those two territories.

## 10. Known limits of this math (tune in playtest)

- Damage does not scale with remaining strength (`damageScalesWithStrength: false`).
- The reference battles ignore range geometry, forest, slope and fords.
- Artillery hits one target; there is no splash.
- Mines, barriers and bridges are not modelled.
- Task-force retreat is not modelled in battles; every battalion fights to the death.
- The economy model uses derived but fixed capture schedules. Reactions after the plan are not simulated: no counterattack on The Dalles, and no Compact reinforcement stream.
- Travel uses the seat-to-seat shortest road length at one surface value, with no climb cost and no out-of-supply slowdown. Inside a territory, a fixed 15 s hop is used.
- `race.py` puts every arrival in the open and the garrison in the urban holding. It uses the spec target rule with all sides mutually hostile. A later village clear uses fresh-hp survivors.
- Construction ignores engineer travel, except in the Condon timeline.
- Depot placement rules (v0.5) are checked against a fixed ownership snapshot in `tools/supply.py`. The supply network there is plain road distance from the HQ and own depots, and it ignores `edgeUnusableWhen` (enemy-controlled nodes and destroyed bridges) unless a caller passes blocked nodes.
