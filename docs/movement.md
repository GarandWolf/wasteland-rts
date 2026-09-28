# Sim movement and pathfinding

Headless, integer-only, deterministic. Player UI preview and AI call the same functions. Unity NavMesh is presentation/authoring only.

## Tick and units

- 20 Hz (`SimConfig.TickRateHz`). Speeds in the tables are **cm per tick**.
- Positions: `int32` cm. Path costs and squared distances: `int64`.
- Ratios: parts-per-thousand (`SimConfig.PptScale` = 1000).
- No `float` / `double` in sim state, hashes, or path decisions. `Fixed64` is not used here; remaining square roots are integer (`Int128Math.IntegerSqrt`).

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

`MovementService` exposes three searches, plus `EstimateTravelTicks` (the path’s `TotalCostTicks`, or `-1` if unreachable):

1. **`PathOnRoads(fromNode, toNode, class, costWeights)`** — integer A* on the undirected road graph. `costWeights` are ppt multipliers per road tier, with a separate bridge-span ppt. Weighted step `max(1, ticks * weightPpt / 1000)` (weight 0 skips the edge).
2. **`PathOffRoad(fromCm, toCm, class)`** — 8-connected grid A* on the per-class cost grid. Diagonal cost is `orthoTicks * 1414 / 1000` (ppt √2). No corner-cutting: both adjacent orthogonal cells must be passable.
3. **`PathCombined`** — min of pure off-road and a hierarchical road trip (see below).

Stable A* ties: pop the open set by `(f, nodeOrCellIndex)` (then the pushed index). Equal `g` keeps the parent with the smaller index. Open/closed storage is arrays + a generation counter, never `HashSet` enumeration.

Heuristics are admissible: octile-cm / maxSpeed on the grid, Euclidean-cm / maxRoadSpeed on roads (floor integer sqrt).

## Hierarchical combined query

A 200-mile map cannot run a unified A* over every cell plus every road edge as first-class neighbors without blowing the per-tick budget. Combined search therefore:

1. Runs off-road grid A* start → goal (may be unreachable, e.g. wheeled vs a ridge).
2. Picks **gateways**: road nodes whose cell is passable. If `NodeCount ≤ 4 * MaxGateways` (default 8), **every** node is a candidate — synthetic tests are exact. Otherwise the `MaxGateways` nearest to start and to goal by `(distSq, nodeId)`.
3. For each `(entry, exit)` pair in `(entryId, exitId)` order, sums `grid(start, entryCell) + PathOnRoads(entry, exit) + grid(exitCell, goal)`, skipping unreachable legs.
4. Returns the cheaper of (1) and (3). Equal cost prefers **off-road** (fewer systems in the path; stable).

This is not globally optimal when the cheapest on-ramp is not among the K nearest geometrically (a ridge that forces a far on-ramp). The full-candidate rule covers small graphs; large-map misses of that shape are the accepted cost of the budget. Revisit with true hierarchical contraction if a mode needs optimality.

Road travel **does not** pay the terrain cells under the polyline: the unit is on the graph while the span is uncut. That is why a truck can take a switchback that is authored across steep cells.

## Micro-benchmark (not a CI gate)

`dotnet run -c Release --project src/Wasteland.Sim.Benchmarks` on the Cloud Agent VM (.NET 8.0.425, linux-x64):

| Workload | Result |
| --- | --- |
| 90×90 road graph (8,100 nodes, 16,020 edges) load | 43 ms |
| First `PathOnRoads` corner-to-corner | 13 ms |
| 40 unique `PathOnRoads` | 2.10 ms/query |
| 1600×1600 terrain (2.56M cells) load + cost grids | 102 ms |
| Foot corner-to-corner `PathOffRoad` | 22 ms, 1,600 waypoints |
| 8 unique wheeled `PathOffRoad` | 32.64 ms/query |

Re-run locally after table or heuristic changes; do not treat these numbers as a pass/fail gate.

## Budget, queue, cache

- `PathRequestsPerTick` (default 8) is a match-wide cap on **A\* runs**, not on API calls.
- `Enqueue` stamps a monotonic request id. Cache hits resolve immediately and do not consume the budget. Misses sit in a FIFO `List` (enqueue order = process order; callers must submit in sim-deterministic order, typically command-queue order).
- `ProcessQueue()` runs up to the remaining budget of searches. Direct `Path*` methods exist for tests and UI preview; they use the same cache and A* but do not debit the tick budget.
- Cache key: `(kind, class, from, to, weightKey, graphVersion)`. Off-road keys pin `graphVersion` to 0 so a bridge cut does not flush grid paths. `CutSpan` increments `graphVersion` and clears keys that include it (the whole cache is dropped — `Dictionary.Clear`, no enumeration of results).

`from`/`to` for grid queries are **cell indices**, so any centimeters in the same cell share a result.

## Hashing

Each `PathResult` carries `xxHash64` over kind, cost, graph version, and every waypoint `(segmentKind, id, xCm, yCm)`. Goldens freeze that hash. Two loads of the same blobs + tables must match; repeated queries must match.

## What this service does not do

It does not steer squads. `SimWorld` still integrates Move as a straight cm line. Wiring `Move` to follow a `PathResult` is a later PR so existing command-log hashes stay intact until that integration is intentional.
