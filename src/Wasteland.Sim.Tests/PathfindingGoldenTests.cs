using System.Collections.Generic;
using Wasteland.Sim.Movement;
using Xunit;


namespace Wasteland.Sim.Tests
{
    internal static class SyntheticMaps
    {
        public const int HillStartX = 15000;
        public const int HillStartY = 55000;
        public const int HillGoalX = 95000;
        public const int HillGoalY = 55000;

        public static MovementService HillSwitchback(MovementServiceConfig? config = null)
        {
            TerrainGridBuilder terrain = new TerrainGridBuilder(11, 11)
                .Fill(0, LandCover.Field)
                .FillRect(4, 0, 6, 10, 400, LandCover.Field);

            RoadGraphBuilder roads = new RoadGraphBuilder()
                .AddNode(1, 15000, 5000)
                .AddNode(2, 55000, 5000)
                .AddNode(3, 95000, 5000)
                .AddEdge(10, 1, 2, 40000, RoadTier.Paved, RoadSurface.Asphalt)
                .AddEdge(11, 2, 3, 40000, RoadTier.Paved, RoadSurface.Asphalt);

            return Load(roads, terrain, config);
        }

        public static MovementService BridgeNetwork()
        {
            TerrainGridBuilder terrain = new TerrainGridBuilder(4, 4).Fill(0, LandCover.Field);
            RoadGraphBuilder roads = new RoadGraphBuilder()
                .AddNode(1, 5000, 5000, RoadNodeFlags.BridgeHead)
                .AddNode(2, 25000, 5000, RoadNodeFlags.BridgeHead)
                .AddNode(3, 5000, 25000)
                .AddNode(4, 25000, 25000)
                .AddEdge(100, 1, 2, 20000, RoadTier.Paved, RoadSurface.BridgeDeck, RoadEdgeFlags.BridgeSpan)
                .AddEdge(101, 1, 3, 20000, RoadTier.Dirt, RoadSurface.Dirt)
                .AddEdge(102, 3, 4, 20000, RoadTier.Dirt, RoadSurface.Dirt)
                .AddEdge(103, 4, 2, 20000, RoadTier.Dirt, RoadSurface.Dirt);

            return Load(roads, terrain, null);
        }

        public static MovementService SlopeStrip(int middleSlope)
        {
            TerrainGridBuilder terrain = new TerrainGridBuilder(3, 1)
                .Fill(0, LandCover.Field)
                .SetCell(1, 0, middleSlope, LandCover.Field);
            RoadGraphBuilder roads = new RoadGraphBuilder().AddNode(1, 5000, 5000);
            return Load(roads, terrain, null);
        }

        private static MovementService Load(RoadGraphBuilder roads, TerrainGridBuilder terrain, MovementServiceConfig? config)
        {
            MovementServiceConfig cfg = config ?? MovementServiceConfig.Default;
            return MovementService.Create(roads.ToBlob(), terrain.ToBlob(), MovementTables.Default(), cfg);
        }
    }

    public sealed class PathfindingGoldenTests
    {
        [Fact]
        public void TruckTakesSwitchback_InfantryGoesOverTheHill()
        {
            MovementService svc = SyntheticMaps.HillSwitchback();
            RoadCostWeights weights = RoadCostWeights.Uniform();

            PathResult truck = svc.PathCombined(
                SyntheticMaps.HillStartX,
                SyntheticMaps.HillStartY,
                SyntheticMaps.HillGoalX,
                SyntheticMaps.HillGoalY,
                MovementClass.Wheeled,
                weights);

            PathResult infantry = svc.PathCombined(
                SyntheticMaps.HillStartX,
                SyntheticMaps.HillStartY,
                SyntheticMaps.HillGoalX,
                SyntheticMaps.HillGoalY,
                MovementClass.Foot,
                weights);

            Assert.True(truck.Reachable);
            Assert.True(infantry.Reachable);
            Assert.Equal(PathKind.Combined, truck.Kind);
            Assert.Equal(PathKind.OffRoad, infantry.Kind);
            Assert.True(ContainsRoadNode(truck, 1));
            Assert.True(ContainsRoadNode(truck, 2));
            Assert.True(ContainsRoadNode(truck, 3));
            Assert.False(ContainsRoadNode(infantry, 1));
            Assert.False(ContainsRoadNode(infantry, 2));
            Assert.False(ContainsRoadNode(infantry, 3));
            Assert.True(HasSouthRoadWaypoint(truck));
            Assert.False(HasSouthRoadWaypoint(infantry));
            for (int i = 0; i < infantry.Waypoints.Length; i++)
            {
                Assert.Equal(SyntheticMaps.HillStartY, infantry.Waypoints[i].YCm);
            }

            Assert.True(svc.EstimateTravelTicks(truck) > 0);
            Assert.True(svc.EstimateTravelTicks(infantry) > 0);
            Assert.True(infantry.TotalCostTicks > 0);
        }

