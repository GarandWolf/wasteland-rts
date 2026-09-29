# Sim movement and pathfinding

Headless, integer-only, deterministic. Player UI preview and AI call the same functions. Unity NavMesh is presentation/authoring only.

## Tick and units

- 20 Hz (`SimConfig.TickRateHz`). Speeds in the tables are **cm per tick**.
- Positions: `int32` cm. Path costs and squared distances: `int64`.
- Ratios: parts-per-thousand (`SimConfig.PptScale` = 1000).
- No `float` / `double` in sim state, hashes, or path decisions. `Fixed64` is not used here; remaining square roots are integer (`Int128Math.IntegerSqrt`).

## Map blobs (WRMB)

Roads and terrain arrive as **two** WRMB files (same container; sections located by tag). See [formats/wrmb-v0.md](formats/wrmb-v0.md). The loader accepts formatVersion 0 and 1 (v1 requires `V1_FINAL` and, on terrain files, `WTRA`, which the sim skips). Map/balance identity: [formats/map-keys.md](formats/map-keys.md).

- Map/AI file: `RNOD` / `REDG`. Bridges are two `BridgeHead` nodes plus a `bridgeSpan` edge.
- Terrain file: `MVSL` (uint16 per-mille slope), `MVLC` (land cover), `MVFD` (ford bits). Row 0 is south. Sim x = east, y = north, origin SW.
- Cell size is `META.cellSizeCm`, divided by `moveCellsPerCell` when the movement grid is a subdivision of the mile grid (production maps: 18 cells/mile ≈ 89.4 m). The sim does not assume 100 m.

`MovementService.Create(mapBlob, terrainBlob, tables)` loads both. Synthetic tests emit WRMB fixtures for the 11×11 hill goldens. `GoldenMapTests` loads `data/golden/golden-ci-columbia-gorge-100` (Git LFS, 1800×1800, 8940 cm cells) and pins road/off-road path hashes.

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

