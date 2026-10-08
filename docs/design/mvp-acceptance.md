# MVP acceptance cases: win/loss and supply depots

Owner: Game Designer. Status: draft for the Lead's sim tests. Design only; nothing here changes data or code.

- **Source:** Designer MVP rules sheet (draft Oct 8 2026): §2 Win / lose, §3 and §3a buildings, supply and MVP defaults, §4 scenario keys, §5 timeline.
- **Data baseline:** main `b9e53fc`, `dataVersion` 8. Scenario `data/balance/scenarios/columbia_gorge_100mi_mvp.json` on map `golden_ci_columbia_gorge_100_v1_marsh` (`mapFileSha256` `76a801a3…107f`).
- **Units:** 20 ticks per second (`rules.json sim.ticksPerSecond`). Distances are cm. 1 road-mile = 160934.4 cm.
- Every number below comes from the rules sheet, from main's data files, or from the pinned marsh map read with `tools/map/wrmb_reader.py`. Road distances are shortest paths over `REDG.lengthCm`. Anything not settled is in **Open questions** at the end rather than guessed.
- Assert against the `rules.json` values, not against hardcoded constants.

## 0. Fixture facts (MVP scenario + marsh map)

| | Remnant (seat slot 0, player) | Compact (seat slot 1, AI `homestead_compact_settler`) | The Dalles (neutral prize) |
|---|---|---|---|
| `homeTerritoryKey` | `0x1000000008FF3E87` | `0x1000000008FFA335` | `0x10000000B3787512` (`neutralPrizeTerritoryKey`) |
| TERR id / name | 11 Goldendale | 6 Hood River | 4 The Dalles |
| Seat town (TOWN id, population) | 33 Goldendale, 3435 | 26 Hood River, 7624 | 14 The Dalles, 15340 |
| Seat tier (`economy.json holdingTiers`) | village (500–4999) | town (5000–49999) | town |
| Seat town road node | 953 | 769 | 499 |
| `startingBuildings` | hq, muster_camp, heavy_works, **town_walls_l1**, salvage_yard | hq, muster_camp, **town_walls_l1**, salvage_yard | – |
| `buildableBuildings` | `["supply_depot"]` | `["supply_depot"]` | – |
| Builder in `raisableUnits` | `remnant_engineers` (`canBuild` true) | `compact_engineers` (`canBuild` true) | – |

`matchRules`: `victory` `"capture_enemy_hq_territory"`, `territoryOwnershipRule` `"hold_seat"`, `simultaneousHqLoss` `"draw"`, `fuelEnabled` false.

Seat-to-seat road distances on the pinned marsh map (Map Pipeline output, cheapest path over the road graph):

| Route | Road-mi |
|---|---|
| Goldendale – The Dalles | 33.0 |
| Hood River – The Dalles | 21.2 |
| Goldendale – Hood River | 44.4 |

The node-to-node distances in §3 are shortest paths by `REDG.lengthCm` between the named road nodes. On that measure Goldendale node 953 – The Dalles node 499 is 5272961 cm (32.8 road-mi), which is also the value in `territory-roads.v1-marsh.json` and the designer sim; see open question 9.

A seat's **HQ holding** is the seat place of its `homeTerritoryKey` (Goldendale town 33 for the Remnant, Hood River town 26 for the Compact).

## 1. Win, loss and draw (§2)

The HQ check is the final phase of `Step(t)`, after capture resolution. At the end of tick *t* every seat is checked for whether it still owns its HQ holding.