        [Fact]
        public void Wheeled_CannotCross300PerMilleCell()
        {
            MovementService steep = SyntheticMaps.SlopeStrip(300);
            MovementService justOk = SyntheticMaps.SlopeStrip(299);
            PathResult wheeledSteep = steep.PathOffRoad(5000, 5000, 25000, 5000, MovementClass.Wheeled);
            PathResult wheeledOk = justOk.PathOffRoad(5000, 5000, 25000, 5000, MovementClass.Wheeled);
            PathResult tracked = steep.PathOffRoad(5000, 5000, 25000, 5000, MovementClass.Tracked);

            Assert.False(wheeledSteep.Reachable);
            Assert.Equal(-1, steep.EstimateTravelTicks(wheeledSteep));
            Assert.True(wheeledOk.Reachable);
            Assert.True(tracked.Reachable);
        }

        [Fact]
        public void CuttingBridge_ReroutesAndBumpsGraphVersion()
        {
            MovementService svc = SyntheticMaps.BridgeNetwork();
            RoadCostWeights weights = RoadCostWeights.Uniform();
            Assert.Equal(1, svc.GraphVersion);

            PathResult before = svc.PathOnRoads(1, 2, MovementClass.Wheeled, weights);
            Assert.True(before.Reachable);
            Assert.Equal(2, before.Waypoints.Length);
            Assert.Equal(1, before.Waypoints[0].Id);
            Assert.Equal(2, before.Waypoints[1].Id);
            Assert.Equal(1, before.GraphVersion);

            Assert.True(svc.CutBridge(100));
            Assert.Equal(2, svc.GraphVersion);
            Assert.False(svc.CutBridge(100));
            Assert.Equal(2, svc.GraphVersion);
            Assert.False(svc.CutBridge(101));

            PathResult after = svc.PathOnRoads(1, 2, MovementClass.Wheeled, weights);
            Assert.True(after.Reachable);
            Assert.Equal(2, after.GraphVersion);
            Assert.Equal(4, after.Waypoints.Length);
            Assert.Equal(1, after.Waypoints[0].Id);
            Assert.Equal(3, after.Waypoints[1].Id);
            Assert.Equal(4, after.Waypoints[2].Id);
            Assert.Equal(2, after.Waypoints[3].Id);
            Assert.True(after.TotalCostTicks > before.TotalCostTicks);
            Assert.NotEqual(before.Hash, after.Hash);
        }

        [Fact]
        public void IdenticalResults_AcrossRepeatedRuns_AndServices()
        {
            RoadCostWeights weights = RoadCostWeights.Uniform();
            ulong[] hashes = new ulong[6];
            for (int i = 0; i < hashes.Length; i++)
            {
                MovementService svc = SyntheticMaps.HillSwitchback();
                PathResult truck = svc.PathCombined(
                    SyntheticMaps.HillStartX,
                    SyntheticMaps.HillStartY,
                    SyntheticMaps.HillGoalX,
                    SyntheticMaps.HillGoalY,
                    MovementClass.Wheeled,
                    weights);
                PathResult inf = svc.PathCombined(
                    SyntheticMaps.HillStartX,
                    SyntheticMaps.HillStartY,
                    SyntheticMaps.HillGoalX,
                    SyntheticMaps.HillGoalY,
                    MovementClass.Foot,
                    weights);
                PathResult again = svc.PathCombined(
                    SyntheticMaps.HillStartX,
                    SyntheticMaps.HillStartY,
                    SyntheticMaps.HillGoalX,
                    SyntheticMaps.HillGoalY,
                    MovementClass.Wheeled,
                    weights);
                Assert.Equal(truck.Hash, again.Hash);
                hashes[i] = truck.Hash ^ inf.Hash;
            }

            for (int i = 1; i < hashes.Length; i++)
            {
                Assert.Equal(hashes[0], hashes[i]);
            }
        }

