# Terrain grid blob (schema v1)

Little-endian binary. Slope and land-cover only — no baked movement costs. Per-class cost grids are computed at **match load** from designer tables so balance tweaks do not rebake the map.

Magic ASCII `WTG1`. Schema version `1`. Readers **must** reject any other magic or schema. Extra bytes at the end of the file are invalid.

## Header (32 bytes)

| Offset | Size | Type | Field |
| --- | --- | --- | --- |
| 0 | 4 | char[4] | Magic `WTG1` |
| 4 | 2 | uint16 | `SchemaVersion` = 1 |
| 6 | 2 | uint16 | `HeaderSize` = 32 |
| 8 | 4 | uint32 | `Width` (cells, ≥ 1) |
| 12 | 4 | uint32 | `Height` (cells, ≥ 1) |
| 16 | 4 | uint32 | `CellSizeCm` (≥ 1; default authoring size is 10,000 = 100 m) |
| 20 | 4 | int32 | `OriginXCm` (world cm of the min corner of cell `(0,0)`) |
| 24 | 4 | int32 | `OriginYCm` |
| 28 | 4 | uint32 | `Flags` (reserved, write 0) |

Caps: `Width` ≤ 16,384, `Height` ≤ 16,384, `Width * Height` must fit in `int`. File size must equal `HeaderSize + Width*Height*4`.

## Cell record (4 bytes), row-major

Index `i = y * Width + x` with `x ∈ [0, Width)` and `y ∈ [0, Height)`. `y` increases with world Y.

| Offset | Size | Type | Field |
| --- | --- | --- | --- |
| 0 | 2 | uint16 | `SlopePerMille` — magnitude of grade (rise/run × 1000). 0 is flat; 300 is a 30% grade. Sign is dropped at import: the sim stores magnitude only. |
| 2 | 1 | uint8 | `LandCover` (see below) |
| 3 | 1 | uint8 | `Reserved` (write 0) |

## Land cover

| Value | Name | Notes |
| --- | --- | --- |
| 0 | Field | Open ground |
| 1 | Scrub | Scrub / rock |
| 2 | Urban | Built-up |
| 3 | Forest | |
| 4 | Marsh | |
| 5 | Water | Impassable to ground classes unless the class table says otherwise |
| 6 | Ford | Shallow crossing |

Unknown values are rejected at load.

## World mapping

Integer floor division, including negatives:

```
cellX = FloorDiv(xCm - OriginXCm, CellSizeCm)
cellY = FloorDiv(yCm - OriginYCm, CellSizeCm)
```

Cell center used as the waypoint for that cell:

```
xCm = OriginXCm + cellX * CellSizeCm + CellSizeCm / 2
yCm = OriginYCm + cellY * CellSizeCm + CellSizeCm / 2
```

(`/` is toward-zero integer division on a positive `CellSizeCm`.)
