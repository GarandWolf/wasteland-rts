# Terrain grid blob

Superseded by the map-pipeline **WRMB** container. See [wrmb-v0.md](wrmb-v0.md).

Terrain ships as a **separate** WRMB file from the map/AI file. The sim reads `META.cellSizeCm` (and `moveCellsPerCell`) plus `MVSL` / `MVLC` / `MVFD`. The old `WTG1` schema is no longer produced or consumed.
