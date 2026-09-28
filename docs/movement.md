# Sim movement and pathfinding

Headless, integer-only, deterministic. Player UI preview and AI call the same functions. Unity NavMesh is presentation/authoring only.

## Tick and units

- 20 Hz (`SimConfig.TickRateHz`). Speeds in the tables are **cm per tick**.
- Positions: `int32` cm. Path costs and squared distances: `int64`.
- Ratios: parts-per-thousand (`SimConfig.PptScale` = 1000).
- No `float` / `double` in sim state, hashes, or path decisions. `Fixed64` is not used here; remaining square roots are integer (`Int128Math.IntegerSqrt`).

## Map blobs (WRMB)

Roads and terrain arrive as **two** WRMB files (same container; sections located by tag). See [formats/wrmb-v0.md](formats/wrmb-v0.md).

- Map/AI file: `RNOD` / `REDG`. Bridges are two `BridgeHead` nodes plus a `bridgeSpan` edge.
- Terrain file: `MVSL` (uint16 per-mille slope), `MVLC` (land cover), `MVFD` (ford bits). Row 0 is south. Sim x = east, y = north, origin SW.
- Cell size is `META.cellSizeCm`, divided by `moveCellsPerCell` when the movement grid is a subdivision of the mile grid (production maps: 18 cells/mile ≈ 89.4 m). The sim does not assume 100 m.

`MovementService.Create(mapBlob, terrainBlob, tables)` loads both. Synthetic tests emit WRMB fixtures so goldens stay in CI.

## Speed

Loaded from `movement-tables-v1.json` (strict integer JSON — no fractions, no exponents) at match load:

```
speedCmPerTick = floor(base * surfacePpt * slopePpt / 1_000_000)
```

Zero is impassable. Then a cell or edge costs

```
ticks = ceil(lengthCm / speedCmPerTick) = (lengthCm + speed - 1) / speed
```

Designer caps (off-road only; applied after the table lookup):

| Class | Max off-road grade (per-mille, inclusive) |
| --- | --- |
| Wheeled | 299 |
| Tracked | 599 |
| Foot | 999 |
| Air | ignored |

Roads **ignore slope** (`slopePpt` = 1000). Air **ignores terrain** (cover, slope, water). Cost grids are built at match load from the blob + tables so a JSON tweak needs no map rebake.

## Queries

`MovementService` exposes three searches, plus `EstimateTravelTicks(path, class)` (recosts the waypoints for that movement class with uniform road weights, or `-1` if unreachable / a step is impassable for the class):

1. **`PathOnRoads(fromNode, toNode, class, costWeights)`** — integer A* on the undirected road graph. `costWeights` are ppt multipliers per road tier, with a separate bridge-span ppt. Weighted step `max(1, ticks * weightPpt / 1000)` (weight 0 skips the edge).
2. **`PathOffRoad(fromCm, toCm, class)`** — 8-connected grid A* on the per-class cost grid. Diagonal cost is `orthoTicks * 1414 / 1000` (ppt √2). No corner-cutting: both adjacent orthogonal cells must be passable.
3. **`PathCombined`** — min of pure off-road and a hierarchical road trip (see below).

Stable A* ties: grid searches pop `(f, cellIndex)`. Road searches pop `(f, h, nodeIndex)` so an exact ALT plateau expands toward the goal instead of by node id (the 90×90 lattice would otherwise close every node before the highest-index corner). Equal `g` keeps the parent with the smaller index. Open/closed storage is arrays + a generation counter, never `HashSet` enumeration.

Heuristics are admissible: octile-cm / maxSpeed on the grid; on roads, `max(Euclidean-cm / maxRoadSpeed, ALT)` with ALT scaled by `minWeightPpt / 1000` when that min is below 1000. ALT landmarks are Dijkstra distances on the **uncut** graph at ppt=1000, so they stay admissible after a span cut (true distances only grow) and after weights ≥ 1000.

## Off-road: octile fast path + HPA*

Maps that fit in one HPA* cluster (`width` and `height` ≤ `ClusterSize`, default 32) never build a hierarchy. Those queries — including the 11×11 hill goldens — stay exact 8-connected A*, so frozen hashes do not change.

