using System;
using System.Collections.Generic;
using System.IO;
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
            TerrainGridBuilder terrain = new TerrainGridBuilder(11, 11, MovementConfig.SyntheticFixtureCellSizeCm)
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
            TerrainGridBuilder terrain = new TerrainGridBuilder(4, 4, MovementConfig.SyntheticFixtureCellSizeCm).Fill(0, LandCover.Field);
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
            TerrainGridBuilder terrain = new TerrainGridBuilder(3, 1, MovementConfig.SyntheticFixtureCellSizeCm)
                .Fill(0, LandCover.Field)
                .SetCell(1, 0, middleSlope, LandCover.Field);
            RoadGraphBuilder roads = new RoadGraphBuilder().AddNode(1, 5000, 5000);
            return Load(roads, terrain, null);
        }

        public static MovementService OpenTerrain(int width, int height, MovementServiceConfig config)
        {
            TerrainGridBuilder terrain = new TerrainGridBuilder(width, height, MovementConfig.SyntheticFixtureCellSizeCm).Fill(0, LandCover.Field);
            RoadGraphBuilder roads = new RoadGraphBuilder().AddNode(1, 5000, 5000);
            return Load(roads, terrain, config);
        }

        public static MovementService WalledTerrain(int width, int height, MovementServiceConfig config)
        {
            TerrainGridBuilder terrain = new TerrainGridBuilder(width, height, MovementConfig.SyntheticFixtureCellSizeCm).Fill(0, LandCover.Field);
            int wallX = width / 2;
            int gapY = (height * 5) / 6;
            for (int y = 0; y < height; y++)
            {
                if (y >= gapY)
                {
                    continue;
                }

                terrain.SetCell(wallX, y, 0, LandCover.Water);
            }

            RoadGraphBuilder roads = new RoadGraphBuilder().AddNode(1, 5000, 5000);
            return Load(roads, terrain, config);
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

            Assert.True(svc.EstimateTravelTicks(truck, MovementClass.Wheeled) > 0);
            Assert.True(svc.EstimateTravelTicks(infantry, MovementClass.Foot) > 0);
            Assert.Equal(truck.TotalCostTicks, svc.EstimateTravelTicks(truck, MovementClass.Wheeled));
            Assert.Equal(infantry.TotalCostTicks, svc.EstimateTravelTicks(infantry, MovementClass.Foot));
            Assert.Equal(-1, svc.EstimateTravelTicks(infantry, MovementClass.Wheeled));
            Assert.True(svc.EstimateTravelTicks(truck, MovementClass.Foot) > 0);
            Assert.NotEqual(truck.TotalCostTicks, svc.EstimateTravelTicks(truck, MovementClass.Foot));
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
            Assert.Equal(-1, steep.EstimateTravelTicks(wheeledSteep, MovementClass.Wheeled));
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
        public void TimeSlice_ResumesAndCommitsOnDeterministicTick()
        {
            MovementService svc = SyntheticMaps.WalledTerrain(
                64,
                64,
                new MovementServiceConfig(
                    pathRequestsPerTick: 8,
                    maxGateways: 8,
                    expansionsPerTick: 48,
                    clusterSize: 32,
                    landmarkCount: 0));

            int cell = svc.Terrain.CellSizeCm;
            int fromX = cell / 2;
            int fromY = cell / 2;
            int toX = svc.Terrain.CellCenterXCm(63);
            int toY = svc.Terrain.CellCenterYCm(63);
            int id = svc.Enqueue(PathRequest.OffRoad(fromX, fromY, toX, toY, MovementClass.Foot));
            Assert.False(svc.TryGetResult(id, out _));

            int ticks = 0;
            PathResult? path = null;
            while (!svc.TryGetResult(id, out path) && ticks < 20000)
            {
                int ran = svc.ProcessQueue();
                Assert.True(svc.ExpansionsThisTick <= 48);
                Assert.True(svc.LastExpansions <= 48);
                if (ran == 0)
                {
                    Assert.True(svc.HasInFlightSearch);
                }

                ticks++;
            }

            Assert.NotNull(path);
            Assert.True(path.Reachable);
            Assert.True(ticks > 1);
            Assert.Equal(svc.Tick, path.CommitTick);
            Assert.True(path.CommitTick >= 2);
            Assert.False(svc.HasInFlightSearch);
        }

        [Fact]
        public void Hpa_CostStaysWithinBoundOfExactAStar()
        {
            MovementServiceConfig exactCfg = new MovementServiceConfig(8, 8, 4096, 96, 0);
            MovementServiceConfig hpaCfg = new MovementServiceConfig(8, 8, 4096, 32, 0);
            MovementService exact = SyntheticMaps.WalledTerrain(96, 96, exactCfg);
            MovementService hpa = SyntheticMaps.WalledTerrain(96, 96, hpaCfg);

            long worstNum = 0;
            long worstDen = 1;
            int[][] queries =
            {
                new[] { 0, 0, 95, 95 },
                new[] { 0, 48, 95, 48 },
                new[] { 10, 10, 10, 90 },
                new[] { 2, 2, 90, 40 }
            };

            for (int q = 0; q < queries.Length; q++)
            {
                int[] g = queries[q];
                PathResult e = exact.PathOffRoad(
                    exact.Terrain.CellCenterXCm(g[0]),
                    exact.Terrain.CellCenterYCm(g[1]),
                    exact.Terrain.CellCenterXCm(g[2]),
                    exact.Terrain.CellCenterYCm(g[3]),
                    MovementClass.Foot);
                PathResult h = hpa.PathOffRoad(
                    hpa.Terrain.CellCenterXCm(g[0]),
                    hpa.Terrain.CellCenterYCm(g[1]),
                    hpa.Terrain.CellCenterXCm(g[2]),
                    hpa.Terrain.CellCenterYCm(g[3]),
                    MovementClass.Foot);
                Assert.True(e.Reachable);
                Assert.True(h.Reachable);
                Assert.True(h.TotalCostTicks >= e.TotalCostTicks);
                if (e.TotalCostTicks > 0 && h.TotalCostTicks * worstDen > worstNum * e.TotalCostTicks)
                {
                    worstNum = h.TotalCostTicks;
                    worstDen = e.TotalCostTicks;
                }
            }

            // Observed bound vs exact A*: HPA* cost / exact cost <= 1.15 on these goldens.
            Assert.True(worstNum * 100 <= worstDen * 115, $"HPA/exact={worstNum}/{worstDen}");
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

    public sealed class HierarchyBuildTests
    {
        [Fact]
        public void SerialAndParallelBuilds_AreByteIdentical()
        {
            MovementService serial = SyntheticMaps.WalledTerrain(
                64,
                64,
                new MovementServiceConfig(8, 8, 4096, 32, 0, null, 1));
            MovementService parallel = SyntheticMaps.WalledTerrain(
                64,
                64,
                new MovementServiceConfig(8, 8, 4096, 32, 0, null, 8));

            for (int c = 0; c < MovementTables.ClassCount; c++)
            {
                MovementClass clas = (MovementClass)c;
                Assert.Equal(serial.HierarchyFingerprint(clas), parallel.HierarchyFingerprint(clas));
            }

            PathResult a = serial.PathOffRoad(
                serial.Terrain.CellCenterXCm(0),
                serial.Terrain.CellCenterYCm(0),
                serial.Terrain.CellCenterXCm(63),
                serial.Terrain.CellCenterYCm(63),
                MovementClass.Foot);
            PathResult b = parallel.PathOffRoad(
                parallel.Terrain.CellCenterXCm(0),
                parallel.Terrain.CellCenterYCm(0),
                parallel.Terrain.CellCenterXCm(63),
                parallel.Terrain.CellCenterYCm(63),
                MovementClass.Foot);
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(a.TotalCostTicks, b.TotalCostTicks);
        }

        [Fact]
        public void GridHierarchyBuild_SerialVsParallel_UniformAndMixedAreByteIdentical()
        {
            TerrainGrid terrain = Wrmb.ReadTerrain(
                new TerrainGridBuilder(64, 64, MovementConfig.SyntheticFixtureCellSizeCm)
                    .Fill(0, LandCover.Field)
                    .ToBlob());
            int cells = terrain.CellCount;
            int[] uniform = new int[cells];
            for (int i = 0; i < cells; i++)
            {
                uniform[i] = 10;
            }

            ulong uniformSerial = GridHierarchy.Build(terrain, uniform, 32, 1).Fingerprint();
            ulong uniformParallel = GridHierarchy.Build(terrain, uniform, 32, 4).Fingerprint();
            Assert.Equal(uniformSerial, uniformParallel);
            Assert.NotEqual(0UL, uniformSerial);

            int[] mixed = new int[cells];
            Array.Copy(uniform, mixed, cells);
            mixed[32 * 64 + 16] = 25;
            mixed[40 * 64 + 40] = 0;
            ulong mixedSerial = GridHierarchy.Build(terrain, mixed, 32, 1).Fingerprint();
            ulong mixedParallel = GridHierarchy.Build(terrain, mixed, 32, 4).Fingerprint();
            Assert.Equal(mixedSerial, mixedParallel);
            Assert.NotEqual(uniformSerial, mixedSerial);
        }

        [Fact]
        public void UniformOpenField_SkipsHierarchy_AirSkipsWalledWater()
        {
            MovementServiceConfig cfg = new MovementServiceConfig(8, 8, 4096, 32, 0, null, 1);
            MovementService open = SyntheticMaps.OpenTerrain(64, 64, cfg);
            Assert.Equal(0UL, open.HierarchyFingerprint(MovementClass.Foot));
            Assert.Equal(0UL, open.HierarchyFingerprint(MovementClass.Wheeled));
            Assert.Equal(0UL, open.HierarchyFingerprint(MovementClass.Tracked));
            Assert.Equal(0UL, open.HierarchyFingerprint(MovementClass.Air));

            MovementService walled = SyntheticMaps.WalledTerrain(64, 64, cfg);
            Assert.NotEqual(0UL, walled.HierarchyFingerprint(MovementClass.Foot));
            Assert.Equal(0UL, walled.HierarchyFingerprint(MovementClass.Air));
        }

        [Fact]
        public void HierarchyCache_RoundtripsAndRejectsTamper()
        {
            string dir = Path.Combine(Path.GetTempPath(), "wasteland-whpa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                MovementServiceConfig cfg = new MovementServiceConfig(8, 8, 4096, 32, 0, dir, 1);
                MovementService cold = SyntheticMaps.WalledTerrain(64, 64, cfg);
                Assert.False(cold.LoadStats.HierarchyCacheHit);
                ulong foot = cold.HierarchyFingerprint(MovementClass.Foot);

                MovementService warm = SyntheticMaps.WalledTerrain(64, 64, cfg);
                Assert.True(warm.LoadStats.HierarchyCacheHit);
                Assert.Equal(foot, warm.HierarchyFingerprint(MovementClass.Foot));

                string[] files = Directory.GetFiles(dir, "*.bin");
                Assert.True(files.Length >= 1);
                byte[] blob = File.ReadAllBytes(files[0]);
                blob[blob.Length - 1] ^= 0xFF;
                File.WriteAllBytes(files[0], blob);

                MovementService afterTamper = SyntheticMaps.WalledTerrain(64, 64, cfg);
                Assert.False(afterTamper.LoadStats.HierarchyCacheHit);
                Assert.Equal(foot, afterTamper.HierarchyFingerprint(MovementClass.Foot));
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
