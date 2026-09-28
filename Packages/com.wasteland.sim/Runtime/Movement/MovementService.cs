using System;
using System.Collections.Generic;
using Wasteland.Sim.Math;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// Sim-owned movement service: road / grid / combined integer pathfinding,
    /// match-load cost grids, a deterministic request queue, and a result cache.
    /// </summary>
    public sealed class MovementService
    {
        private static readonly int[] DirX = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] DirY = { 1, 0, -1, 0, 1, -1, -1, 1 };

        private readonly RoadGraph _graph;
        private readonly TerrainGrid _terrain;
        private readonly MovementTables _tables;
        private readonly MovementCosts _costs;
        private readonly MovementServiceConfig _config;

        private readonly long[] _g;
        private readonly int[] _parent;
        private readonly int[] _gen;
        private readonly int[] _closed;
        private int _searchId;
        private readonly MinHeap _heap;
        private readonly List<PathWaypoint> _scratchWaypoints = new List<PathWaypoint>();
        private readonly GatewayCand[] _gatewayScratch;
        private readonly int[] _startGateways;
        private readonly int[] _goalGateways;

        private readonly Dictionary<CacheKey, PathResult> _cache = new Dictionary<CacheKey, PathResult>();
        private readonly Dictionary<int, PathResult> _results = new Dictionary<int, PathResult>();
        private readonly List<QueuedRequest> _pending = new List<QueuedRequest>();
        private int _pendingHead;
        private int _nextRequestId;

        private MovementService(
            RoadGraph graph,
            TerrainGrid terrain,
            MovementTables tables,
            MovementCosts costs,
            MovementServiceConfig config)
        {
            _graph = graph;
            _terrain = terrain;
            _tables = tables;
            _costs = costs;
            _config = config;

            int scratch = terrain.CellCount;
            if (graph.NodeCount > scratch)
            {
                scratch = graph.NodeCount;
            }

            if (scratch < 1)
            {
                scratch = 1;
            }

            _g = new long[scratch];
            _parent = new int[scratch];
            _gen = new int[scratch];
            _closed = new int[scratch];
            _heap = new MinHeap(scratch);
            _gatewayScratch = new GatewayCand[System.Math.Max(graph.NodeCount, 1)];
            _startGateways = new int[System.Math.Max(graph.NodeCount, 1)];
            _goalGateways = new int[System.Math.Max(graph.NodeCount, 1)];
        }

        public static MovementService Create(
            byte[] roadBlob,
            byte[] terrainBlob,
            MovementTables tables,
            MovementServiceConfig config)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            RoadGraph graph = RoadGraphBlob.Read(roadBlob);
            TerrainGrid terrain = TerrainGridBlob.Read(terrainBlob);
            MovementCosts costs = MovementCosts.Build(terrain, graph, tables);
            return new MovementService(graph, terrain, tables, costs, config);
        }

        public static MovementService Create(byte[] roadBlob, byte[] terrainBlob, MovementTables tables)
        {
            return Create(roadBlob, terrainBlob, tables, MovementServiceConfig.Default);
        }

        public RoadGraph Graph
        {
            get { return _graph; }
        }

        public TerrainGrid Terrain
        {
            get { return _terrain; }
        }

        public MovementTables Tables
        {
            get { return _tables; }
        }

        public int GraphVersion
        {
            get { return _graph.Version; }
        }

        public int PathRequestsPerTick
        {
            get { return _config.PathRequestsPerTick; }
        }

        public int PendingCount
        {
            get { return _pending.Count - _pendingHead; }
        }

        public bool CutBridge(int edgeId)
        {
            bool cut = _graph.CutSpan(edgeId);
            if (cut)
            {
                _cache.Clear();
            }

            return cut;
        }

        public PathResult PathOnRoads(int fromNodeId, int toNodeId, MovementClass clas, RoadCostWeights weights)
        {
            CacheKey key = new CacheKey(
                (byte)PathQueryKind.OnRoads,
                (byte)clas,
                fromNodeId,
                0,
                toNodeId,
                0,
                weights.Key,
                _graph.Version);
            if (_cache.TryGetValue(key, out PathResult hit))
            {
                return hit;
            }

            PathResult result = SearchRoads(fromNodeId, toNodeId, clas, weights);
            _cache[key] = result;
            return result;
        }

        public PathResult PathOffRoad(int fromXCm, int fromYCm, int toXCm, int toYCm, MovementClass clas)
        {
            if (!TryCells(fromXCm, fromYCm, toXCm, toYCm, out int fromCell, out int toCell))
            {
                return Unreachable(0);
            }

            CacheKey key = new CacheKey(
                (byte)PathQueryKind.OffRoad,
                (byte)clas,
                fromCell,
                0,
                toCell,
                0,
                0,
                0);
            if (_cache.TryGetValue(key, out PathResult hit))
            {
                return hit;
            }

            PathResult result = SearchGrid(fromCell, toCell, clas, PathKind.OffRoad, 0);
            _cache[key] = result;
            return result;
        }

        public PathResult PathCombined(
            int fromXCm,
            int fromYCm,
            int toXCm,
            int toYCm,
            MovementClass clas,
            RoadCostWeights weights)
        {
            if (!TryCells(fromXCm, fromYCm, toXCm, toYCm, out int fromCell, out int toCell))
            {
                return Unreachable(_graph.Version);
            }

            CacheKey key = new CacheKey(
                (byte)PathQueryKind.Combined,
                (byte)clas,
                fromCell,
                0,
                toCell,
                0,
                weights.Key,
                _graph.Version);
            if (_cache.TryGetValue(key, out PathResult hit))
            {
                return hit;
            }

            PathResult result = SearchCombined(fromXCm, fromYCm, toXCm, toYCm, fromCell, toCell, clas, weights);
            _cache[key] = result;
            return result;
        }

        public long EstimateTravelTicks(PathResult path)
        {
            if (path == null || !path.Reachable)
            {
                return -1;
            }

            return path.TotalCostTicks;
        }

        public int Enqueue(PathRequest request)
        {
            int id = ++_nextRequestId;
            CacheKey key = KeyFor(request);
            if (_cache.TryGetValue(key, out PathResult hit))
            {
                _results[id] = hit;
                return id;
            }

            _pending.Add(new QueuedRequest(id, request));
            return id;
        }

        public bool TryGetResult(int requestId, out PathResult result)
        {
            return _results.TryGetValue(requestId, out result);
        }

        /// <summary>
        /// Runs up to <see cref="PathRequestsPerTick"/> cache-missing searches.
        /// Cache hits that land while draining the queue do not consume the budget.
        /// </summary>
        public int ProcessQueue()
        {
            CompactPending();
            int ran = 0;
            int budget = _config.PathRequestsPerTick;
            while (_pendingHead < _pending.Count && ran < budget)
            {
                QueuedRequest q = _pending[_pendingHead++];
                CacheKey key = KeyFor(q.Request);
                if (_cache.TryGetValue(key, out PathResult hit))
                {
                    _results[q.Id] = hit;
                    continue;
                }

                PathResult result = Execute(q.Request);
                _results[q.Id] = result;
                ran++;
            }

            return ran;
        }

        private PathResult Execute(PathRequest request)
        {
            switch (request.Kind)
            {
                case PathQueryKind.OnRoads:
                    return PathOnRoads(request.FromA, request.ToA, request.Class, request.Weights);
                case PathQueryKind.OffRoad:
                    return PathOffRoad(request.FromA, request.FromB, request.ToA, request.ToB, request.Class);
                default:
                    return PathCombined(
                        request.FromA,
                        request.FromB,
                        request.ToA,
                        request.ToB,
                        request.Class,
                        request.Weights);
            }
        }

        private CacheKey KeyFor(PathRequest request)
        {
            if (request.Kind == PathQueryKind.OnRoads)
            {
                return new CacheKey(
                    (byte)request.Kind,
                    (byte)request.Class,
                    request.FromA,
                    0,
                    request.ToA,
                    0,
                    request.Weights.Key,
                    _graph.Version);
            }

            if (!TryCells(request.FromA, request.FromB, request.ToA, request.ToB, out int fromCell, out int toCell))
            {
                return new CacheKey((byte)request.Kind, (byte)request.Class, -1, -1, -1, -1, request.Weights.Key, _graph.Version);
            }

            int version = request.Kind == PathQueryKind.OffRoad ? 0 : _graph.Version;
            ulong weightKey = request.Kind == PathQueryKind.OffRoad ? 0UL : request.Weights.Key;
            return new CacheKey((byte)request.Kind, (byte)request.Class, fromCell, 0, toCell, 0, weightKey, version);
        }

        private PathResult SearchCombined(
            int fromXCm,
            int fromYCm,
            int toXCm,
            int toYCm,
            int fromCell,
            int toCell,
            MovementClass clas,
            RoadCostWeights weights)
        {
            PathResult offRoad = SearchGrid(fromCell, toCell, clas, PathKind.OffRoad, 0);
            int startCount = CollectGateways(fromXCm, fromYCm, clas, _startGateways);
            int goalCount = CollectGateways(toXCm, toYCm, clas, _goalGateways);
            PathResult bestHier = PathResult.Unreachable;
            bool haveHier = false;
            long bestCost = 0;
            int bestEntryId = int.MaxValue;
            int bestExitId = int.MaxValue;

            for (int s = 0; s < startCount; s++)
            {
                int entryIndex = _startGateways[s];
                int entryCell = _costs.NodeCellIndex[entryIndex];
                int entryId = _graph.NodeId[entryIndex];
                PathResult toEntry = SearchGrid(fromCell, entryCell, clas, PathKind.OffRoad, 0);
                if (!toEntry.Reachable)
                {
                    continue;
                }

                for (int t = 0; t < goalCount; t++)
                {
                    int exitIndex = _goalGateways[t];
                    int exitCell = _costs.NodeCellIndex[exitIndex];
                    int exitId = _graph.NodeId[exitIndex];
                    PathResult road = SearchRoads(entryId, exitId, clas, weights);
                    if (!road.Reachable)
                    {
                        continue;
                    }

                    PathResult fromExit = SearchGrid(exitCell, toCell, clas, PathKind.OffRoad, 0);
                    if (!fromExit.Reachable)
                    {
                        continue;
                    }

                    long cost = toEntry.TotalCostTicks + road.TotalCostTicks + fromExit.TotalCostTicks;
                    bool better = !haveHier
                        || cost < bestCost
                        || (cost == bestCost && (entryId < bestEntryId || (entryId == bestEntryId && exitId < bestExitId)));
                    if (!better)
                    {
                        continue;
                    }

                    bestCost = cost;
                    bestEntryId = entryId;
                    bestExitId = exitId;
                    haveHier = true;
                    bestHier = Stitch(toEntry, road, fromExit, cost);
                }
            }

            if (offRoad.Reachable)
            {
                if (!haveHier || offRoad.TotalCostTicks <= bestCost)
                {
                    return offRoad;
                }

                return bestHier;
            }

            if (haveHier)
            {
                return bestHier;
            }

            return Unreachable(_graph.Version);
        }

        private PathResult Stitch(PathResult toEntry, PathResult road, PathResult fromExit, long cost)
        {
            _scratchWaypoints.Clear();
            AppendWaypoints(_scratchWaypoints, toEntry.Waypoints);
            AppendWaypoints(_scratchWaypoints, road.Waypoints);
            AppendWaypoints(_scratchWaypoints, fromExit.Waypoints);
            PathWaypoint[] points = _scratchWaypoints.ToArray();
            return PathResult.Create(true, cost, PathKind.Combined, _graph.Version, points);
        }

        private static void AppendWaypoints(List<PathWaypoint> dst, PathWaypoint[] src)
        {
            for (int i = 0; i < src.Length; i++)
            {
                PathWaypoint p = src[i];
                if (dst.Count > 0)
                {
                    PathWaypoint last = dst[dst.Count - 1];
                    if (last.Kind == p.Kind && last.Id == p.Id)
                    {
                        continue;
                    }
                }

                dst.Add(p);
            }
        }

        private int CollectGateways(int xCm, int yCm, MovementClass clas, int[] dest)
        {
            int n = _graph.NodeCount;
            int write = 0;
            for (int i = 0; i < n; i++)
            {
                int cell = _costs.NodeCellIndex[i];
                if (cell < 0 || !_costs.IsPassable(clas, cell))
                {
                    continue;
                }

                long dx = (long)_graph.NodeXCm[i] - xCm;
                long dy = (long)_graph.NodeYCm[i] - yCm;
                _gatewayScratch[write] = new GatewayCand(dx * dx + dy * dy, _graph.NodeId[i], i);
                write++;
            }

            if (write == 0)
            {
                return 0;
            }

            Array.Sort(_gatewayScratch, 0, write, GatewayCandComparer.Instance);
            int cap = _config.MaxGateways;
            if (n <= cap * 4)
            {
                cap = write;
            }

            if (cap > write)
            {
                cap = write;
            }

            if (cap > dest.Length)
            {
                cap = dest.Length;
            }

            for (int i = 0; i < cap; i++)
            {
                dest[i] = _gatewayScratch[i].NodeIndex;
            }

            for (int i = 1; i < cap; i++)
            {
                int value = dest[i];
                int j = i;
                while (j > 0 && _graph.NodeId[dest[j - 1]] > _graph.NodeId[value])
                {
                    dest[j] = dest[j - 1];
                    j--;
                }

                dest[j] = value;
            }

            return cap;
        }

        private PathResult SearchRoads(int fromNodeId, int toNodeId, MovementClass clas, RoadCostWeights weights)
        {
            if (!_graph.TryGetNodeIndex(fromNodeId, out int fromIndex)
                || !_graph.TryGetNodeIndex(toNodeId, out int toIndex))
            {
                return Unreachable(_graph.Version);
            }

            if (fromIndex == toIndex)
            {
                PathWaypoint[] one =
                {
                    new PathWaypoint(
                        PathSegmentKind.RoadNode,
                        fromNodeId,
                        _graph.NodeXCm[fromIndex],
                        _graph.NodeYCm[fromIndex])
                };
                return PathResult.Create(true, 0, PathKind.OnRoads, _graph.Version, one);
            }

            int classIndex = (int)clas;
            int[] speeds = _costs.RoadSpeedCmPerTick[classIndex];
            int maxSpeed = _costs.MaxRoadSpeed[classIndex];
            int minWeight = MinWeight(weights);
            BeginSearch();
            _g[fromIndex] = 0;
            _parent[fromIndex] = fromIndex;
            _gen[fromIndex] = _searchId;
            _heap.Push(HeuristicRoad(fromIndex, toIndex, maxSpeed, minWeight), fromIndex, fromIndex);

            while (_heap.TryPop(out int current, out _))
            {
                if (_gen[current] != _searchId || _closed[current] == _searchId)
                {
                    continue;
                }

                _closed[current] = _searchId;
                if (current == toIndex)
                {
                    return ReconstructRoad(fromIndex, toIndex);
                }

                int adjStart = _graph.AdjOffset[current];
                int adjEnd = _graph.AdjOffset[current + 1];
                for (int a = adjStart; a < adjEnd; a++)
                {
                    int edge = _graph.AdjEdgeIndex[a];
                    if (_graph.EdgeCut[edge])
                    {
                        continue;
                    }

                    int speed = speeds[edge];
                    if (speed <= 0)
                    {
                        continue;
                    }

                    bool span = (_graph.EdgeFlags[edge] & (ushort)RoadEdgeFlags.BridgeSpan) != 0;
                    int weight = weights.PptFor((RoadTier)_graph.EdgeTier[edge], span);
                    if (weight <= 0)
                    {
                        continue;
                    }

                    int ticks = MovementTables.TicksToTraverse(_graph.EdgeLengthCm[edge], speed);
                    long step = (long)ticks * weight / SimConfig.PptScale;
                    if (step < 1)
                    {
                        step = 1;
                    }

                    int next = _graph.AdjNeighborIndex[a];
                    long ng = _g[current] + step;
                    if (_gen[next] != _searchId || ng < _g[next] || (ng == _g[next] && current < _parent[next]))
                    {
                        _gen[next] = _searchId;
                        _g[next] = ng;
                        _parent[next] = current;
                        long f = ng + HeuristicRoad(next, toIndex, maxSpeed, minWeight);
                        _heap.Push(f, next, next);
                    }
                }
            }

            return Unreachable(_graph.Version);
        }

        private PathResult SearchGrid(int fromCell, int toCell, MovementClass clas, PathKind kind, int graphVersion)
        {
            int classIndex = (int)clas;
            int[] ticks = _costs.CellTicks[classIndex];
            if (ticks[fromCell] <= 0 || ticks[toCell] <= 0)
            {
                return Unreachable(graphVersion);
            }

            if (fromCell == toCell)
            {
                int x = fromCell % _terrain.Width;
                int y = fromCell / _terrain.Width;
                PathWaypoint[] one =
                {
                    new PathWaypoint(
                        PathSegmentKind.GridCell,
                        fromCell,
                        _terrain.CellCenterXCm(x),
                        _terrain.CellCenterYCm(y))
                };
                return PathResult.Create(true, 0, kind, graphVersion, one);
            }

            int maxSpeed = _costs.MaxGridSpeed[classIndex];
            int width = _terrain.Width;
            int height = _terrain.Height;
            BeginSearch();
            _g[fromCell] = 0;
            _parent[fromCell] = fromCell;
            _gen[fromCell] = _searchId;
            _heap.Push(HeuristicGrid(fromCell, toCell, maxSpeed), fromCell, fromCell);

            while (_heap.TryPop(out int current, out _))
            {
                if (_gen[current] != _searchId || _closed[current] == _searchId)
                {
                    continue;
                }

                _closed[current] = _searchId;
                if (current == toCell)
                {
                    return ReconstructGrid(fromCell, toCell, kind, graphVersion);
                }

                int cx = current % width;
                int cy = current / width;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DirX[d];
                    int ny = cy + DirY[d];
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    {
                        continue;
                    }

                    int next = ny * width + nx;
                    int step = ticks[next];
                    if (step <= 0)
                    {
                        continue;
                    }

                    bool diagonal = DirX[d] != 0 && DirY[d] != 0;
                    if (diagonal)
                    {
                        int orthoA = cy * width + nx;
                        int orthoB = ny * width + cx;
                        if (ticks[orthoA] <= 0 || ticks[orthoB] <= 0)
                        {
                            continue;
                        }

                        step = (int)((long)step * MovementConfig.Sqrt2Ppt / SimConfig.PptScale);
                        if (step < 1)
                        {
                            step = 1;
                        }
                    }

                    long ng = _g[current] + step;
                    if (_gen[next] != _searchId || ng < _g[next] || (ng == _g[next] && current < _parent[next]))
                    {
                        _gen[next] = _searchId;
                        _g[next] = ng;
                        _parent[next] = current;
                        long f = ng + HeuristicGrid(next, toCell, maxSpeed);
                        _heap.Push(f, next, next);
                    }
                }
            }

            return Unreachable(graphVersion);
        }

        private PathResult ReconstructRoad(int fromIndex, int toIndex)
        {
            _scratchWaypoints.Clear();
            int current = toIndex;
            while (true)
            {
                _scratchWaypoints.Add(new PathWaypoint(
                    PathSegmentKind.RoadNode,
                    _graph.NodeId[current],
                    _graph.NodeXCm[current],
                    _graph.NodeYCm[current]));
                if (current == fromIndex)
                {
                    break;
                }

                current = _parent[current];
            }

            _scratchWaypoints.Reverse();
            return PathResult.Create(true, _g[toIndex], PathKind.OnRoads, _graph.Version, _scratchWaypoints.ToArray());
        }

        private PathResult ReconstructGrid(int fromCell, int toCell, PathKind kind, int graphVersion)
        {
            _scratchWaypoints.Clear();
            int width = _terrain.Width;
            int current = toCell;
            while (true)
            {
                int x = current % width;
                int y = current / width;
                _scratchWaypoints.Add(new PathWaypoint(
                    PathSegmentKind.GridCell,
                    current,
                    _terrain.CellCenterXCm(x),
                    _terrain.CellCenterYCm(y)));
                if (current == fromCell)
                {
                    break;
                }

                current = _parent[current];
            }

            _scratchWaypoints.Reverse();
            return PathResult.Create(true, _g[toCell], kind, graphVersion, _scratchWaypoints.ToArray());
        }

        private long HeuristicGrid(int fromCell, int toCell, int maxSpeed)
        {
            if (maxSpeed <= 0)
            {
                return 0;
            }

            int width = _terrain.Width;
            int x0 = fromCell % width;
            int y0 = fromCell / width;
            int x1 = toCell % width;
            int y1 = toCell / width;
            int dx = x0 > x1 ? x0 - x1 : x1 - x0;
            int dy = y0 > y1 ? y0 - y1 : y1 - y0;
            int min = dx < dy ? dx : dy;
            int max = dx < dy ? dy : dx;
            int cell = _terrain.CellSizeCm;
            long octileCm = ((long)cell * max)
                + (((long)cell * MovementConfig.Sqrt2MinusOnePpt / SimConfig.PptScale) * min);
            return octileCm / maxSpeed;
        }

        private long HeuristicRoad(int fromIndex, int toIndex, int maxSpeed, int minWeightPpt)
        {
            if (maxSpeed <= 0 || minWeightPpt <= 0)
            {
                return 0;
            }

            long dx = (long)_graph.NodeXCm[fromIndex] - _graph.NodeXCm[toIndex];
            long dy = (long)_graph.NodeYCm[fromIndex] - _graph.NodeYCm[toIndex];
            long distSq = dx * dx + dy * dy;
            ulong dist = Int128Math.IntegerSqrt(0UL, (ulong)distSq);
            return ((long)dist * minWeightPpt) / ((long)maxSpeed * SimConfig.PptScale);
        }

        private static int MinWeight(RoadCostWeights weights)
        {
            int m = weights.TrackPpt;
            if (weights.DirtPpt < m)
            {
                m = weights.DirtPpt;
            }

            if (weights.PavedPpt < m)
            {
                m = weights.PavedPpt;
            }

            if (weights.HighwayPpt < m)
            {
                m = weights.HighwayPpt;
            }

            if (weights.BridgeSpanPpt < m)
            {
                m = weights.BridgeSpanPpt;
            }

            return m < 0 ? 0 : m;
        }

        private bool TryCells(int fromXCm, int fromYCm, int toXCm, int toYCm, out int fromCell, out int toCell)
        {
            fromCell = 0;
            toCell = 0;
            if (!_terrain.TryWorldToCell(fromXCm, fromYCm, out int fx, out int fy)
                || !_terrain.TryWorldToCell(toXCm, toYCm, out int tx, out int ty))
            {
                return false;
            }

            fromCell = _terrain.Index(fx, fy);
            toCell = _terrain.Index(tx, ty);
            return true;
        }

        private void BeginSearch()
        {
            _searchId++;
            if (_searchId == int.MaxValue)
            {
                Array.Clear(_gen, 0, _gen.Length);
                Array.Clear(_closed, 0, _closed.Length);
                _searchId = 1;
            }

            _heap.Clear();
        }

        private void CompactPending()
        {
            if (_pendingHead == 0)
            {
                return;
            }

            _pending.RemoveRange(0, _pendingHead);
            _pendingHead = 0;
        }

        private static PathResult Unreachable(int graphVersion)
        {
            if (graphVersion == 0)
            {
                return PathResult.Unreachable;
            }

            return PathResult.Create(false, 0, PathKind.Unreachable, graphVersion, Array.Empty<PathWaypoint>());
        }

        private struct GatewayCand
        {
            public readonly long DistSq;
            public readonly int NodeId;
            public readonly int NodeIndex;

            public GatewayCand(long distSq, int nodeId, int nodeIndex)
            {
                DistSq = distSq;
                NodeId = nodeId;
                NodeIndex = nodeIndex;
            }
        }

        private sealed class GatewayCandComparer : IComparer<GatewayCand>
        {
            public static readonly GatewayCandComparer Instance = new GatewayCandComparer();

            public int Compare(GatewayCand x, GatewayCand y)
            {
                int c = x.DistSq.CompareTo(y.DistSq);
                if (c != 0)
                {
                    return c;
                }

                return x.NodeId.CompareTo(y.NodeId);
            }
        }

        private readonly struct QueuedRequest
        {
            public readonly int Id;
            public readonly PathRequest Request;

            public QueuedRequest(int id, PathRequest request)
            {
                Id = id;
                Request = request;
            }
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly byte Kind;
            public readonly byte Class;
            public readonly int FromA;
            public readonly int FromB;
            public readonly int ToA;
            public readonly int ToB;
            public readonly ulong WeightKey;
            public readonly int GraphVersion;

            public CacheKey(byte kind, byte clas, int fromA, int fromB, int toA, int toB, ulong weightKey, int graphVersion)
            {
                Kind = kind;
                Class = clas;
                FromA = fromA;
                FromB = fromB;
                ToA = toA;
                ToB = toB;
                WeightKey = weightKey;
                GraphVersion = graphVersion;
            }

            public bool Equals(CacheKey other)
            {
                return Kind == other.Kind
                    && Class == other.Class
                    && FromA == other.FromA
                    && FromB == other.FromB
                    && ToA == other.ToA
                    && ToB == other.ToB
                    && WeightKey == other.WeightKey
                    && GraphVersion == other.GraphVersion;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Kind;
                    hash = (hash * 397) ^ Class;
                    hash = (hash * 397) ^ FromA;
                    hash = (hash * 397) ^ FromB;
                    hash = (hash * 397) ^ ToA;
                    hash = (hash * 397) ^ ToB;
                    hash = (hash * 397) ^ (int)WeightKey;
                    hash = (hash * 397) ^ (int)(WeightKey >> 32);
                    hash = (hash * 397) ^ GraphVersion;
                    return hash;
                }
            }
        }
    }
}