| Case | Setup | Expected |
|---|---|---|
| **W1 HQ capture is a win** | Remnant battalions with `canCapture` complete the capture of Hood River (town 26) at tick *t*. Goldendale is still Remnant-owned. | At end of *t*: result = Remnant **win**, Compact **loss**. Game over at *t*. |
| **W2 Own HQ captured is a loss** | Compact battalions complete the capture of Goldendale (town 33) at tick *t*. Hood River is still Compact-owned. | At end of *t*: Remnant **loss**, Compact **win**. Game over at *t*. |
| **W3 Same-tick draw** | Both HQ captures complete on the same tick *t*. | Result = **draw** (`simultaneousHqLoss` `"draw"`). Game over at *t*. |
| **W3b Draw is order-independent** | Repeat W3 with the two captures resolved in the opposite order inside tick *t* (reverse seat iteration and capture iteration order). | Still **draw**, identical result object both times. The result never depends on seat order or capture iteration order. |
| **W4 One tick apart** | Hood River falls at *t*; Goldendale would fall at *t*+1. | Remnant **win** at *t*. The game is over, so the capture at *t*+1 never resolves. |
| **W5 No timer** | No HQ changes hands. Run for any long horizon, for example 2 × 25020 = 50040 ticks (twice the §5 earliest-win estimate). | No result. There is no time limit, score or tiebreak. |
| **W6 Other captures never end the game** | The Compact captures The Dalles (territory 4, the neutral prize) at tick *t*; or any non-HQ territory changes hands. | No result. Only a seat's HQ holding counts. |
| **W7 End of game is final** | Any of W1–W4 ends the game at tick *t*, with commands already scheduled for *t*+1 and later. | Those commands are discarded. The state hash at *t* is the final hash. |
| **W8 End screen** | W3. | The end screen shows a **Draw** state (UI). |

## 2. HQ capture timers (§2, `rules.json capture`)

Formula: `ticks = timerTicksByTier[seatTier] × (1000 + min(L × fortificationTimerPerLevelPpt, fortificationTimerMaxPpt)) / 1000`, with `fortificationTimerPerLevelPpt` 250 and `fortificationTimerMaxPpt` 500. Both HQs start with `town_walls_l1` (`fortificationLevel` 1), so L = 1.

| Case | Seat tier | `timerTicksByTier` | L | Expected capture time |
|---|---|---|---|---|
| **C1 Goldendale (Remnant HQ)** | village | 1200 | 1 | 1200 × 1250 / 1000 = **1500 ticks (75 s)** |
| **C2 Hood River (Compact HQ)** | town | 2400 | 1 | 2400 × 1250 / 1000 = **3000 ticks (150 s)** |

Timer behaviour, unchanged from `rules.json capture`:

- **C3** The timer runs only while capturer units with `canCapture` are inside the zone and no enemy combat unit is in it (`requiresZoneClearOfEnemyCombatUnits` true). Zone radius: village 160934 cm, town 241402 cm.
- **C4** Contest pauses the timer and does not reset it (`onContest` `"pause"`, `resetOnContest` false). Example: Remnant capturers are in the Hood River zone, uncontested, for 2000 ticks; a Compact combat unit enters for 400 ticks and then dies. The timer resumes with 1000 ticks left, so the capture completes 3400 ticks after the timer started.
- **C5** If the capturer leaves, the timer pauses. After 1200 ticks (`decayStartsAfterTicks`) it decays at 1 tick per tick (`decayTicksPerTick`).
- **C6** Engineers can neither capture nor contest (`nonCombatUnitsCanCaptureOrContest` false; `canCapture` and `canContest` false on both engineer units). Engineers alone in the zone: no progress, and they do not pause an enemy timer.
- **C7** Walls stay with the town on capture and serve the new owner, both for the timer and for the +200 defence (`garrisonDefensePpt` 200) (§3a).
- **C8** Defenders in a walled HQ get +200 defence (`perFortificationLevel`).

## 3. BuildDepot (§3, §3a, `rules.json supply`)

`BuildDepot` is the only build command in the MVP; `seats[].buildableBuildings` is `["supply_depot"]` for both seats. The builder is a `canBuild` unit (the engineers).

Rules under test (`rules.json supply` unless noted):

| Rule | Value |
|---|---|
| HQ supply range | `hqRangeCm` 6437376 (40.0 road-mi) |
| Depot supply range | `depotRangeCm` 4828032 (30.0 road-mi); `supply_depot.supplyRangeCm` 4828032 |
| Placement node | `depotPlacementNode` `"any_road_node_in_owned_territory"` |
| Must connect to own supply | `depotPlacementRequiresConnectedToOwnSupply` true: road distance to own HQ ≤ `hqRangeCm`, or to one of own depots ≤ `depotRangeCm` |
| Spacing | `depotMinSpacingCm` 804670 (5 road-mi), `depotSpacingMeasure` `"road_graph_shortest_path"`, `depotSpacingCountsAllFactions` true. The HQ is not a depot and does not count (`balance-notes.md`) |
| Per node | `maxDepotsPerNode` 1, any owner |
| Per faction | `maxDepotsPerFaction` 16 |
| When checked | `depotPlacementCheckedAt` `"command_execute"` |
| Cost / time | `supply_depot` 0 manpower / 200 materiel, `buildTicks` 900 (45 s). Its `cost.fuel` 50 is ignored because `fuelEnabled` is false |
| Town slot | A road-node depot uses no town build slot; `buildSlotCost` 1 applies only to town-slot placement (§3a) |