Larger maps, per movement class, at match load:

1. Partition the cost grid into `ClusterSize × ClusterSize` clusters.
2. Place border **entrances** at the first, last, and midpoint cell of each contiguous passable orthogonal run, plus extra samples every `DefaultEntranceInterval` (8) cells. Corners stay in the abstract graph so 8-connected cluster hops work.
3. Intra-cluster edges: Dijkstra between that cluster's entrances (same 8-connected, no-corner-cut rule as query A*).
4. Inter-cluster edges: 8-connected steps between entrance cells in neighboring clusters.

Query:

1. If the canonical octile line (diagonal until aligned, then orthogonal) is fully passable, walk it. On uniform 8-connected cost that walk is optimal. Obstacle / cost wells fail the walk and fall through.
2. Otherwise HPA*: Dijkstra-fill the start and goal clusters, A* on the abstract graph (virtual start/goal attached to cluster entrances, plus a direct start–goal edge when they share a cluster), then refine each hop with octile-or-local-A*.
3. Integer costs, deterministic ties. Not globally optimal once a query leaves a single cluster.

**Observed suboptimality vs exact A*** on the 96×96 walled golden (cluster 32 vs cluster 96, four queries including the gap detour): HPA* cost / exact cost **≤ 1.15**. Open-field octile matches exact cost.

## On-road: ALT landmarks

At match load, 16 farthest-from-farthest landmarks per class store Dijkstra tick distances on the uncut graph. Query A* uses `max(euclid, ALT)`. Edge ticks are precomputed at match load (`ceil(length / speed)`).

## Time-slicing

`ProcessQueue` spends **work units in sim state**, never wall clock:

- `ExpansionsPerTick` (default 4096) is the cap on closed vertices (plus octile cells, which count as one unit each).
- `PathRequestsPerTick` (default 8) is the cap on **completions**. Cache hits do not consume either budget.
- A search that exhausts the expansion cap leaves an in-flight heap/HPA phase in the service and resumes next `ProcessQueue`. `PathResult.CommitTick` is the tick it finishes (`ProcessQueue` increments `Tick` from 0). Commit tick is **not** in the xxHash payload.
- Direct `Path*` methods still run to completion (tests / UI preview) and do not debit the tick budgets.
- Callers keep the previously committed path until `TryGetResult` succeeds — the AI doc's "squads hold the last path" rule.

`CutSpan` still bumps `graphVersion`, clears the cache, and aborts an in-flight road/combined search back onto the FIFO head.

## Hierarchical combined query

A 200-mile map cannot run a unified A* over every cell plus every road edge as first-class neighbors without blowing the per-tick budget. Combined search therefore:

1. Runs off-road (`PathOffRoad`: octile / HPA* / exact A*) start → goal (may be unreachable, e.g. wheeled vs a ridge).
2. Picks **gateways**: road nodes whose cell is passable. If `NodeCount ≤ 4 * MaxGateways` (default 8), **every** node is a candidate — synthetic tests are exact. Otherwise the `MaxGateways` nearest to start and to goal by `(distSq, nodeId)`.
3. For each `(entry, exit)` pair in `(entryId, exitId)` order, sums `grid(start, entryCell) + PathOnRoads(entry, exit) + grid(exitCell, goal)`, skipping unreachable legs.
4. Returns the cheaper of (1) and (3). Equal cost prefers **off-road** (fewer systems in the path; stable).

This is not globally optimal when the cheapest on-ramp is not among the K nearest geometrically (a ridge that forces a far on-ramp). The full-candidate rule covers small graphs; large-map misses of that shape are the accepted cost of the budget. Revisit with true hierarchical contraction if a mode needs optimality.

Road travel **does not** pay the terrain cells under the polyline: the unit is on the graph while the span is uncut. That is why a truck can take a switchback that is authored across steep cells.

## Micro-benchmark (not a CI gate)