        [Fact]
        public void PathHashes_AreFrozenGoldens()
        {
            MovementService svc = SyntheticMaps.HillSwitchback();
            RoadCostWeights weights = RoadCostWeights.Uniform();
            PathResult truck = svc.PathCombined(
                SyntheticMaps.HillStartX,
                SyntheticMaps.HillStartY,
                SyntheticMaps.HillGoalX,
                SyntheticMaps.HillGoalY,
                MovementClass.Wheeled,
                weights);
            PathResult infantry = svc.PathCombined(
                SyntheticMaps.HillStartX,
                SyntheticMaps.HillStartY,
                SyntheticMaps.HillGoalX,
                SyntheticMaps.HillGoalY,
                MovementClass.Foot,
                weights);

            Assert.Equal(0x2B0B078CCC489F33UL, truck.Hash);
            Assert.Equal(0x9AFC305C35AF54D4UL, infantry.Hash);
        }

        private static bool ContainsRoadNode(PathResult path, int nodeId)
        {
            for (int i = 0; i < path.Waypoints.Length; i++)
            {
                if (path.Waypoints[i].Kind == PathSegmentKind.RoadNode && path.Waypoints[i].Id == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSouthRoadWaypoint(PathResult path)
        {
            for (int i = 0; i < path.Waypoints.Length; i++)
            {
                if (path.Waypoints[i].YCm == 5000)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class PathQueueTests
    {
        [Fact]
        public void Budget_ProcessesEightSearchesPerTick()
        {
            MovementService svc = SyntheticMaps.HillSwitchback(
                new MovementServiceConfig(pathRequestsPerTick: 8, maxGateways: 8));

            List<int> ids = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                int y = 5000 + i * 10000;
                ids.Add(svc.Enqueue(PathRequest.OffRoad(15000, y, 25000, y, MovementClass.Foot)));
            }

            Assert.Equal(10, svc.PendingCount);
            Assert.Equal(8, svc.ProcessQueue());
            Assert.Equal(2, svc.PendingCount);

            int resolved = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (svc.TryGetResult(ids[i], out _))
                {
                    resolved++;
                }
            }

            Assert.Equal(8, resolved);
            Assert.Equal(2, svc.ProcessQueue());
            Assert.Equal(0, svc.PendingCount);
            Assert.True(svc.TryGetResult(ids[9], out PathResult last));
            Assert.NotNull(last);
        }

        [Fact]
        public void CacheHit_DoesNotConsumeBudget()
        {
            MovementService svc = SyntheticMaps.HillSwitchback();
            PathRequest req = PathRequest.OffRoad(
                SyntheticMaps.HillStartX,
                SyntheticMaps.HillStartY,
                SyntheticMaps.HillGoalX,
                SyntheticMaps.HillGoalY,
                MovementClass.Foot);
            svc.PathOffRoad(
                SyntheticMaps.HillStartX,
                SyntheticMaps.HillStartY,
                SyntheticMaps.HillGoalX,
                SyntheticMaps.HillGoalY,
                MovementClass.Foot);

            int id = svc.Enqueue(req);
            Assert.Equal(0, svc.PendingCount);
            Assert.True(svc.TryGetResult(id, out PathResult result));
            Assert.True(result.Reachable);
            Assert.Equal(0, svc.ProcessQueue());
        }

        [Fact]
        public void CutBridge_InvalidatesRoadCache()
        {
            MovementService svc = SyntheticMaps.BridgeNetwork();
            PathResult before = svc.PathOnRoads(1, 2, MovementClass.Foot, RoadCostWeights.Uniform());
            int id = svc.Enqueue(PathRequest.OnRoads(1, 2, MovementClass.Foot, RoadCostWeights.Uniform()));
            Assert.True(svc.TryGetResult(id, out PathResult cached));
            Assert.Equal(before.Hash, cached.Hash);

            Assert.True(svc.CutBridge(100));
            PathResult after = svc.PathOnRoads(1, 2, MovementClass.Foot, RoadCostWeights.Uniform());
            Assert.NotEqual(before.Hash, after.Hash);
        }
    }
}