Distances below are from the marsh map with every edge usable (no enemy-controlled node on the route; see `edgeUnusableWhen`).

| Case | Setup | Expected |
|---|---|---|
| **D1 Legal: The Dalles node 614** | Remnant owns The Dalles (territory 4). Remnant engineers issue `BuildDepot` at road node 614. No depot exists anywhere. | **Accepted.** Node 614 is 4094466 cm (25.4 road-mi) from the Goldendale HQ node 953, ≤ 6437376, so it is connected. Costs 200 materiel; completes 900 ticks after it starts. When complete, Hood River node 769 is 4599410 cm (28.6 road-mi) away, ≤ 4828032, so Hood River is inside Remnant supply. |
| **D2 Legal: White Salmon node 963** | Remnant owns White Salmon (territory 9). `BuildDepot` at node 963. No depots. | **Accepted.** 2885949 cm (17.9 road-mi) from node 953 (connected); 4785577 cm (29.7 road-mi) to node 769, ≤ 4828032. |
| **D3 Illegal: territory not owned** | The Dalles is neutral or Compact-owned. Remnant `BuildDepot` at node 614. | **Rejected** (node not in a territory the builder owns). No cost is charged (see open question 4). |
| **D4 Illegal: not connected to own supply** | Remnant owns Arlington (territory 7) and has no depots. `BuildDepot` at the Arlington seat town node 814. | **Rejected.** Node 814 is 7448013 cm (46.3 road-mi) from node 953, > 6437376, and no own depot is within 4828032. |
| **D5 Illegal: spacing, any owner** | A completed depot (owned by either seat) stands at node 614. Remnant owns The Dalles and issues `BuildDepot` at node 506 or node 609. | **Rejected.** Both are 800047 cm from node 614, < 804670. The same command at node 503 (838824 cm from 614) passes the spacing rule. Boundary: exactly 804670 passes ("at least"). |
| **D6 Spacing ignores the HQ** | `BuildDepot` at a legal node closer than 804670 cm to the builder's own HQ node. | Spacing passes; the HQ is not a depot. |
| **D7 Illegal: one per node** | A depot (either owner) stands at node 614. `BuildDepot` at node 614. | **Rejected** (`maxDepotsPerNode` 1). |
| **D8 Illegal: faction cap** | The builder already owns 16 depots. `BuildDepot` at an otherwise legal node. | **Rejected** (`maxDepotsPerFaction` 16). A 16th depot (15 owned) is accepted. |
| **D9 Checked at execute, not issue** | `BuildDepot` at node 614 is issued while legal; before it executes, the Remnant loses The Dalles, or another depot appears within 804670 cm. | **Rejected at execute.** Conversely, a command that would have been illegal at issue but is legal at execute is **accepted**. |
| **D10 Builder** | `BuildDepot` issued to a non-`canBuild` unit (any combat battalion, recon or scouts). | **Rejected.** Only `remnant_engineers` / `compact_engineers` build. |
| **D11 Nothing else is buildable** | Any attempt to build a building other than `supply_depot` (walls, salvage yard, muster camp, ...). | **Rejected** (`buildableBuildings` `["supply_depot"]`; no command exists for it). |
| **D12 Buildings are not targets** | Units in range of an enemy depot. | The depot is not a valid attack target (§3a, combat rule (c)). |

### 3.1 Forward depot is required to attack Hood River