`dotnet run -c Release --project src/Wasteland.Sim.Benchmarks` on the Cloud Agent VM (.NET 8.0.425, linux-x64). Load times include per-class HPA* graphs and road ALT. Memory figures are **per movement class** except scratch (shared).

| Workload | Result |
| --- | --- |
| 90×90 road graph (8,100 nodes, 16,020 edges) load | 240 ms; 16 landmarks, 1.0 MB/class |
| First `PathOnRoads` corner-to-corner | 3 ms, 179 expansions |
| 40 unique `PathOnRoads` | **0.45 ms/query**, 112 expansions/query (was 2.10 ms) |
| 1600×1600 terrain (2.56M cells) load | 24 s; 2,500 clusters of 32; 39,396 abstract nodes / 641,804 directed edges / class |
| 1600×1600 memory/class | cost grid 9.8 MB, hierarchy 6.0 MB, scratch 0.8 MB shared |
| Foot corner-to-corner `PathOffRoad` | **2 ms**, 1,600 waypoints, 1,599 expansions (was 22 ms) |
| 8 unique wheeled `PathOffRoad` 1600 | **0.40 ms/query**, 1,067 expansions/query (was 32.64 ms) |
| 1600 sliced corner (`ExpansionsPerTick` 4096) | 1 tick, max 1,599 expansions, worst tick 1 ms |
| 3200×3200 (200-mile @ 100 m, 10.24M cells) load | 106 s; 10,000 clusters; 158,796 abstract nodes / 2,603,604 directed edges / class |
| 3200×3200 memory/class | cost grid 39.1 MB, hierarchy 24.2 MB, scratch 3.0 MB shared |
| 8 unique wheeled `PathOffRoad` 3200 | **0.62 ms/query**, 2,132 expansions/query |
| 3200 sliced corner | 1 tick, max 3,199 expansions (cap 4,096), worst tick < 1 ms |

Targets: amortized **< 1 ms** per off-road request on 1,600×1,600 (**met**, 0.40 ms) and **< 0.2 ms** per road request on the 8,100-node mesh (**0.45 ms** — about 5× faster than 2.10 ms; remaining cost is reconstruct+hash of ~100-node paths after ALT already cuts expansions to ~112). Per-tick path work is bounded by `ExpansionsPerTick` (measured max 3,199 ≤ 4,096 on 3200, worst tick 1 ms).

Re-run locally after table or heuristic changes; do not treat these numbers as a pass/fail gate.

## Budget, queue, cache

- `PathRequestsPerTick` (default 8) is a match-wide cap on **completed** cache-missing searches. `ExpansionsPerTick` (default 4096) is the work-unit cap (see Time-slicing).
- `Enqueue` stamps a monotonic request id. Cache hits resolve immediately and do not consume either budget. Misses sit in a FIFO `List` (enqueue order = process order; callers must submit in sim-deterministic order, typically command-queue order).
- `ProcessQueue()` spends the expansion budget, possibly resuming one in-flight search, then starts further FIFO misses until either cap is hit. Direct `Path*` methods exist for tests and UI preview; they use the same cache and searches but do not debit the tick budgets.
- Cache key: `(kind, class, from, to, weightKey, graphVersion)`. Off-road keys pin `graphVersion` to 0 so a bridge cut does not flush grid paths. `CutSpan` increments `graphVersion` and clears keys that include it (the whole cache is dropped — `Dictionary.Clear`, no enumeration of results).

`from`/`to` for grid queries are **cell indices**, so any centimeters in the same cell share a result.

## Hashing

Each `PathResult` carries `xxHash64` over kind, cost, graph version, and every waypoint `(segmentKind, id, xCm, yCm)`. **`CommitTick` is omitted** so a sliced vs synchronous result hashes the same. Goldens freeze that hash (`truck=0x2B0B078CCC489F33`, `infantry=0x9AFC305C35AF54D4`); they are unchanged because the 11×11 hill map fits in one cluster and still uses exact A*.

## What this service does not do

It does not steer squads. `SimWorld` still integrates Move as a straight cm line. Wiring `Move` to follow a `PathResult` is a later PR so existing command-log hashes stay intact until that integration is intentional.
