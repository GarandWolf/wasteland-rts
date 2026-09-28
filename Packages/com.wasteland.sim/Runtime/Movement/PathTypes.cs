using System;
using Wasteland.Sim.Hashing;

namespace Wasteland.Sim.Movement
{
    public readonly struct PathWaypoint
    {
        public readonly PathSegmentKind Kind;
        public readonly int Id;
        public readonly int XCm;
        public readonly int YCm;

        public PathWaypoint(PathSegmentKind kind, int id, int xCm, int yCm)
        {
            Kind = kind;
            Id = id;
            XCm = xCm;
            YCm = yCm;
        }
    }

    /// <summary>Immutable path answer. <see cref="Hash"/> is xxHash64 over the integer payload.</summary>
    public sealed class PathResult
    {
        public static readonly PathResult Unreachable = Create(
            false,
            0,
            PathKind.Unreachable,
            0,
            Array.Empty<PathWaypoint>());

        public readonly bool Reachable;
        public readonly long TotalCostTicks;
        public readonly PathKind Kind;
        public readonly int GraphVersion;
        public readonly PathWaypoint[] Waypoints;
        public readonly ulong Hash;
        public readonly int CommitTick;

        private PathResult(
            bool reachable,
            long totalCostTicks,
            PathKind kind,
            int graphVersion,
            PathWaypoint[] waypoints,
            ulong hash,
            int commitTick)
        {
            Reachable = reachable;
            TotalCostTicks = totalCostTicks;
            Kind = kind;
            GraphVersion = graphVersion;
            Waypoints = waypoints;
            Hash = hash;
            CommitTick = commitTick;
        }

        public static PathResult Create(
            bool reachable,
            long totalCostTicks,
            PathKind kind,
            int graphVersion,
            PathWaypoint[] waypoints,
            int commitTick = 0)
        {
            if (waypoints == null)
            {
                throw new ArgumentNullException(nameof(waypoints));
            }

            XxHash64Builder hash = new XxHash64Builder();
            hash.AddByte(reachable ? (byte)1 : (byte)0);
            hash.AddLong(totalCostTicks);
            hash.AddByte((byte)kind);
            hash.AddInt(graphVersion);
            hash.AddInt(waypoints.Length);
            for (int i = 0; i < waypoints.Length; i++)
            {
                PathWaypoint p = waypoints[i];
                hash.AddByte((byte)p.Kind);
                hash.AddInt(p.Id);
                hash.AddInt(p.XCm);
                hash.AddInt(p.YCm);
            }

            return new PathResult(reachable, totalCostTicks, kind, graphVersion, waypoints, hash.Digest(), commitTick);
        }

        public PathResult WithCommitTick(int commitTick)
        {
            return new PathResult(Reachable, TotalCostTicks, Kind, GraphVersion, Waypoints, Hash, commitTick);
        }
    }

    public readonly struct PathRequest
    {
        public readonly PathQueryKind Kind;
        public readonly MovementClass Class;
        public readonly int FromA;
        public readonly int FromB;
        public readonly int ToA;
        public readonly int ToB;
        public readonly RoadCostWeights Weights;

        public PathRequest(
            PathQueryKind kind,
            MovementClass clas,
            int fromA,
            int fromB,
            int toA,
            int toB,
            RoadCostWeights weights)
        {
            Kind = kind;
            Class = clas;
            FromA = fromA;
            FromB = fromB;
            ToA = toA;
            ToB = toB;
            Weights = weights;
        }

        public static PathRequest OnRoads(int fromNodeId, int toNodeId, MovementClass clas, RoadCostWeights weights)
        {
            return new PathRequest(PathQueryKind.OnRoads, clas, fromNodeId, 0, toNodeId, 0, weights);
        }

        public static PathRequest OffRoad(int fromXCm, int fromYCm, int toXCm, int toYCm, MovementClass clas)
        {
            return new PathRequest(PathQueryKind.OffRoad, clas, fromXCm, fromYCm, toXCm, toYCm, default);
        }

        public static PathRequest Combined(
            int fromXCm,
            int fromYCm,
            int toXCm,
            int toYCm,
            MovementClass clas,
            RoadCostWeights weights)
        {
            return new PathRequest(PathQueryKind.Combined, clas, fromXCm, fromYCm, toXCm, toYCm, weights);
        }
    }

    public readonly struct MovementServiceConfig
    {
        public readonly int PathRequestsPerTick;
        public readonly int MaxGateways;
        public readonly int ExpansionsPerTick;
        public readonly int ClusterSize;
        public readonly int LandmarkCount;

        public MovementServiceConfig(int pathRequestsPerTick, int maxGateways)
            : this(
                pathRequestsPerTick,
                maxGateways,
                MovementConfig.DefaultExpansionsPerTick,
                MovementConfig.DefaultClusterSize,
                MovementConfig.DefaultLandmarkCount)
        {
        }

        public MovementServiceConfig(
            int pathRequestsPerTick,
            int maxGateways,
            int expansionsPerTick,
            int clusterSize,
            int landmarkCount)
        {
            if (pathRequestsPerTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pathRequestsPerTick));
            }

            if (maxGateways < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxGateways));
            }

            if (expansionsPerTick < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(expansionsPerTick));
            }

            if (clusterSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(clusterSize));
            }

            if (landmarkCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(landmarkCount));
            }

            PathRequestsPerTick = pathRequestsPerTick;
            MaxGateways = maxGateways;
            ExpansionsPerTick = expansionsPerTick;
            ClusterSize = clusterSize;
            LandmarkCount = landmarkCount;
        }

        public static MovementServiceConfig Default
        {
            get
            {
                return new MovementServiceConfig(
                    MovementConfig.DefaultPathRequestsPerTick,
                    MovementConfig.DefaultMaxGateways,
                    MovementConfig.DefaultExpansionsPerTick,
                    MovementConfig.DefaultClusterSize,
                    MovementConfig.DefaultLandmarkCount);
            }
        }
    }

    /// <summary>Match-load hierarchy / landmark footprint for diagnostics and the benchmark.</summary>
    public sealed class MovementLoadStats
    {
        public int ClusterSize;
        public int ClusterCount;
        public int AbstractNodesPerClass;
        public int AbstractDirectedEdgesPerClass;
        public int LandmarkCount;
        public int RoadNodes;
        public long CostGridBytesPerClass;
        public long HierarchyBytesPerClass;
        public long LandmarkBytesPerClass;
        public long ScratchBytes;
        public long LoadMilliseconds;
    }
}