| Case | Facts | Expected |
|---|---|---|
| **F1 Hood River is out of HQ supply** | Goldendale node 953 to Hood River node 769: 7140003 cm = **44.4 road-mi**, > `hqRangeCm` 6437376. | Remnant units at Hood River are out of supply (after `graceTicks` 1200) unless a Remnant depot covers them. **Intended** (§3). |
| **F2 Same for the Compact** | Hood River node 769 to Goldendale node 953: 7140003 cm, > 6437376. | The Compact HQ does not reach Goldendale either (§7). |
| **F3 Nearest legal forward depot sites** | Nodes inside HQ range of node 953 that also put node 769 inside `depotRangeCm`, nearest to the Remnant HQ in each territory: **node 614** in The Dalles (25.4 mi from HQ, 28.6 to Hood River) and **node 963** in White Salmon (17.9 / 29.7). | Matches the rules sheet (§3, from `supply.py`). The only other territory with such a node is Trout Lake (territory 12): node 1040 at 5778247 cm from HQ / 4264417 cm to Hood River. |

### 3.2 Depots on territory capture (§3a)

| Case | Setup | Expected |
|---|---|---|
| **K1 Captured territory loses its depots** | Remnant depot complete at node 614 (The Dalles). The Compact completes the capture of The Dalles at tick *t*. | Every depot on a road node in territory 4 is **destroyed** at *t*, whoever owns it. Hood River loses Remnant depot supply; Remnant units there go out of supply after `graceTicks` 1200 if no other source covers them. |
| **K2 Freed slot** | After K1. | Node 614 is free and the destroyed depot no longer counts for spacing or for the 16 cap (derived; see open question 3). |
| **K3 Walls stay** | Any territory capture where the town has walls. | Walls stay with the town and serve the new owner (§3a). |

## 4. Timing targets

The §5 timeline estimates live in `data/design/mvp_targets.json` (ticks, tolerance 300 = 15 s). `tools/check_mvp_timings.py` compares a headless run log against them. These are designer-sim estimates for the tuning pass, not acceptance asserts.

## Open questions

1. **Capture completion tick.** If the capture timer first runs on tick *a*, does the capture resolve on tick *a* + 1500 / *a* + 3000 or one tick earlier? Tests should pin whichever convention the sim picks.
2. **Absent-key defaults vs the loader.** Sheet §4 says absent `fuelEnabled`, `simultaneousHqLoss`, `raisableUnits`, `buildableBuildings` and `autoResearch` keep today's behaviour (defaults `true`, `"draw"`, every unit, every building, `[]`). Main's `ScenarioLoader` requires all five (`Require`), so `columbia_gorge_100mi_test.json`, which has none of them, would not load. Nothing on main loads the test scenario today. Is the test scenario retired, or should the loader apply the defaults?
3. **Depots under construction.** Does a depot still under construction count for spacing, the per-node limit and the 16 cap, and is it destroyed by a territory capture (K1)? Is a destroyed depot's slot freed at once (K2)?
4. **Cost on rejection.** Is the 200 materiel charged at issue or at execute, and is it refunded if `BuildDepot` is rejected at execute?
5. **Range boundary.** "Inside range" is assumed to be road distance ≤ range (as in `supply.py`). Spacing is ≥ 804670 to pass.
6. **Route usability.** All distances assume every edge is usable. With enemy-controlled nodes on the route (`edgeUnusableWhen`), connection and spacing distances can grow; tests should state the ownership of every node on the route.
7. **`compact_militia` target.** §5 gives "Compact militia / AT: 3:15 / 4:45" without saying which militia. In the designer sim (old Settler build order, rifles ×4 first) 3:15 is the second raised militia; the first is at 2:21. The real AI's build order differs from the designer sim (§6.5), so the Compact production targets are expected to move and are not marked `core`.
8. **Approximate targets.** The forward depot (about 10:39) and the HQ win (about 20:51, a lower bound against a passive Compact) are on the `remnant_commit` line only. The HQ win assumes the commit line, as in the sheet's `hq_win.py` run.
9. **Goldendale – The Dalles distance.** Map Pipeline gives 33.0 road-mi (cheapest path). The designer sim uses `seatRoadCm` 5272961 cm (32.8 road-mi) from `territory-roads.v1-marsh.json`, and the shortest path by `REDG.lengthCm` between seat nodes 953 and 499 gives the same. Hood River – The Dalles (21.2) and Goldendale – Hood River (44.4) agree. Which measure should the targets and tests use? No number here was changed for this.
