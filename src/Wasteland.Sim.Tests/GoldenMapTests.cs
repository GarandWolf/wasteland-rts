using System;
using System.IO;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class GoldenMapTests
    {
        public const string MapFileName = "golden-ci-columbia-gorge-100.v0.wrmb";
        public const string TerrainFileName = "golden-ci-columbia-gorge-100.terrain.v0.wrmb";
        public const int MapBytes = 602648;
        public const int TerrainBytes = 16614424;
        public const int ExpectedWidth = 1800;
        public const int ExpectedHeight = 1800;
        public const int ExpectedCellSizeCm = 8940;

        // Road: first → last graph node. Off-road: first east-west run of 16 wheeled-passable cells.
        public const ulong RoadWheeledHash = 0xED55C547CFC83883UL;
        public const ulong RoadFootHash = 0x6AC2AC8318785748UL;
        public const ulong OffRoadFootHash = 0xAEA7ED93CEECF776UL;
        public const ulong OffRoadWheeledHash = 0x327FA8A3742DE14FUL;

        [Fact]
        public void ColumbiaGorge_LoadsAndPinsPathHashes()
        {
            string dir = GoldenMapFiles.FindDirectory(MapFileName);
            byte[] map = GoldenMapFiles.ReadWrmb(Path.Combine(dir, MapFileName), MapBytes);
            byte[] terrain = GoldenMapFiles.ReadWrmb(Path.Combine(dir, TerrainFileName), TerrainBytes);

            MovementService svc = MovementService.Create(map, terrain, MovementTables.Default());
            Assert.Equal(ExpectedWidth, svc.Terrain.Width);
            Assert.Equal(ExpectedHeight, svc.Terrain.Height);
            Assert.Equal(ExpectedCellSizeCm, svc.Terrain.CellSizeCm);
            Assert.True(svc.Graph.NodeCount > 1);

            int firstId = svc.Graph.GetNodeId(0);
            int lastId = svc.Graph.GetNodeId(svc.Graph.NodeCount - 1);
            RoadCostWeights weights = RoadCostWeights.Uniform();

            PathResult roadWheeled = svc.PathOnRoads(firstId, lastId, MovementClass.Wheeled, weights);
            PathResult roadFoot = svc.PathOnRoads(firstId, lastId, MovementClass.Foot, weights);
            Assert.True(roadWheeled.Reachable);
            Assert.True(roadFoot.Reachable);
            Assert.Equal(PathKind.OnRoads, roadWheeled.Kind);
            Assert.Equal(PathKind.OnRoads, roadFoot.Kind);

            FindWheeledRun(svc.Terrain, 16, out int x0, out int y0, out int x1, out int y1);
            int ax = svc.Terrain.CellCenterXCm(x0);
            int ay = svc.Terrain.CellCenterYCm(y0);
            int bx = svc.Terrain.CellCenterXCm(x1);
            int by = svc.Terrain.CellCenterYCm(y1);
            PathResult offFoot = svc.PathOffRoad(ax, ay, bx, by, MovementClass.Foot);
            PathResult offWheeled = svc.PathOffRoad(ax, ay, bx, by, MovementClass.Wheeled);
            Assert.True(offFoot.Reachable);
            Assert.True(offWheeled.Reachable);
            Assert.Equal(PathKind.OffRoad, offFoot.Kind);
            Assert.Equal(PathKind.OffRoad, offWheeled.Kind);

            Assert.Equal(RoadWheeledHash, roadWheeled.Hash);
            Assert.Equal(RoadFootHash, roadFoot.Hash);
            Assert.Equal(OffRoadFootHash, offFoot.Hash);
            Assert.Equal(OffRoadWheeledHash, offWheeled.Hash);
        }

        [Fact]
        public void NullCacheDirectory_GorgePathsMatchWarmCacheAndGoldens()
        {
            string dir = Path.Combine(Path.GetTempPath(), "wasteland-whpa-gorge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string mapDir = GoldenMapFiles.FindDirectory(MapFileName);
                byte[] map = GoldenMapFiles.ReadWrmb(Path.Combine(mapDir, MapFileName), MapBytes);
                byte[] terrain = GoldenMapFiles.ReadWrmb(Path.Combine(mapDir, TerrainFileName), TerrainBytes);
                MovementTables tables = MovementTables.Default();

                MovementService cold = MovementService.Create(map, terrain, tables, CacheConfig(dir));
                Assert.False(cold.LoadStats.HierarchyCacheHit);
                string[] cacheFiles = Directory.GetFiles(dir, "*.bin");
                Assert.True(cacheFiles.Length == 1);
                byte[] cacheBytes = File.ReadAllBytes(cacheFiles[0]);
                string[] cwdBefore = Directory.GetFiles(Directory.GetCurrentDirectory(), "whpa-*.bin");

                MovementService uncached = MovementService.Create(map, terrain, tables, CacheConfig(null));
                Assert.False(uncached.LoadStats.HierarchyCacheHit);
                Assert.Equal(cacheBytes, File.ReadAllBytes(cacheFiles[0]));
                Assert.Equal(cacheFiles, Directory.GetFiles(dir, "*.bin"));
                Assert.Equal(cwdBefore, Directory.GetFiles(Directory.GetCurrentDirectory(), "whpa-*.bin"));

                ReadGorgePaths(
                    uncached,
                    out PathResult bareRoadWheeled,
                    out PathResult bareRoadFoot,
                    out PathResult bareOffFoot,
                    out PathResult bareOffWheeled);

                MovementService warm = MovementService.Create(map, terrain, tables, CacheConfig(dir));
                Assert.True(warm.LoadStats.HierarchyCacheHit);
                Assert.Equal(cacheBytes, File.ReadAllBytes(cacheFiles[0]));

                ReadGorgePaths(
                    warm,
                    out PathResult warmRoadWheeled,
                    out PathResult warmRoadFoot,
                    out PathResult warmOffFoot,
                    out PathResult warmOffWheeled);

                AssertByteIdentical(bareRoadWheeled, warmRoadWheeled);
                AssertByteIdentical(bareRoadFoot, warmRoadFoot);
                AssertByteIdentical(bareOffFoot, warmOffFoot);
                AssertByteIdentical(bareOffWheeled, warmOffWheeled);

                Assert.Equal(RoadWheeledHash, bareRoadWheeled.Hash);
                Assert.Equal(RoadFootHash, bareRoadFoot.Hash);
                Assert.Equal(OffRoadFootHash, bareOffFoot.Hash);
                Assert.Equal(OffRoadWheeledHash, bareOffWheeled.Hash);
                Assert.Equal(RoadWheeledHash, warmRoadWheeled.Hash);
                Assert.Equal(RoadFootHash, warmRoadFoot.Hash);
                Assert.Equal(OffRoadFootHash, warmOffFoot.Hash);
                Assert.Equal(OffRoadWheeledHash, warmOffWheeled.Hash);
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

        static MovementServiceConfig CacheConfig(string? directory)
        {
            return new MovementServiceConfig(
                MovementConfig.DefaultPathRequestsPerTick,
                MovementConfig.DefaultMaxGateways,
                MovementConfig.DefaultExpansionsPerTick,
                MovementConfig.DefaultClusterSize,
                MovementConfig.DefaultLandmarkCount,
                directory,
                0);
        }

        static void ReadGorgePaths(
            MovementService svc,
            out PathResult roadWheeled,
            out PathResult roadFoot,
            out PathResult offFoot,
            out PathResult offWheeled)
        {
            int firstId = svc.Graph.GetNodeId(0);
            int lastId = svc.Graph.GetNodeId(svc.Graph.NodeCount - 1);
            RoadCostWeights weights = RoadCostWeights.Uniform();
            roadWheeled = svc.PathOnRoads(firstId, lastId, MovementClass.Wheeled, weights);
            roadFoot = svc.PathOnRoads(firstId, lastId, MovementClass.Foot, weights);
            FindWheeledRun(svc.Terrain, 16, out int x0, out int y0, out int x1, out int y1);
            int ax = svc.Terrain.CellCenterXCm(x0);
            int ay = svc.Terrain.CellCenterYCm(y0);
            int bx = svc.Terrain.CellCenterXCm(x1);
            int by = svc.Terrain.CellCenterYCm(y1);
            offFoot = svc.PathOffRoad(ax, ay, bx, by, MovementClass.Foot);
            offWheeled = svc.PathOffRoad(ax, ay, bx, by, MovementClass.Wheeled);
        }

        static void AssertByteIdentical(PathResult bare, PathResult warm)
        {
            Assert.Equal(bare.Hash, warm.Hash);
            Assert.Equal(bare.Reachable, warm.Reachable);
            Assert.Equal(bare.TotalCostTicks, warm.TotalCostTicks);
            Assert.Equal(bare.Kind, warm.Kind);
            Assert.Equal(bare.GraphVersion, warm.GraphVersion);
            Assert.Equal(bare.Waypoints.Length, warm.Waypoints.Length);
            for (int i = 0; i < bare.Waypoints.Length; i++)
            {
                PathWaypoint a = bare.Waypoints[i];
                PathWaypoint b = warm.Waypoints[i];
                Assert.Equal(a.Kind, b.Kind);
                Assert.Equal(a.Id, b.Id);
                Assert.Equal(a.XCm, b.XCm);
                Assert.Equal(a.YCm, b.YCm);
            }
        }

        static void FindWheeledRun(TerrainGrid terrain, int minSpan, out int x0, out int y0, out int x1, out int y1)
        {
            int width = terrain.Width;
            int height = terrain.Height;
            for (int y = 0; y < height; y++)
            {
                int runStart = -1;
                for (int x = 0; x < width; x++)
                {
                    if (!WheeledCell(terrain, x, y))
                    {
                        runStart = -1;
                        continue;
                    }

                    if (runStart < 0)
                    {
                        runStart = x;
                    }

                    if (x - runStart >= minSpan)
                    {
                        x0 = runStart;
                        y0 = y;
                        x1 = x;
                        y1 = y;
                        return;
                    }
                }
            }

            throw new InvalidDataException("Golden terrain has no wheeled-passable run of " + minSpan + " cells.");
        }

        static bool WheeledCell(TerrainGrid terrain, int x, int y)
        {
            int index = terrain.Index(x, y);
            LandCover cover = terrain.CoverAt(index);
            if (cover == LandCover.Water || cover == LandCover.Marsh)
            {
                return false;
            }

            return terrain.SlopeAt(index) <= 299;
        }
    }
}
