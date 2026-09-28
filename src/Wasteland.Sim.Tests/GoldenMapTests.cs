using System;
using System.IO;
using System.Text;
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
            string dir = FindGoldenDirectory();
            byte[] map = ReadWrmb(Path.Combine(dir, MapFileName), MapBytes);
            byte[] terrain = ReadWrmb(Path.Combine(dir, TerrainFileName), TerrainBytes);

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

        static string FindGoldenDirectory()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "data", "golden");
                if (File.Exists(Path.Combine(candidate, MapFileName)))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), "data", "golden");
            if (File.Exists(Path.Combine(cwd, MapFileName)))
            {
                return cwd;
            }

            throw new InvalidOperationException(
                "Could not find data/golden/" + MapFileName + " by walking up from the test output directory.");
        }

        static byte[] ReadWrmb(string path, int expectedLength)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Missing WRMB golden (run git lfs pull --include=\"data/golden/*.wrmb\").", path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != expectedLength || IsLfsPointer(bytes) || !HasWrmbMagic(bytes))
            {
                throw new InvalidDataException(
                    path + " is not a materialized WRMB (" + bytes.Length
                    + " bytes). Run git lfs pull --include=\"data/golden/*.wrmb\".");
            }

            return bytes;
        }

        static bool HasWrmbMagic(byte[] bytes)
        {
            return bytes.Length >= 4
                && bytes[0] == (byte)'W'
                && bytes[1] == (byte)'R'
                && bytes[2] == (byte)'M'
                && bytes[3] == (byte)'B';
        }

        static bool IsLfsPointer(byte[] bytes)
        {
            const string prefix = "version https://git-lfs.github.com/spec/v1";
            if (bytes.Length < prefix.Length || bytes.Length >= 1024)
            {
                return false;
            }

            string head = Encoding.ASCII.GetString(bytes, 0, prefix.Length);
            return head == prefix;
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
