namespace Wasteland.Sim.Movement
{
    /// <summary>Integer constants for movement tables, cost grids, and A*.</summary>
    public static class MovementConfig
    {
        /// <summary>
        /// Divisor for <c>base * surfacePpt * slopePpt</c>. Equals
        /// <see cref="SimConfig.PptScale"/> squared (1,000,000).
        /// </summary>
        public const int SpeedPptDivisor = SimConfig.PptScale * SimConfig.PptScale;

        /// <summary>Parts-per-thousand approximation of √2 (1.414).</summary>
        public const int Sqrt2Ppt = 1414;

        /// <summary>√2 − 1 in ppt, used by the octile heuristic.</summary>
        public const int Sqrt2MinusOnePpt = Sqrt2Ppt - SimConfig.PptScale;

        /// <summary>Default terrain cell size (100 m).</summary>
        public const int DefaultCellSizeCm = 10000;

        /// <summary>Match-wide A* completions per tick. Cache hits do not consume it.</summary>
        public const int DefaultPathRequestsPerTick = 8;

        /// <summary>
        /// Node expansions (closed vertices) allowed per <c>ProcessQueue</c> call.
        /// Wall clock is never the cap; this integer is sim state.
        /// </summary>
        public const int DefaultExpansionsPerTick = 4096;

        /// <summary>
        /// HPA* cluster side in cells. Maps that fit in one cluster use exact grid A*
        /// (golden tests stay optimal).
        /// </summary>
        public const int DefaultClusterSize = 32;

        /// <summary>
        /// Extra HPA* border entrances along each contiguous passable run (plus the
        /// run's first, last, and midpoint cells). Corners stay in the abstract graph
        /// so 8-connected cluster hops stay cheap.
        /// </summary>
        public const int DefaultEntranceInterval = 8;

        /// <summary>ALT landmarks per movement class for road A*.</summary>
        public const int DefaultLandmarkCount = 16;

        /// <summary>
        /// Max road-node gateways sampled at each end of a combined query
        /// on large graphs. Small graphs use every node (see docs/movement.md).
        /// </summary>
        public const int DefaultMaxGateways = 8;

        public const int MaxRoadNodes = 2000000;
        public const int MaxRoadEdges = 4000000;
        public const int MaxGridDimension = 16384;

        public const ushort RoadSchemaVersion = 1;
        public const ushort TerrainSchemaVersion = 1;
        public const ushort BlobHeaderSize = 32;
        public const int RoadNodeStride = 16;
        public const int RoadEdgeStride = 24;
        public const int TerrainCellStride = 4;
    }
}
