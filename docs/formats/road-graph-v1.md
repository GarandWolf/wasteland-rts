# Road graph blob (schema v1)

Little-endian binary. Produced by the map pipeline; consumed by `Wasteland.Sim.Movement.RoadGraphBlob`. The sim never infers connectivity from presentation meshes or Unity NavMesh.

Magic ASCII `WRG1`. Schema version `1`. Readers **must** reject any other magic or schema. Extra bytes at the end of the file are invalid.

## Header (32 bytes)

| Offset | Size | Type | Field |
| --- | --- | --- | --- |
| 0 | 4 | char[4] | Magic `WRG1` |
| 4 | 2 | uint16 | `SchemaVersion` = 1 |
| 6 | 2 | uint16 | `HeaderSize` = 32 (payload starts here; v1 readers skip unknown header padding if this were ever larger **and** schema stayed 1 — it will not; bump schema instead) |
| 8 | 4 | uint32 | `NodeCount` |
| 12 | 4 | uint32 | `EdgeCount` |
| 16 | 4 | int32 | `OriginXCm` (authoring origin; unused by A* except documentation) |
| 20 | 4 | int32 | `OriginYCm` |
| 24 | 4 | uint32 | `Flags` (reserved, write 0) |
| 28 | 4 | uint32 | `Reserved` (write 0) |

Caps: `NodeCount` ≤ 2,000,000, `EdgeCount` ≤ 4,000,000. File size must equal `HeaderSize + NodeCount*16 + EdgeCount*24`.

## Node record (16 bytes), sorted by `NodeId` ascending, unique

| Offset | Size | Type | Field |
| --- | --- | --- | --- |
| 0 | 4 | uint32 | `NodeId` |
| 4 | 4 | int32 | `XCm` |
| 8 | 4 | int32 | `YCm` |
| 12 | 2 | uint16 | `Flags` — bit 0 = `BridgeHead` |
| 14 | 2 | uint16 | `Reserved` (write 0) |

`NodeId` 0 is legal. Coordinates are world centimeters.

## Edge record (24 bytes), sorted by `EdgeId` ascending, unique

| Offset | Size | Type | Field |
| --- | --- | --- | --- |
| 0 | 4 | uint32 | `EdgeId` |
| 4 | 4 | uint32 | `FromNodeId` |
| 8 | 4 | uint32 | `ToNodeId` |
| 12 | 4 | uint32 | `LengthCm` (> 0) |
| 16 | 1 | uint8 | `Tier` — 0 track, 1 dirt, 2 paved, 3 highway |
| 17 | 1 | uint8 | `Surface` — 0 dirt, 1 gravel, 2 asphalt, 3 bridge deck |
| 18 | 2 | uint16 | `Flags` — bit 0 = `BridgeSpan` |
| 20 | 4 | uint32 | `Reserved` (write 0) |

Endpoints must exist. Self-loops (`From == To`) are rejected. Roads are **undirected**: the runtime CSR inserts both directions. Parallel edges are allowed; A* keeps the cheaper one.

`LengthCm` is the authored travel length (not necessarily the Euclidean distance). The map pipeline may follow a polyline; the sim only sees this integer.

## Bridges

A bridge is two nodes with `BridgeHead` joined by one edge with `BridgeSpan`. Both endpoints of a span **must** be bridge heads. The span may be cut at runtime (`RoadGraph.CutSpan`); that does not rewrite the blob. Each successful cut increments `graphVersion` (starts at 1). Already-cut spans are no-ops. Pathfinding skips cut spans; the CSR is not rebuilt.

A non-span edge may still connect two bridge heads (the ford / bypass). Cutting is refused for non-span edges.

## Adjacency iteration

Runtime adjacency is sorted by neighbor `NodeId`, then `EdgeId`. Search never enumerates a `Dictionary` or `HashSet`.