Larger maps, per movement class, at match load (pure function of that class's cost grid, `ClusterSize`, and `HierarchyAlgorithmVersion`):

1. **Skip the hierarchy** when every cell has the same tick cost (Air always; open field for every class). Canonical octile is then exact, so the abstract graph is wasted work.
2. Partition the cost grid into `ClusterSize × ClusterSize` clusters.
3. Place border **entrances** at the first, last, and midpoint cell of each contiguous passable orthogonal run, plus extra samples every `DefaultEntranceInterval` (8) cells. Corners stay in the abstract graph so 8-connected cluster hops work.
4. Intra-cluster edges: one closed-set Dijkstra per entrance over **that cluster only** (scratch reused, last source skipped because CSR is undirected), or octile arithmetic when the cluster itself is uniform-cost and fully passable.
5. Inter-cluster edges: 8-connected steps between entrance cells in neighboring clusters.

Clusters are built with `Parallel.For` (deterministic CSR sort afterwards). Classes run in parallel only when `BuildWorkers >= ClassCount * 2` so a 4-core VM spends all cores inside one class rather than starving cluster work. Serial vs parallel fingerprints are tested (`workers=1` vs `workers=8`).

Optional **WHPA** on-disk cache (`MovementServiceConfig.HierarchyCacheDirectory`): keyed by terrain `terrainPayloadSha256` (MVSL|MVLC|MVFD|MVEL, **not** the file `contentHash`, so config or provenance-only rebuilds stay valid), movement-table xxHash64, algorithm version, cluster size, and `W×H`. Payload SHA-256 is checked on load; a mismatch rebuilds. Repeat loads of the same map are then dominated by cost-grid fill + ALT, not HPA*.

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
- Callers keep the previously committed path until `TryGetResult` succeeds — the AI doc's "battalions hold the last path" rule.

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

`dotnet run -c Release --project src/Wasteland.Sim.Benchmarks` on the Cloud Agent VM (.NET 8.0.425, linux-x64, 4 cores). Road mesh still uses 10,000 cm synthetic cells. Grid load uses **production `cellSizeCm` = 8941** (~89.4 m) on mixed cover (16-cell field/forest bands so every 32-cluster has two costs, plus a 2-cell water river with fords). That forces HPA* for wheeled/tracked/foot; Air is uniform and skips. Memory: per-class estimates plus process working set / GC heap after `GC.Collect`.

Load-time targets (all movement classes, cold, no cache): **< ~5 s at 1800×1800** (100 mi) and **< ~20 s at 3600×3600** (200 mi). Two Release runs on this VM: **4.8–5.3 s** and **19.1–20.5 s**. WHPA cache reload is **0.14–0.18 s** / **0.49–0.54 s**.

| Workload | Result |
| --- | --- |
| 90×90 road graph (8,100 nodes, 16,020 edges) load | 149–167 ms; 16 landmarks, 1.0 MB/class; HPA* skipped (uniform field) |
| First `PathOnRoads` corner-to-corner | 3 ms, 179 expansions |
| 40 unique `PathOnRoads` | **0.42–0.52 ms/query**, 112 expansions/query (was 2.10 ms) |
| 1800×1800 mixed (3.24M cells, 8941 cm) cold load | **4.8–5.3 s** (cost grid 21–31 ms, HPA* 4.7–5.2 s); 3,249 clusters of 32; 51,296 abs nodes / 837,888 directed edges / class (3 ground classes; Air skipped) |
| 1800×1800 memory | 12.4 MB cost grid / class, 7.8 MB hierarchy / class, 1.0 MB scratch shared; live ≈ 82 MB; GC heap 105 MB; RSS 308–335 MB |
| 1800×1800 WHPA cache reload | **136–175 ms**, hash-verified hit |
| 1800 sliced foot corner (`ExpansionsPerTick` 4096) | 9 ticks, max 4,096 expansions/tick, worst tick 2–3 ms |
| 3600×3600 mixed (12.96M cells, 8941 cm) cold load | **19.1–20.5 s** (cost grid 97–295 ms, HPA* 18.8–19.6 s); 12,769 clusters; 202,720 abs nodes / 3,322,586 directed edges / class |
| 3600×3600 memory | 49.4 MB cost grid / class, 30.9 MB hierarchy / class, 3.9 MB scratch shared; live ≈ 325 MB; GC heap 417 MB; RSS 1.3–1.4 GB (process does not return the 1800 heap to the OS) |
| 3600×3600 WHPA cache reload | **492–543 ms**, hash-verified hit |
| 3600 sliced foot corner | 33 ticks, max 4,096 expansions/tick, worst tick 8–9 ms |

Query-time on **open field** (previous pass; mixed-cover queries cannot use the octile line, so they are slower and not the gate):

| Workload | Result |
| --- | --- |
| 8 unique wheeled `PathOffRoad` 1600 open field | **0.40 ms/query**, 1,067 expansions/query (was 32.64 ms) |
| 1600 sliced corner (`ExpansionsPerTick` 4096) | 1 tick, max 1,599 expansions, worst tick 1 ms |
| 8 unique wheeled `PathOffRoad` 3200 open field | **0.62 ms/query**, 2,132 expansions/query |
| 3200 sliced corner | 1 tick, max 3,199 expansions (cap 4,096), worst tick < 1 ms |

Targets: amortized **< 1 ms** per off-road request on open 1,600×1,600 (**met**, 0.40 ms) and **< 0.2 ms** per road request (**0.42–0.52 ms** — accepted given the expansion cap). Per-tick path work is bounded by `ExpansionsPerTick`. Match load replaces the old 24 s/class @ 1600 and 106 s/class @ 3200 open-field Dijkstra.

Re-run locally after table or heuristic changes; do not treat these numbers as a pass/fail gate. Bump `HierarchyAlgorithmVersion` if entrance placement, intra costs, or CSR order change — the WHPA cache key includes it.

## Budget, queue, cache

- `PathRequestsPerTick` (default 8) is a match-wide cap on **completed** cache-missing searches. `ExpansionsPerTick` (default 4096) is the work-unit cap (see Time-slicing).
- `Enqueue` stamps a monotonic request id. Cache hits resolve immediately and do not consume either budget. Misses sit in a FIFO `List` (enqueue order = process order; callers must submit in sim-deterministic order, typically command-queue order).
- `ProcessQueue()` spends the expansion budget, possibly resuming one in-flight search, then starts further FIFO misses until either cap is hit. Direct `Path*` methods exist for tests and UI preview; they use the same cache and searches but do not debit the tick budgets.
- Cache key: `(kind, class, from, to, weightKey, graphVersion)`. Off-road keys pin `graphVersion` to 0 so a bridge cut does not flush grid paths. `CutSpan` increments `graphVersion` and clears keys that include it (the whole cache is dropped — `Dictionary.Clear`, no enumeration of results).

`from`/`to` for grid queries are **cell indices**, so any centimeters in the same cell share a result.

## Hashing

Each `PathResult` carries `xxHash64` over kind, cost, graph version, and every waypoint `(segmentKind, id, xCm, yCm)`. **`CommitTick` is omitted** so a sliced vs synchronous result hashes the same. Hill goldens freeze that hash (`truck=0x2B0B078CCC489F33`, `infantry=0x9AFC305C35AF54D4`); they are unchanged because the 11×11 hill map fits in one cluster and still uses exact A*. Columbia Gorge pins (`GoldenMapTests`) are a separate set: road wheeled `0xED55C547CFC83883`, road foot `0x6AC2AC8318785748`, off-road foot `0xAEA7ED93CEECF776`, off-road wheeled `0x327FA8A3742DE14F`.

## What this service does not do

It does not steer battalions. `SimWorld` still integrates Move as a straight cm line. Wiring `Move` to follow a `PathResult` is a later PR so existing command-log hashes stay intact until that integration is intentional.
