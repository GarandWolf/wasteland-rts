using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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
        private readonly GridHierarchy[] _hierarchies;
        private readonly RoadLandmarks[] _landmarks;
        private readonly MovementLoadStats _loadStats;

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
        private readonly long[] _hpaStartCost;
        private readonly long[] _hpaGoalCost;
        private readonly int[] _hpaAbsPath;
        private readonly List<PathWaypoint> _sliceWaypoints = new List<PathWaypoint>();

        private readonly Dictionary<CacheKey, PathResult> _cache = new Dictionary<CacheKey, PathResult>();
        private readonly Dictionary<int, PathResult> _results = new Dictionary<int, PathResult>();
        private readonly List<QueuedRequest> _pending = new List<QueuedRequest>();
        private int _pendingHead;
        private int _nextRequestId;
        private int _searchExpansions;

        private const byte ModeNone = 0;
        private const byte ModeGrid = 1;
        private const byte ModeRoad = 2;
        private const byte ModeHpaFillStart = 3;
        private const byte ModeHpaFillGoal = 4;
        private const byte ModeHpaAbstract = 5;
        private const byte ModeHpaRefineSetup = 6;
        private const byte ModeHpaRefine = 7;

        private bool _sliceActive;
        private byte _sliceMode;
        private int _sliceRequestId;
        private PathRequest _sliceRequest;
        private int _sliceX0;
        private int _sliceY0;
        private int _sliceX1;
        private int _sliceY1;
        private int _sliceFrom;
        private int _sliceTo;
        private int _sliceStopLocal;
        private MovementClass _sliceClass;
        private PathKind _slicePathKind;
        private int _sliceGraphVersion;
        private RoadCostWeights _sliceWeights;
        private int _sliceMaxSpeed;
        private int _sliceMinWeight;

        private bool _combActive;
        private byte _combPhase;
        private int _combS;
        private int _combT;
        private int _combStartCount;
        private int _combGoalCount;
        private int _combFromCell;
        private int _combToCell;
        private PathResult _combOff = PathResult.Unreachable;
        private PathResult _combToEntry = PathResult.Unreachable;
        private PathResult _combRoad = PathResult.Unreachable;
        private PathResult _combBest = PathResult.Unreachable;
        private bool _combHaveHier;
        private long _combBestCost;
        private int _combBestEntryId;
        private int _combBestExitId;

        private long _hpaDirectCost;
        private int _hpaAbsLen;
        private int _hpaRefineStep;
        private int _hpaFromCell;
        private int _hpaToCell;
        private int _hpaStartCluster;
        private int _hpaGoalCluster;
        private long _hpaCost;
        private int _hpaStartNode;
        private int _hpaGoalNode;

        private MovementService(
            RoadGraph graph,
            TerrainGrid terrain,
            MovementTables tables,
            MovementCosts costs,
            MovementServiceConfig config,
            GridHierarchy[] hierarchies,
            RoadLandmarks[] landmarks,
            MovementLoadStats loadStats)
        {
            _graph = graph;
            _terrain = terrain;
            _tables = tables;
            _costs = costs;
            _config = config;
            _hierarchies = hierarchies;
            _landmarks = landmarks;
            _loadStats = loadStats;

            int scratch = graph.NodeCount + 2;
            int clusterCells = config.ClusterSize * config.ClusterSize;
            if (clusterCells > scratch)
            {
                scratch = clusterCells;
            }

            bool smallMap = terrain.Width <= config.ClusterSize && terrain.Height <= config.ClusterSize;
            int maxAbs = 0;
            for (int c = 0; c < hierarchies.Length; c++)
            {
                if (hierarchies[c] != null && hierarchies[c].NodeCount + 2 > maxAbs)
                {
                    maxAbs = hierarchies[c].NodeCount + 2;
                }
            }

            if (maxAbs > scratch)
            {
                scratch = maxAbs;
            }

            if (smallMap && terrain.CellCount > scratch)
            {
                scratch = terrain.CellCount;
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
            int hpaNodes = System.Math.Max(maxAbs, 1);
            _hpaStartCost = new long[hpaNodes];
            _hpaGoalCost = new long[hpaNodes];
            _hpaAbsPath = new int[hpaNodes];
            loadStats.ScratchBytes = (long)scratch * 20;
        }

        public static MovementService Create(
            byte[] mapBlob,
            byte[] terrainBlob,
            MovementTables tables,
            MovementServiceConfig config)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            Stopwatch loadClock = Stopwatch.StartNew();
            RoadGraph graph = Wrmb.ReadRoadGraph(mapBlob);
            WrmbFile terrainFile = WrmbFile.Parse(terrainBlob);
            TerrainGrid terrain = Wrmb.ReadTerrain(terrainFile);
            Stopwatch costClock = Stopwatch.StartNew();
            MovementCosts costs = MovementCosts.Build(terrain, graph, tables);
            costClock.Stop();
            GridHierarchy[] hierarchies = new GridHierarchy[MovementTables.ClassCount];
            RoadLandmarks[] landmarks = new RoadLandmarks[MovementTables.ClassCount];
            int clusterSize = config.ClusterSize;
            int clustersX = (terrain.Width + clusterSize - 1) / clusterSize;
            int clustersY = (terrain.Height + clusterSize - 1) / clusterSize;
            bool buildHierarchy = clustersX > 1 || clustersY > 1;
            int workers = config.BuildWorkers;
            if (workers <= 0)
            {
                workers = Environment.ProcessorCount;
            }

            if (workers < 1)
            {
                workers = 1;
            }

            bool cacheHit = false;
            byte[] terrainHash = terrainFile.TerrainPayloadSha256();
            ulong tableHash = tables.ContentHash();
            string? hierarchyCacheDirectory = config.HierarchyCacheDirectory;
            Stopwatch hierClock = Stopwatch.StartNew();
            GridHierarchy[]? cached = null;
            bool loadedFromCache = false;
            if (buildHierarchy && hierarchyCacheDirectory != null && hierarchyCacheDirectory.Length > 0)
            {
                loadedFromCache = HierarchyCache.TryLoad(
                    hierarchyCacheDirectory,
                    terrainHash,
                    tableHash,
                    clusterSize,
                    terrain.Width,
                    terrain.Height,
                    out GridHierarchy[] loaded);
                cached = loaded;
            }

            if (loadedFromCache
                && cached != null
                && cached.Length == MovementTables.ClassCount)
            {
                hierarchies = cached;
                cacheHit = true;
            }
            else
            {
                int classWorkers;
                int innerWorkers;
                if (workers >= MovementTables.ClassCount * 2)
                {
                    classWorkers = MovementTables.ClassCount;
                    innerWorkers = System.Math.Max(1, workers / MovementTables.ClassCount);
                }
                else
                {
                    classWorkers = 1;
                    innerWorkers = workers;
                }

                if (classWorkers == 1)
                {
                    for (int c = 0; c < MovementTables.ClassCount; c++)
                    {
                        if (buildHierarchy && !UniformCostGrid(costs.CellTicks[c]))
                        {
                            hierarchies[c] = GridHierarchy.Build(terrain, costs.CellTicks[c], clusterSize, innerWorkers);
                        }

                        if (config.LandmarkCount > 0 && graph.NodeCount > 0)
                        {
                            landmarks[c] = RoadLandmarks.Build(graph, costs.RoadTicks[c], config.LandmarkCount);
                        }
                    }
                }
                else
                {
                    ParallelOptions options = new ParallelOptions();
                    options.MaxDegreeOfParallelism = classWorkers;
                    Parallel.For(0, MovementTables.ClassCount, options, delegate (int c)
                    {
                        if (buildHierarchy && !UniformCostGrid(costs.CellTicks[c]))
                        {
                            hierarchies[c] = GridHierarchy.Build(terrain, costs.CellTicks[c], clusterSize, innerWorkers);
                        }

                        if (config.LandmarkCount > 0 && graph.NodeCount > 0)
                        {
                            landmarks[c] = RoadLandmarks.Build(graph, costs.RoadTicks[c], config.LandmarkCount);
                        }
                    });
                }

                if (buildHierarchy && hierarchyCacheDirectory != null && hierarchyCacheDirectory.Length > 0)
                {
                    HierarchyCache.TrySave(
                        hierarchyCacheDirectory,
                        terrainHash,
                        tableHash,
                        clusterSize,
                        terrain.Width,
                        terrain.Height,
                        hierarchies);
                }
            }

            if (cacheHit)
            {
                ParallelOptions landOptions = new ParallelOptions();
                landOptions.MaxDegreeOfParallelism = workers;
                Parallel.For(0, MovementTables.ClassCount, landOptions, delegate (int c)
                {
                    if (config.LandmarkCount > 0 && graph.NodeCount > 0)
                    {
                        landmarks[c] = RoadLandmarks.Build(graph, costs.RoadTicks[c], config.LandmarkCount);
                    }
                });
            }

            hierClock.Stop();
            int absNodes = 0;
            int absEdges = 0;
            long hierBytes = 0;
            long landBytes = 0;
            for (int c = 0; c < MovementTables.ClassCount; c++)
            {
                if (hierarchies[c] != null)
                {
                    if (hierarchies[c].NodeCount > absNodes)
                    {
                        absNodes = hierarchies[c].NodeCount;
                        absEdges = hierarchies[c].AdjNeighbor.Length;
                        hierBytes = hierarchies[c].EstimateBytes;
                    }
                }

                if (landmarks[c] != null && landmarks[c].EstimateBytes > landBytes)
                {
                    landBytes = landmarks[c].EstimateBytes;
                }
            }

            MovementLoadStats stats = new MovementLoadStats
            {
                ClusterSize = clusterSize,
                ClusterCount = buildHierarchy ? clustersX * clustersY : 1,
                AbstractNodesPerClass = absNodes,
                AbstractDirectedEdgesPerClass = absEdges,
                LandmarkCount = config.LandmarkCount,
                RoadNodes = graph.NodeCount,
                CostGridBytesPerClass = (long)terrain.CellCount * 4,
                HierarchyBytesPerClass = hierBytes,
                LandmarkBytesPerClass = landBytes,
                BuildWorkers = workers,
                HierarchyCacheHit = cacheHit,
                CostGridMilliseconds = costClock.ElapsedMilliseconds,
                HierarchyMilliseconds = hierClock.ElapsedMilliseconds
            };

            loadClock.Stop();
            stats.LoadMilliseconds = loadClock.ElapsedMilliseconds;
            return new MovementService(graph, terrain, tables, costs, config, hierarchies, landmarks, stats);
        }

        public static MovementService Create(byte[] mapBlob, byte[] terrainBlob, MovementTables tables)
        {
            return Create(mapBlob, terrainBlob, tables, MovementServiceConfig.Default);
        }

        public RoadGraph Graph
        {
            get { return _graph; }
        }

        public TerrainGrid Terrain
        {
            get { return _terrain; }
        }

        internal ulong HierarchyFingerprint(MovementClass clas)
        {
            GridHierarchy h = _hierarchies[(int)clas];
            return h == null ? 0UL : h.Fingerprint();
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

        public int ExpansionsPerTick
        {
            get { return _config.ExpansionsPerTick; }
        }

        public int Tick { get; private set; }

        public int LastExpansions { get; private set; }

        public int ExpansionsThisTick { get; private set; }

        public bool HasInFlightSearch
        {
            get { return _sliceActive || _combActive; }
        }

        public MovementLoadStats LoadStats
        {
            get { return _loadStats; }
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
                if (_sliceActive || _combActive)
                {
                    _pending.Insert(_pendingHead, new QueuedRequest(_sliceRequestId, _sliceRequest));
                    ClearSlice();
                }
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

            _searchExpansions = 0;
            PathResult result = SearchRoads(fromNodeId, toNodeId, clas, weights, true, out _);
            LastExpansions = _searchExpansions;
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

            _searchExpansions = 0;
            PathResult result = SearchOffRoad(fromCell, toCell, clas, PathKind.OffRoad, 0, true, out _);
            LastExpansions = _searchExpansions;
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

            _searchExpansions = 0;
            PathResult result = SearchCombined(fromXCm, fromYCm, toXCm, toYCm, fromCell, toCell, clas, weights);
            LastExpansions = _searchExpansions;
            _cache[key] = result;
            return result;
        }

        public long EstimateTravelTicks(PathResult path, MovementClass clas)
        {
            if (path == null || !path.Reachable)
            {
                return -1;
            }

            PathWaypoint[] points = path.Waypoints;
            if (points == null || points.Length == 0)
            {
                return -1;
            }

            int classIndex = (int)clas;
            int[] cellTicks = _costs.CellTicks[classIndex];
            int[] roadTicks = _costs.RoadTicks[classIndex];
            long total = 0;
            for (int i = 1; i < points.Length; i++)
            {
                long step = RecostStep(points[i - 1], points[i], cellTicks, roadTicks);
                if (step < 0)
                {
                    return -1;
                }

                total += step;
            }

            return total;
        }

        private long RecostStep(PathWaypoint from, PathWaypoint to, int[] cellTicks, int[] roadTicks)
        {
            if (from.Kind == PathSegmentKind.GridCell && to.Kind == PathSegmentKind.GridCell)
            {
                return RecostGridStep(from.Id, to.Id, cellTicks);
            }

            if (from.Kind == PathSegmentKind.RoadNode && to.Kind == PathSegmentKind.RoadNode)
            {
                return RecostRoadStep(from.Id, to.Id, roadTicks);
            }

            return 0;
        }

        private long RecostGridStep(int fromCell, int toCell, int[] cellTicks)
        {
            int cells = cellTicks.Length;
            if ((uint)fromCell >= (uint)cells || (uint)toCell >= (uint)cells)
            {
                return -1;
            }

            if (fromCell == toCell)
            {
                return 0;
            }

            int width = _terrain.Width;
            int ax = fromCell % width;
            int ay = fromCell / width;
            int bx = toCell % width;
            int by = toCell / width;
            int dx = bx - ax;
            int dy = by - ay;
            int adx = dx < 0 ? -dx : dx;
            int ady = dy < 0 ? -dy : dy;
            if (adx > 1 || ady > 1)
            {
                return -1;
            }

            int step = cellTicks[toCell];
            if (step <= 0)
            {
                return -1;
            }

            if (adx == 1 && ady == 1)
            {
                int orthoA = ay * width + bx;
                int orthoB = by * width + ax;
                if (cellTicks[orthoA] <= 0 || cellTicks[orthoB] <= 0)
                {
                    return -1;
                }

                step = (int)((long)step * MovementConfig.Sqrt2Ppt / SimConfig.PptScale);
                if (step < 1)
                {
                    step = 1;
                }
            }

            return step;
        }

        private long RecostRoadStep(int fromNodeId, int toNodeId, int[] roadTicks)
        {
            if (!_graph.TryGetNodeIndex(fromNodeId, out int fromIndex)
                || !_graph.TryGetNodeIndex(toNodeId, out int toIndex))
            {
                return -1;
            }

            if (fromIndex == toIndex)
            {
                return 0;
            }

            int best = -1;
            int start = _graph.AdjOffset[fromIndex];
            int end = _graph.AdjOffset[fromIndex + 1];
            for (int a = start; a < end; a++)
            {
                if (_graph.AdjNeighborIndex[a] != toIndex)
                {
                    continue;
                }

                int edge = _graph.AdjEdgeIndex[a];
                if (_graph.EdgeCut[edge])
                {
                    continue;
                }

                int ticks = roadTicks[edge];
                if (ticks <= 0)
                {
                    continue;
                }

                if (best < 0 || ticks < best)
                {
                    best = ticks;
                }
            }

            return best;
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
        /// Spends up to <see cref="ExpansionsPerTick"/> closed nodes (work units in
        /// sim state, never wall clock) and up to <see cref="PathRequestsPerTick"/>
        /// completions. Long searches resume next tick; <see cref="PathResult.CommitTick"/>
        /// is the tick they finish. Cache hits do not consume either budget.
        /// </summary>
        public int ProcessQueue()
        {
            Tick++;
            ExpansionsThisTick = 0;
            CompactPending();
            int ran = 0;
            int requestCap = _config.PathRequestsPerTick;
            int expandCap = _config.ExpansionsPerTick;

            if (_sliceActive || _combActive)
            {
                PathResult done = ContinueInFlight(out bool paused);
                if (paused)
                {
                    LastExpansions = ExpansionsThisTick;
                    return ran;
                }

                CommitSlice(done);
                ran++;
            }

            while (_pendingHead < _pending.Count && ran < requestCap && ExpansionsThisTick < expandCap)
            {
                QueuedRequest q = _pending[_pendingHead];
                CacheKey key = KeyFor(q.Request);
                if (_cache.TryGetValue(key, out PathResult hit))
                {
                    _pendingHead++;
                    _results[q.Id] = hit;
                    continue;
                }

                _pendingHead++;
                _sliceRequestId = q.Id;
                _sliceRequest = q.Request;
                PathResult result = StartQueued(q.Request, out bool paused);
                if (paused)
                {
                    LastExpansions = ExpansionsThisTick;
                    return ran;
                }

                CommitSlice(result);
                ran++;
            }

            LastExpansions = ExpansionsThisTick;
            return ran;
        }

        private PathResult StartQueued(PathRequest request, out bool paused)
        {
            switch (request.Kind)
            {
                case PathQueryKind.OnRoads:
                    return SearchRoads(request.FromA, request.ToA, request.Class, request.Weights, false, out paused);
                case PathQueryKind.OffRoad:
                    if (!TryCells(request.FromA, request.FromB, request.ToA, request.ToB, out int fromCell, out int toCell))
                    {
                        paused = false;
                        return Unreachable(0);
                    }

                    return SearchOffRoad(fromCell, toCell, request.Class, PathKind.OffRoad, 0, false, out paused);
                default:
                    return StartCombined(request, out paused);
            }
        }

        private PathResult ContinueInFlight(out bool paused)
        {
            if (_sliceActive)
            {
                PathResult inner = ContinueInner(false, out paused);
                if (paused)
                {
                    return PathResult.Unreachable;
                }

                if (_combActive)
                {
                    return CombinedGot(inner, out paused);
                }

                return inner;
            }

            if (_combActive)
            {
                return ContinueCombined(out paused);
            }

            paused = false;
            return Unreachable(0);
        }

        private PathResult ContinueInner(bool unlimited, out bool paused)
        {
            switch (_sliceMode)
            {
                case ModeGrid:
                    return ContinueGrid(unlimited, out paused);
                case ModeRoad:
                    return ContinueRoads(unlimited, out paused);
                default:
                    return ContinueHpa(unlimited, out paused);
            }
        }

        private void CommitSlice(PathResult result)
        {
            result = result.WithCommitTick(Tick);
            CacheKey key = KeyFor(_sliceRequest);
            _cache[key] = result;
            _results[_sliceRequestId] = result;
            ClearSlice();
        }

        private void ClearSlice()
        {
            _sliceActive = false;
            _combActive = false;
            _sliceMode = ModeNone;
            _heap.Clear();
        }

        private void ClearInner()
        {
            _sliceActive = false;
            _sliceMode = ModeNone;
            _heap.Clear();
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
            PathResult offRoad = SearchOffRoad(fromCell, toCell, clas, PathKind.OffRoad, 0, true, out _);
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
                    PathResult toEntry = SearchOffRoad(fromCell, entryCell, clas, PathKind.OffRoad, 0, true, out _);
                    if (!toEntry.Reachable)
                    {
                        continue;
                    }

                    for (int t = 0; t < goalCount; t++)
                    {
                        int exitIndex = _goalGateways[t];
                        int exitCell = _costs.NodeCellIndex[exitIndex];
                        int exitId = _graph.NodeId[exitIndex];
                        PathResult road = SearchRoads(entryId, exitId, clas, weights, true, out _);
                        if (!road.Reachable)
                        {
                            continue;
                        }

                        PathResult fromExit = SearchOffRoad(exitCell, toCell, clas, PathKind.OffRoad, 0, true, out _);
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

        private PathResult SearchOffRoad(
            int fromCell,
            int toCell,
            MovementClass clas,
            PathKind kind,
            int graphVersion,
            bool unlimited,
            out bool paused)
        {
            paused = false;
            int classIndex = (int)clas;
            int[] ticks = _costs.CellTicks[classIndex];
            if (ticks[fromCell] <= 0 || ticks[toCell] <= 0)
            {
                return Unreachable(graphVersion);
            }

            if (fromCell == toCell)
            {
                paused = false;
                return GridTrivial(fromCell, kind, graphVersion);
            }

            if (OctileFitsBudget(fromCell, toCell, unlimited)
                && TryCanonicalOctile(fromCell, toCell, ticks, kind, graphVersion, out PathResult octile))
            {
                paused = false;
                return octile;
            }

            _sliceClass = clas;
            _slicePathKind = kind;
            _sliceGraphVersion = graphVersion;
            _sliceFrom = fromCell;
            _sliceTo = toCell;
            _sliceMaxSpeed = _costs.MaxGridSpeed[classIndex];
            GridHierarchy hier = _hierarchies[classIndex];
            if (hier == null)
            {
                InitGridRect(fromCell, toCell, 0, 0, _terrain.Width, _terrain.Height, false);
                _sliceMode = ModeGrid;
                _sliceActive = true;
                return ContinueGrid(unlimited, out paused);
            }

            return SearchHpa(hier, fromCell, toCell, clas, kind, graphVersion, unlimited, out paused);
        }

        private PathResult GridTrivial(int cell, PathKind kind, int graphVersion)
        {
            return PathResult.Create(true, 0, kind, graphVersion, new[] { CellWaypoint(cell) });
        }

        private bool TryCanonicalOctile(
            int fromCell,
            int toCell,
            int[] ticks,
            PathKind kind,
            int graphVersion,
            out PathResult result)
        {
            result = PathResult.Unreachable;
            int width = _terrain.Width;
            int height = _terrain.Height;
            int x = fromCell % width;
            int y = fromCell / width;
            int x1 = toCell % width;
            int y1 = toCell / width;
            _scratchWaypoints.Clear();
            _scratchWaypoints.Add(CellWaypoint(fromCell));
            long cost = 0;
            int steps = 0;
            int guard = width + height + 2;
            while ((x != x1 || y != y1) && guard-- > 0)
            {
                int sx = x1 > x ? 1 : x1 < x ? -1 : 0;
                int sy = y1 > y ? 1 : y1 < y ? -1 : 0;
                int nx = x + sx;
                int ny = y + sy;
                if (sx != 0 && sy != 0)
                {
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    {
                        return false;
                    }

                    int orthoA = y * width + nx;
                    int orthoB = ny * width + x;
                    if (ticks[orthoA] <= 0 || ticks[orthoB] <= 0)
                    {
                        return false;
                    }
                }
                else
                {
                    nx = x + sx;
                    ny = y + sy;
                }

                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                {
                    return false;
                }

                int next = ny * width + nx;
                int step = ticks[next];
                if (step <= 0)
                {
                    return false;
                }

                if (sx != 0 && sy != 0)
                {
                    step = (int)((long)step * MovementConfig.Sqrt2Ppt / SimConfig.PptScale);
                    if (step < 1)
                    {
                        step = 1;
                    }
                }

                cost += step;
                x = nx;
                y = ny;
                _scratchWaypoints.Add(CellWaypoint(next));
                steps++;
            }

            if (x != x1 || y != y1)
            {
                return false;
            }

            _searchExpansions += steps;
            ExpansionsThisTick += steps;
            result = PathResult.Create(true, cost, kind, graphVersion, _scratchWaypoints.ToArray());
            return true;
        }

        private bool OctileFitsBudget(int fromCell, int toCell, bool unlimited)
        {
            if (unlimited)
            {
                return true;
            }

            int width = _terrain.Width;
            int x0 = fromCell % width;
            int y0 = fromCell / width;
            int x1 = toCell % width;
            int y1 = toCell / width;
            int dx = x0 > x1 ? x0 - x1 : x1 - x0;
            int dy = y0 > y1 ? y0 - y1 : y1 - y0;
            int est = dx > dy ? dx : dy;
            int remaining = _config.ExpansionsPerTick - ExpansionsThisTick;
            return est <= remaining;
        }

        private PathResult SearchRoads(
            int fromNodeId,
            int toNodeId,
            MovementClass clas,
            RoadCostWeights weights,
            bool unlimited,
            out bool paused)
        {
            paused = false;
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

            _sliceClass = clas;
            _sliceWeights = weights;
            _sliceFrom = fromIndex;
            _sliceTo = toIndex;
            _sliceMaxSpeed = _costs.MaxRoadSpeed[(int)clas];
            _sliceMinWeight = MinWeight(weights);
            BeginSearch();
            _g[fromIndex] = 0;
            _parent[fromIndex] = fromIndex;
            _gen[fromIndex] = _searchId;
            long startH = HeuristicRoad(fromIndex, toIndex, _sliceMaxSpeed, _sliceMinWeight, (int)clas);
            _heap.Push(startH, TieH(startH), fromIndex);
            _sliceMode = ModeRoad;
            _sliceActive = true;
            return ContinueRoads(unlimited, out paused);
        }

        private PathResult ContinueRoads(bool unlimited, out bool paused)
        {
            paused = false;
            int classIndex = (int)_sliceClass;
            int[] edgeTicks = _costs.RoadTicks[classIndex];
            int toIndex = _sliceTo;
            int maxSpeed = _sliceMaxSpeed;
            int minWeight = _sliceMinWeight;
            RoadCostWeights weights = _sliceWeights;
            int cap = _config.ExpansionsPerTick;
            while (_heap.TryPop(out int current, out _))
            {
                if (_gen[current] != _searchId || _closed[current] == _searchId)
                {
                    continue;
                }

                if (!unlimited && ExpansionsThisTick >= cap)
                {
                    long hf = HeuristicRoad(current, toIndex, maxSpeed, minWeight, classIndex);
                    _heap.Push(_g[current] + hf, TieH(hf), current);
                    paused = true;
                    return PathResult.Unreachable;
                }

                _closed[current] = _searchId;
                _searchExpansions++;
                if (!unlimited)
                {
                    ExpansionsThisTick++;
                }

                if (current == toIndex)
                {
                    _sliceActive = false;
                    return ReconstructRoad(_sliceFrom, toIndex);
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

                    int edgeTick = edgeTicks[edge];
                    if (edgeTick <= 0)
                    {
                        continue;
                    }

                    bool span = (_graph.EdgeFlags[edge] & (ushort)RoadEdgeFlags.BridgeSpan) != 0;
                    int weight = weights.PptFor((RoadTier)_graph.EdgeTier[edge], span);
                    if (weight <= 0)
                    {
                        continue;
                    }

                    long step = (long)edgeTick * weight / SimConfig.PptScale;
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
                        long f = ng + HeuristicRoad(next, toIndex, maxSpeed, minWeight, classIndex);
                        _heap.Push(f, TieH(f - ng), next);
                    }
                }
            }

            _sliceActive = false;
            return Unreachable(_graph.Version);
        }

        private PathResult ContinueGrid(bool unlimited, out bool paused)
        {
            paused = false;
            int[] ticks = _costs.CellTicks[(int)_sliceClass];
            StepEnd end = StepGrid(unlimited, false, _sliceStopLocal, _sliceTo, _sliceMaxSpeed, ticks);
            if (end == StepEnd.Paused)
            {
                paused = true;
                return PathResult.Unreachable;
            }

            _sliceActive = false;
            if (end == StepEnd.Found)
            {
                return ReconstructGridLocal(_sliceFrom, _sliceTo, _slicePathKind, _sliceGraphVersion);
            }

            return Unreachable(_sliceGraphVersion);
        }

        private void InitGridRect(int fromCell, int toCell, int x0, int y0, int x1, int y1, bool dijkstra)
        {
            _sliceX0 = x0;
            _sliceY0 = y0;
            _sliceX1 = x1;
            _sliceY1 = y1;
            _sliceFrom = fromCell;
            _sliceTo = toCell;
            int localW = x1 - x0;
            int fromLocal = LocalOfCell(fromCell, x0, y0, localW);
            _sliceStopLocal = dijkstra ? -1 : LocalOfCell(toCell, x0, y0, localW);
            BeginSearch();
            _g[fromLocal] = 0;
            _parent[fromLocal] = fromLocal;
            _gen[fromLocal] = _searchId;
            long h = dijkstra ? 0 : HeuristicGrid(fromCell, toCell, _sliceMaxSpeed);
            _heap.Push(h, fromCell, fromLocal);
        }

        private enum StepEnd : byte
        {
            Found = 0,
            Exhausted = 1,
            Paused = 2
        }

        private StepEnd StepGrid(bool unlimited, bool dijkstra, int stopLocal, int goalCell, int maxSpeed, int[] ticks)
        {
            int width = _terrain.Width;
            int x0 = _sliceX0;
            int y0 = _sliceY0;
            int x1 = _sliceX1;
            int y1 = _sliceY1;
            int localW = x1 - x0;
            int cap = _config.ExpansionsPerTick;
            while (_heap.TryPop(out int current, out _))
            {
                if (_gen[current] != _searchId || _closed[current] == _searchId)
                {
                    continue;
                }

                if (!unlimited && ExpansionsThisTick >= cap)
                {
                    int cell = CellOfLocal(current, x0, y0, localW);
                    long f = _g[current] + (dijkstra ? 0 : HeuristicGrid(cell, goalCell, maxSpeed));
                    _heap.Push(f, cell, current);
                    return StepEnd.Paused;
                }

                _closed[current] = _searchId;
                _searchExpansions++;
                if (!unlimited)
                {
                    ExpansionsThisTick++;
                }

                if (stopLocal >= 0 && current == stopLocal)
                {
                    return StepEnd.Found;
                }

                int cx = x0 + current % localW;
                int cy = y0 + current / localW;
                int currentCell = cy * width + cx;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DirX[d];
                    int ny = cy + DirY[d];
                    if (nx < x0 || ny < y0 || nx >= x1 || ny >= y1)
                    {
                        continue;
                    }

                    int nextCell = ny * width + nx;
                    int step = ticks[nextCell];
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

                    int nextLocal = (nx - x0) + (ny - y0) * localW;
                    long ng = _g[current] + step;
                    bool better = _gen[nextLocal] != _searchId || ng < _g[nextLocal];
                    if (!better && ng == _g[nextLocal])
                    {
                        int parentCell = CellOfLocal(_parent[nextLocal], x0, y0, localW);
                        better = currentCell < parentCell;
                    }

                    if (better)
                    {
                        _gen[nextLocal] = _searchId;
                        _g[nextLocal] = ng;
                        _parent[nextLocal] = current;
                        long f = ng + (dijkstra ? 0 : HeuristicGrid(nextCell, goalCell, maxSpeed));
                        _heap.Push(f, nextCell, nextLocal);
                    }
                }
            }

            return stopLocal < 0 ? StepEnd.Found : StepEnd.Exhausted;
        }

        private PathResult SearchHpa(
            GridHierarchy hier,
            int fromCell,
            int toCell,
            MovementClass clas,
            PathKind kind,
            int graphVersion,
            bool unlimited,
            out bool paused)
        {
            paused = false;
            _hpaFromCell = fromCell;
            _hpaToCell = toCell;
            _sliceClass = clas;
            _slicePathKind = kind;
            _sliceGraphVersion = graphVersion;
            _sliceMaxSpeed = _costs.MaxGridSpeed[(int)clas];
            _hpaStartCluster = hier.ClusterOfCell(fromCell, _terrain.Width);
            _hpaGoalCluster = hier.ClusterOfCell(toCell, _terrain.Width);
            _hpaDirectCost = GridHierarchy.Inf;
            _hpaStartNode = hier.NodeCount;
            _hpaGoalNode = hier.NodeCount + 1;
            for (int i = 0; i < hier.NodeCount; i++)
            {
                _hpaStartCost[i] = GridHierarchy.Inf;
                _hpaGoalCost[i] = GridHierarchy.Inf;
            }

            hier.ClusterBounds(_hpaStartCluster, _terrain.Width, _terrain.Height, out int x0, out int y0, out int x1, out int y1);
            InitGridRect(fromCell, fromCell, x0, y0, x1, y1, true);
            _sliceMode = ModeHpaFillStart;
            _sliceActive = true;
            return ContinueHpa(unlimited, out paused);
        }

        private PathResult ContinueHpa(bool unlimited, out bool paused)
        {
            paused = false;
            GridHierarchy hier = _hierarchies[(int)_sliceClass];
            int[] ticks = _costs.CellTicks[(int)_sliceClass];
            while (true)
            {
                if (!unlimited && ExpansionsThisTick >= _config.ExpansionsPerTick)
                {
                    paused = true;
                    return PathResult.Unreachable;
                }

                switch (_sliceMode)
                {
                    case ModeHpaFillStart:
                    {
                        StepEnd fill = StepGrid(unlimited, true, -1, _hpaFromCell, _sliceMaxSpeed, ticks);
                        if (fill == StepEnd.Paused)
                        {
                            paused = true;
                            return PathResult.Unreachable;
                        }

                        ExtractClusterCosts(hier, _hpaStartCluster, _hpaStartCost);
                        if (_hpaStartCluster == _hpaGoalCluster)
                        {
                            int localW = _sliceX1 - _sliceX0;
                            int goalLocal = LocalOfCell(_hpaToCell, _sliceX0, _sliceY0, localW);
                            if (_gen[goalLocal] == _searchId)
                            {
                                _hpaDirectCost = _g[goalLocal];
                            }
                        }

                        hier.ClusterBounds(_hpaGoalCluster, _terrain.Width, _terrain.Height, out int gx0, out int gy0, out int gx1, out int gy1);
                        InitGridRect(_hpaToCell, _hpaToCell, gx0, gy0, gx1, gy1, true);
                        _sliceMode = ModeHpaFillGoal;
                        break;
                    }

                    case ModeHpaFillGoal:
                    {
                        StepEnd fill = StepGrid(unlimited, true, -1, _hpaToCell, _sliceMaxSpeed, ticks);
                        if (fill == StepEnd.Paused)
                        {
                            paused = true;
                            return PathResult.Unreachable;
                        }

                        ExtractClusterCosts(hier, _hpaGoalCluster, _hpaGoalCost);
                        InitAbstract(hier);
                        _sliceMode = ModeHpaAbstract;
                        break;
                    }

                    case ModeHpaAbstract:
                    {
                        StepEnd abs = StepAbstract(hier, unlimited);
                        if (abs == StepEnd.Paused)
                        {
                            paused = true;
                            return PathResult.Unreachable;
                        }

                        if (abs != StepEnd.Found)
                        {
                            _sliceActive = false;
                            return Unreachable(_sliceGraphVersion);
                        }

                        _hpaCost = _g[_hpaGoalNode];
                        BuildAbsPath(hier);
                        _sliceWaypoints.Clear();
                        _hpaRefineStep = 0;
                        _sliceMode = ModeHpaRefineSetup;
                        break;
                    }

                    case ModeHpaRefineSetup:
                    {
                        if (!StartRefineHop(hier, unlimited, out paused, out PathResult done))
                        {
                            return paused ? PathResult.Unreachable : done;
                        }

                        _sliceMode = ModeHpaRefine;
                        break;
                    }

                    default:
                    {
                        StepEnd hop = StepGrid(unlimited, false, _sliceStopLocal, _sliceTo, _sliceMaxSpeed, ticks);
                        if (hop == StepEnd.Paused)
                        {
                            paused = true;
                            return PathResult.Unreachable;
                        }

                        if (hop != StepEnd.Found)
                        {
                            _sliceActive = false;
                            return Unreachable(_sliceGraphVersion);
                        }

                        AppendGridReconstruction();
                        _hpaRefineStep++;
                        _sliceMode = ModeHpaRefineSetup;
                        break;
                    }
                }
            }
        }

        private void ExtractClusterCosts(GridHierarchy hier, int cluster, long[] dest)
        {
            int localW = _sliceX1 - _sliceX0;
            int start = hier.ClusterNodeOffset[cluster];
            int end = hier.ClusterNodeOffset[cluster + 1];
            for (int i = start; i < end; i++)
            {
                int node = hier.ClusterNodes[i];
                int local = LocalOfCell(hier.NodeCell[node], _sliceX0, _sliceY0, localW);
                dest[node] = _gen[local] == _searchId ? _g[local] : GridHierarchy.Inf;
            }
        }

        private void InitAbstract(GridHierarchy hier)
        {
            BeginSearch();
            int start = _hpaStartNode;
            _g[start] = 0;
            _parent[start] = start;
            _gen[start] = _searchId;
            long h0 = HeuristicGrid(_hpaFromCell, _hpaToCell, _sliceMaxSpeed);
            _heap.Push(h0, TieH(h0), start);
        }

        private StepEnd StepAbstract(GridHierarchy hier, bool unlimited)
        {
            int cap = _config.ExpansionsPerTick;
            int start = _hpaStartNode;
            int goal = _hpaGoalNode;
            int maxSpeed = _sliceMaxSpeed;
            while (_heap.TryPop(out int current, out _))
            {
                if (_gen[current] != _searchId || _closed[current] == _searchId)
                {
                    continue;
                }

                if (!unlimited && ExpansionsThisTick >= cap)
                {
                    long hf = current == goal || current == start
                        ? (current == goal ? 0 : HeuristicGrid(_hpaFromCell, _hpaToCell, maxSpeed))
                        : HeuristicGrid(hier.NodeCell[current], _hpaToCell, maxSpeed);
                    _heap.Push(_g[current] + hf, TieH(hf), current);
                    return StepEnd.Paused;
                }

                _closed[current] = _searchId;
                _searchExpansions++;
                if (!unlimited)
                {
                    ExpansionsThisTick++;
                }

                if (current == goal)
                {
                    return StepEnd.Found;
                }

                if (current == start)
                {
                    int s0 = hier.ClusterNodeOffset[_hpaStartCluster];
                    int s1 = hier.ClusterNodeOffset[_hpaStartCluster + 1];
                    for (int i = s0; i < s1; i++)
                    {
                        int node = hier.ClusterNodes[i];
                        long step = _hpaStartCost[node];
                        if (step >= GridHierarchy.Inf)
                        {
                            continue;
                        }

                        RelaxAbstract(node, current, step, HeuristicGrid(hier.NodeCell[node], _hpaToCell, maxSpeed));
                    }

                    if (_hpaDirectCost < GridHierarchy.Inf)
                    {
                        RelaxAbstract(goal, current, _hpaDirectCost, 0);
                    }

                    continue;
                }

                int adjStart = hier.AdjOffset[current];
                int adjEnd = hier.AdjOffset[current + 1];
                for (int a = adjStart; a < adjEnd; a++)
                {
                    int next = hier.AdjNeighbor[a];
                    RelaxAbstract(next, current, hier.AdjCost[a], HeuristicGrid(hier.NodeCell[next], _hpaToCell, maxSpeed));
                }

                if (hier.NodeCluster[current] == _hpaGoalCluster)
                {
                    long step = _hpaGoalCost[current];
                    if (step < GridHierarchy.Inf)
                    {
                        RelaxAbstract(goal, current, step, 0);
                    }
                }
            }

            return StepEnd.Exhausted;
        }

        private void RelaxAbstract(int next, int current, long step, long h)
        {
            long ng = _g[current] + step;
            if (_gen[next] != _searchId || ng < _g[next] || (ng == _g[next] && current < _parent[next]))
            {
                _gen[next] = _searchId;
                _g[next] = ng;
                _parent[next] = current;
                _heap.Push(ng + h, TieH(h), next);
            }
        }

        private void BuildAbsPath(GridHierarchy hier)
        {
            _hpaAbsLen = 0;
            int current = _parent[_hpaGoalNode];
            int start = _hpaStartNode;
            int guard = hier.NodeCount + 4;
            while (current != start && guard-- > 0)
            {
                if (_hpaAbsLen >= _hpaAbsPath.Length)
                {
                    break;
                }

                _hpaAbsPath[_hpaAbsLen++] = current;
                current = _parent[current];
            }

            int i = 0;
            int j = _hpaAbsLen - 1;
            while (i < j)
            {
                int tmp = _hpaAbsPath[i];
                _hpaAbsPath[i] = _hpaAbsPath[j];
                _hpaAbsPath[j] = tmp;
                i++;
                j--;
            }
        }

        private bool StartRefineHop(GridHierarchy hier, bool unlimited, out bool paused, out PathResult done)
        {
            paused = false;
            done = PathResult.Unreachable;
            int len = _hpaAbsLen;
            int total = len == 0 ? 1 : len + 1;
            while (_hpaRefineStep < total)
            {
                if (!unlimited && ExpansionsThisTick >= _config.ExpansionsPerTick)
                {
                    paused = true;
                    return false;
                }

                int fromCell;
                int toCell;
                int cluster;
                bool inter = false;
                if (len == 0)
                {
                    fromCell = _hpaFromCell;
                    toCell = _hpaToCell;
                    cluster = _hpaStartCluster;
                }
                else if (_hpaRefineStep == 0)
                {
                    fromCell = _hpaFromCell;
                    toCell = hier.NodeCell[_hpaAbsPath[0]];
                    cluster = _hpaStartCluster;
                }
                else if (_hpaRefineStep == len)
                {
                    fromCell = hier.NodeCell[_hpaAbsPath[len - 1]];
                    toCell = _hpaToCell;
                    cluster = _hpaGoalCluster;
                }
                else
                {
                    int a = _hpaAbsPath[_hpaRefineStep - 1];
                    int b = _hpaAbsPath[_hpaRefineStep];
                    fromCell = hier.NodeCell[a];
                    toCell = hier.NodeCell[b];
                    cluster = hier.NodeCluster[a];
                    inter = IsInterEdge(hier, a, b);
                }

                if (fromCell == toCell)
                {
                    AppendCellWaypoint(fromCell);
                    _hpaRefineStep++;
                    continue;
                }

                if (inter)
                {
                    AppendCellWaypoint(fromCell);
                    AppendCellWaypoint(toCell);
                    _hpaRefineStep++;
                    continue;
                }

                int[] ticks = _costs.CellTicks[(int)_sliceClass];
                if (OctileFitsBudget(fromCell, toCell, unlimited)
                    && TryCanonicalOctile(fromCell, toCell, ticks, _slicePathKind, _sliceGraphVersion, out PathResult hop))
                {
                    AppendWaypoints(_sliceWaypoints, hop.Waypoints);
                    _hpaRefineStep++;
                    continue;
                }

                hier.ClusterBounds(cluster, _terrain.Width, _terrain.Height, out int x0, out int y0, out int x1, out int y1);
                InitGridRect(fromCell, toCell, x0, y0, x1, y1, false);
                _sliceMode = ModeHpaRefine;
                return true;
            }

            _sliceActive = false;
            PathWaypoint[] points = _sliceWaypoints.Count == 0
                ? new[] { CellWaypoint(_hpaFromCell) }
                : _sliceWaypoints.ToArray();
            done = PathResult.Create(true, _hpaCost, _slicePathKind, _sliceGraphVersion, points);
            return false;
        }

        private static bool IsInterEdge(GridHierarchy hier, int from, int to)
        {
            int start = hier.AdjOffset[from];
            int end = hier.AdjOffset[from + 1];
            for (int i = start; i < end; i++)
            {
                if (hier.AdjNeighbor[i] == to)
                {
                    return hier.AdjInter[i] != 0;
                }
            }

            return false;
        }

        private void AppendGridReconstruction()
        {
            _scratchWaypoints.Clear();
            ReconstructGridIntoScratch();
            AppendWaypoints(_sliceWaypoints, _scratchWaypoints.Count == 0 ? Array.Empty<PathWaypoint>() : _scratchWaypoints.ToArray());
        }

        private void ReconstructGridIntoScratch()
        {
            int x0 = _sliceX0;
            int y0 = _sliceY0;
            int localW = _sliceX1 - x0;
            int fromLocal = LocalOfCell(_sliceFrom, x0, y0, localW);
            int toLocal = LocalOfCell(_sliceTo, x0, y0, localW);
            int current = toLocal;
            int guard = localW * (_sliceY1 - y0) + 2;
            while (guard-- > 0)
            {
                int x = x0 + current % localW;
                int y = y0 + current / localW;
                int cell = y * _terrain.Width + x;
                _scratchWaypoints.Add(CellWaypoint(cell));
                if (current == fromLocal)
                {
                    break;
                }

                current = _parent[current];
            }

            _scratchWaypoints.Reverse();
        }

        private PathResult ReconstructGridLocal(int fromCell, int toCell, PathKind kind, int graphVersion)
        {
            _scratchWaypoints.Clear();
            int savedFrom = _sliceFrom;
            int savedTo = _sliceTo;
            _sliceFrom = fromCell;
            _sliceTo = toCell;
            ReconstructGridIntoScratch();
            _sliceFrom = savedFrom;
            _sliceTo = savedTo;
            int localW = _sliceX1 - _sliceX0;
            int toLocal = LocalOfCell(toCell, _sliceX0, _sliceY0, localW);
            return PathResult.Create(true, _g[toLocal], kind, graphVersion, _scratchWaypoints.ToArray());
        }

        private void AppendCellWaypoint(int cell)
        {
            PathWaypoint p = CellWaypoint(cell);
            if (_sliceWaypoints.Count > 0)
            {
                PathWaypoint last = _sliceWaypoints[_sliceWaypoints.Count - 1];
                if (last.Kind == p.Kind && last.Id == p.Id)
                {
                    return;
                }
            }

            _sliceWaypoints.Add(p);
        }

        private PathWaypoint CellWaypoint(int cell)
        {
            int x = cell % _terrain.Width;
            int y = cell / _terrain.Width;
            return new PathWaypoint(PathSegmentKind.GridCell, cell, _terrain.CellCenterXCm(x), _terrain.CellCenterYCm(y));
        }

        private int LocalOfCell(int cell, int x0, int y0, int localW)
        {
            int x = cell % _terrain.Width;
            int y = cell / _terrain.Width;
            return (x - x0) + (y - y0) * localW;
        }

        private int CellOfLocal(int local, int x0, int y0, int localW)
        {
            int x = x0 + local % localW;
            int y = y0 + local / localW;
            return y * _terrain.Width + x;
        }

        private PathResult StartCombined(PathRequest request, out bool paused)
        {
            paused = false;
            if (!TryCells(request.FromA, request.FromB, request.ToA, request.ToB, out int fromCell, out int toCell))
            {
                return Unreachable(_graph.Version);
            }

            _combActive = true;
            _combPhase = 0;
            _combS = 0;
            _combT = 0;
            _combFromCell = fromCell;
            _combToCell = toCell;
            _combHaveHier = false;
            _combBestCost = 0;
            _combBestEntryId = int.MaxValue;
            _combBestExitId = int.MaxValue;
            _combBest = PathResult.Unreachable;
            _combOff = PathResult.Unreachable;
            _combStartCount = CollectGateways(request.FromA, request.FromB, request.Class, _startGateways);
            _combGoalCount = CollectGateways(request.ToA, request.ToB, request.Class, _goalGateways);
            return ContinueCombined(out paused);
        }

        private PathResult ContinueCombined(out bool paused)
        {
            paused = false;
            MovementClass clas = _sliceRequest.Class;
            RoadCostWeights weights = _sliceRequest.Weights;
            while (true)
            {
                if (_combPhase == 1 && _combS >= _combStartCount)
                {
                    return FinishCombined();
                }

                PathResult inner;
                if (_combPhase == 0)
                {
                    inner = SearchOffRoad(_combFromCell, _combToCell, clas, PathKind.OffRoad, 0, false, out paused);
                }
                else if (_combPhase == 1)
                {
                    int entryCell = _costs.NodeCellIndex[_startGateways[_combS]];
                    inner = SearchOffRoad(_combFromCell, entryCell, clas, PathKind.OffRoad, 0, false, out paused);
                }
                else if (_combPhase == 2)
                {
                    int entryId = _graph.NodeId[_startGateways[_combS]];
                    int exitId = _graph.NodeId[_goalGateways[_combT]];
                    inner = SearchRoads(entryId, exitId, clas, weights, false, out paused);
                }
                else
                {
                    int exitCell = _costs.NodeCellIndex[_goalGateways[_combT]];
                    inner = SearchOffRoad(exitCell, _combToCell, clas, PathKind.OffRoad, 0, false, out paused);
                }

                if (paused)
                {
                    return PathResult.Unreachable;
                }

                ApplyCombinedResult(inner);
            }
        }

        private PathResult CombinedGot(PathResult inner, out bool paused)
        {
            ApplyCombinedResult(inner);
            return ContinueCombined(out paused);
        }

        private void ApplyCombinedResult(PathResult inner)
        {
            ClearInner();
            if (_combPhase == 0)
            {
                _combOff = inner;
                _combPhase = 1;
                _combS = 0;
                _combT = 0;
                if (_combStartCount == 0 || _combGoalCount == 0)
                {
                    _combS = _combStartCount;
                }

                return;
            }

            if (_combPhase == 1)
            {
                _combToEntry = inner;
                if (!inner.Reachable)
                {
                    AdvanceCombinedPair();
                    return;
                }

                _combPhase = 2;
                return;
            }

            if (_combPhase == 2)
            {
                _combRoad = inner;
                if (!inner.Reachable)
                {
                    AdvanceCombinedPair();
                    return;
                }

                _combPhase = 3;
                return;
            }

            if (inner.Reachable)
            {
                long cost = _combToEntry.TotalCostTicks + _combRoad.TotalCostTicks + inner.TotalCostTicks;
                int entryId = _graph.NodeId[_startGateways[_combS]];
                int exitId = _graph.NodeId[_goalGateways[_combT]];
                bool better = !_combHaveHier
                    || cost < _combBestCost
                    || (cost == _combBestCost && (entryId < _combBestEntryId || (entryId == _combBestEntryId && exitId < _combBestExitId)));
                if (better)
                {
                    _combBestCost = cost;
                    _combBestEntryId = entryId;
                    _combBestExitId = exitId;
                    _combHaveHier = true;
                    _combBest = Stitch(_combToEntry, _combRoad, inner, cost);
                }
            }

            AdvanceCombinedPair();
        }

        private void AdvanceCombinedPair()
        {
            _combT++;
            if (_combT >= _combGoalCount)
            {
                _combT = 0;
                _combS++;
            }

            _combPhase = 1;
        }

        private PathResult FinishCombined()
        {
            _combActive = false;
            if (_combOff.Reachable)
            {
                if (!_combHaveHier || _combOff.TotalCostTicks <= _combBestCost)
                {
                    return _combOff;
                }

                return _combBest;
            }

            if (_combHaveHier)
            {
                return _combBest;
            }

            return Unreachable(_graph.Version);
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

        private long HeuristicRoad(int fromIndex, int toIndex, int maxSpeed, int minWeightPpt, int classIndex)
        {
            if (maxSpeed <= 0 || minWeightPpt <= 0)
            {
                return 0;
            }

            long dx = (long)_graph.NodeXCm[fromIndex] - _graph.NodeXCm[toIndex];
            long dy = (long)_graph.NodeYCm[fromIndex] - _graph.NodeYCm[toIndex];
            long distSq = dx * dx + dy * dy;
            ulong dist = Int128Math.IntegerSqrt(0UL, (ulong)distSq);
            long euclid = ((long)dist * minWeightPpt) / ((long)maxSpeed * SimConfig.PptScale);
            RoadLandmarks landmarks = _landmarks[classIndex];
            if (landmarks == null || landmarks.Count == 0)
            {
                return euclid;
            }

            long alt = landmarks.Alt(fromIndex, toIndex);
            if (minWeightPpt < SimConfig.PptScale)
            {
                alt = alt * minWeightPpt / SimConfig.PptScale;
            }

            return alt > euclid ? alt : euclid;
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

        private static int TieH(long h)
        {
            if (h >= int.MaxValue)
            {
                return int.MaxValue;
            }

            if (h <= 0)
            {
                return 0;
            }

            return (int)h;
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

        /// <summary>
        /// HPA* is a no-op when every cell has the same tick cost: octile is exact
        /// (Air always; open field for every class). Mixed cover or any impassable
        /// cell keeps the hierarchy.
        /// </summary>
        private static bool UniformCostGrid(int[] ticks)
        {
            if (ticks.Length == 0)
            {
                return true;
            }

            int first = ticks[0];
            for (int i = 1; i < ticks.Length; i++)
            {
                if (ticks[i] != first)
                {
                    return false;
                }
            }

            return true;
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
