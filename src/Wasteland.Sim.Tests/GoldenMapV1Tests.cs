using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class GoldenMapV1Tests
    {
        public const string MapFileName = "golden-ci-columbia-gorge-100.v1.wrmb";
        public const string TerrainFileName = "golden-ci-columbia-gorge-100.terrain.v1.wrmb";
        public const int MapBytes = 606784;
        public const int TerrainBytes = 19872384;
        public const int ExpectedWidth = 1800;
        public const int ExpectedHeight = 1800;
        public const int ExpectedCellSizeCm = 8940;
        public const int ExpectedChokepoints = 49;
        public const int ExpectedDefenseGroups = 16;
        public const int ExpectedWheeledBlocked = 26;

        // v1 pins live here only. First→last road and the first 16-cell off-road
        // run hashed the same as v0 on this pair (those queries do not cross baked
        // blocking water). Crossing pins are v1-specific.
        public const ulong RoadWheeledHash = 0xED55C547CFC83883UL;
        public const ulong RoadFootHash = 0x6AC2AC8318785748UL;
        public const ulong OffRoadFootHash = 0xAEA7ED93CEECF776UL;
        public const ulong OffRoadWheeledHash = 0x327FA8A3742DE14FUL;
        public const ulong SherarsWheeledHash = 0xF3B9D4E8A019840AUL;
        public const ulong Klickitat11WheeledHash = 0x57874293F467CF82UL;
        public const ulong Klickitat17WheeledHash = 0x05066AFDD674EEEEUL;
        public const ulong Klickitat19WheeledHash = 0xCDC6D6D94C0D07A9UL;
        public const ulong Klickitat20WheeledHash = 0x117D45AB72A4140EUL;

        // First MVFD cell (index order, y=0 south) with plain marsh on both
        // river sides. This pair has no collinear marsh-ford-marsh yet (~3% of
        // river centreline is MVLC 5); the match is a bend. See FindMarshRiverFord.
        public const int MarshRiverFordX = 266;
        public const int MarshRiverFordY = 1377;
        public const int MarshRiverMarshAX = 267;
        public const int MarshRiverMarshAY = 1377;
        public const int MarshRiverMarshBX = 266;
        public const int MarshRiverMarshBY = 1376;
        public const int MarshRiverBankX = 265;
        public const int MarshRiverBankY = 1377;
        public const ulong MarshRiverFordWheeledHash = 0x06DEAF84EAA59BA4UL;

        static readonly byte[] DefenseGroupIdByChokepointId =
        {
            0,
            0, 1, 255, 255, 255, 2, 3, 4, 5, 6,
            7, 255, 255, 8, 255, 255, 7, 255, 7, 9,
            255, 255, 255, 255, 255, 255, 10, 1, 255, 255,
            255, 255, 255, 255, 255, 255, 255, 11, 255, 7,
            9, 12, 13, 14, 1, 14, 15, 2, 11
        };

        static readonly byte[] BypassWheeledBlockedByChokepointId =
        {
            0,
            1, 1, 0, 1, 0, 1, 1, 1, 1, 1,
            1, 0, 0, 1, 1, 1, 1, 0, 1, 1,
            0, 0, 0, 0, 0, 1, 1, 1, 0, 1,
            1, 0, 0, 1, 1, 1, 1, 0, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        static readonly ulong[] TerritoryStableKeyById =
        {
            0UL,
            1152921504757811490UL,
            1152921504757814975UL,
            1152921504757780529UL,
            1152921507617862930UL,
            1152921504757821170UL,
            1152921504757818165UL,
            1152921504757795405UL,
            1152921504757825471UL,
            1152921504757793049UL,
            1152921504757784116UL,
            1152921504757792391UL,
            1152921504757789375UL,
            1152921504757798683UL
        };

        [Fact]
        public void ColumbiaGorgeV1_LoadsAndPinsPathHashes()
        {
            LoadPair(out MovementService svc, out WrmbMap map);
            Assert.Equal(ExpectedWidth, svc.Terrain.Width);
            Assert.Equal(ExpectedHeight, svc.Terrain.Height);
            Assert.Equal(ExpectedCellSizeCm, svc.Terrain.CellSizeCm);
            Assert.Equal(WrmbFile.FormatVersionV1, map.FormatVersion);
            Assert.True((map.File.Flags & WrmbFile.FlagV1Final) != 0);

            int firstId = svc.Graph.GetNodeId(0);
            int lastId = svc.Graph.GetNodeId(svc.Graph.NodeCount - 1);
            RoadCostWeights weights = RoadCostWeights.Uniform();

            PathResult roadWheeled = svc.PathOnRoads(firstId, lastId, MovementClass.Wheeled, weights);
            PathResult roadFoot = svc.PathOnRoads(firstId, lastId, MovementClass.Foot, weights);
            Assert.True(roadWheeled.Reachable);
            Assert.True(roadFoot.Reachable);

            FindWheeledRun(svc.Terrain, 16, out int x0, out int y0, out int x1, out int y1);
            int ax = svc.Terrain.CellCenterXCm(x0);
            int ay = svc.Terrain.CellCenterYCm(y0);
            int bx = svc.Terrain.CellCenterXCm(x1);
            int by = svc.Terrain.CellCenterYCm(y1);
            PathResult offFoot = svc.PathOffRoad(ax, ay, bx, by, MovementClass.Foot);
            PathResult offWheeled = svc.PathOffRoad(ax, ay, bx, by, MovementClass.Wheeled);
            Assert.True(offFoot.Reachable);
            Assert.True(offWheeled.Reachable);

            PathResult sherars = svc.PathOnRoads(191, 190, MovementClass.Wheeled, weights);
            PathResult k11 = svc.PathOnRoads(729, 732, MovementClass.Wheeled, weights);
            PathResult k17 = svc.PathOnRoads(918, 919, MovementClass.Wheeled, weights);
            PathResult k19 = svc.PathOnRoads(975, 976, MovementClass.Wheeled, weights);
            PathResult k20 = svc.PathOnRoads(1052, 1053, MovementClass.Wheeled, weights);
            Assert.True(sherars.Reachable);
            Assert.True(k11.Reachable);
            Assert.True(k17.Reachable);
            Assert.True(k19.Reachable);
            Assert.True(k20.Reachable);

            Assert.Equal(RoadWheeledHash, roadWheeled.Hash);
            Assert.Equal(RoadFootHash, roadFoot.Hash);
            Assert.Equal(OffRoadFootHash, offFoot.Hash);
            Assert.Equal(OffRoadWheeledHash, offWheeled.Hash);
            Assert.Equal(SherarsWheeledHash, sherars.Hash);
            Assert.Equal(Klickitat11WheeledHash, k11.Hash);
            Assert.Equal(Klickitat17WheeledHash, k17.Hash);
            Assert.Equal(Klickitat19WheeledHash, k19.Hash);
            Assert.Equal(Klickitat20WheeledHash, k20.Hash);
        }

        [Fact]
        public void ColumbiaGorgeV1_LoadsEveryChokepointAndTerritoryKey()
        {
            string dir = GoldenMapFiles.FindDirectory(MapFileName);
            byte[] mapBytes = GoldenMapFiles.ReadWrmb(Path.Combine(dir, MapFileName), MapBytes);
            WrmbMap map = WrmbMap.Load(mapBytes);

            Assert.Equal(ExpectedChokepoints, map.Chokepoints.Length);
            Assert.Equal(13, map.Territories.Length);

            HashSet<byte> groups = new HashSet<byte>();
            int wheeled = 0;
            for (int i = 0; i < map.Chokepoints.Length; i++)
            {
                Chokepoint c = map.Chokepoints[i];
                Assert.Equal(i + 1, c.Id);
                Assert.Equal(DefenseGroupIdByChokepointId[c.Id], c.DefenseGroupId);
                Assert.Equal(BypassWheeledBlockedByChokepointId[c.Id], c.BypassWheeledBlocked);
                if (c.HasDefenseGroup)
                {
                    groups.Add(c.DefenseGroupId);
                }

                if (c.BypassWheeledBlocked != 0)
                {
                    wheeled++;
                }
            }

            Assert.Equal(ExpectedDefenseGroups, groups.Count);
            Assert.Equal(ExpectedWheeledBlocked, wheeled);

            for (int i = 0; i < map.Territories.Length; i++)
            {
                Territory t = map.Territories[i];
                Assert.Equal(i + 1, t.Id);
                Assert.Equal(TerritoryStableKeyById[t.Id], t.StableKey);
            }

            Assert.Equal((ushort)1, map.Roads.GetNodeTerritoryId(0));
            Assert.Equal((ushort)13, map.Roads.GetNodeTerritoryId(map.Roads.NodeCount - 1));
            Assert.True(map.Roads.TryGetNodeIndex(191, out int n191));
            Assert.True(map.Roads.TryGetNodeIndex(190, out int n190));
            Assert.Equal((ushort)1, map.Roads.GetNodeTerritoryId(n191));
            Assert.Equal((ushort)3, map.Roads.GetNodeTerritoryId(n190));
        }

        [Fact]
        public void ColumbiaGorgeV1_WheeledCrossesMarshOnlyAtFord()
        {
            LoadPair(out MovementService svc, out _);
            string dir = GoldenMapFiles.FindDirectory(TerrainFileName);
            byte[] terrainBytes = GoldenMapFiles.ReadWrmb(Path.Combine(dir, TerrainFileName), TerrainBytes);
            WrmbFile terrainFile = WrmbFile.Parse(terrainBytes);
            byte[] mvlc = terrainFile.SectionBytes(terrainFile.RequireSection("MVLC"));
            byte[] mvfd = terrainFile.SectionBytes(terrainFile.RequireSection("MVFD"));
            TerrainGrid grid = svc.Terrain;
            MovementTables tables = MovementTables.Default();

            FindMarshRiverFord(
                grid,
                mvlc,
                mvfd,
                out int fordX,
                out int fordY,
                out int marshAx,
                out int marshAy,
                out int marshBx,
                out int marshBy,
                out int bankX,
                out int bankY);

            Assert.Equal(MarshRiverFordX, fordX);
            Assert.Equal(MarshRiverFordY, fordY);
            Assert.Equal(MarshRiverMarshAX, marshAx);
            Assert.Equal(MarshRiverMarshAY, marshAy);
            Assert.Equal(MarshRiverMarshBX, marshBx);
            Assert.Equal(MarshRiverMarshBY, marshBy);
            Assert.Equal(MarshRiverBankX, bankX);
            Assert.Equal(MarshRiverBankY, bankY);

            int ford = grid.Index(fordX, fordY);
            int marshA = grid.Index(marshAx, marshAy);
            int marshB = grid.Index(marshBx, marshBy);
            Assert.True(FordBit(mvfd, ford));
            Assert.False(FordBit(mvfd, marshA));
            Assert.False(FordBit(mvfd, marshB));
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[marshA]);
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[marshB]);
            Assert.Equal(LandCover.Ford, grid.CoverAt(ford));
            Assert.Equal(LandCover.Marsh, grid.CoverAt(marshA));
            Assert.Equal(LandCover.Marsh, grid.CoverAt(marshB));
            Assert.True(tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Ford, grid.SlopeAt(ford)) > 0);
            Assert.Equal(0, tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Marsh, grid.SlopeAt(marshA)));
            Assert.Equal(0, tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Marsh, grid.SlopeAt(marshB)));

            PathResult acrossFord = PathCells(svc, grid, fordX, fordY, bankX, bankY);
            PathResult fromMarsh = PathCells(svc, grid, marshAx, marshAy, bankX, bankY);
            Assert.True(acrossFord.Reachable);
            Assert.False(fromMarsh.Reachable);
            Assert.True(VisitsCell(acrossFord, grid, fordX, fordY));
            Assert.False(VisitsCell(acrossFord, grid, marshAx, marshAy));
            Assert.Equal(MarshRiverFordWheeledHash, acrossFord.Hash);
        }

        static void LoadPair(out MovementService svc, out WrmbMap map)
        {
            string dir = GoldenMapFiles.FindDirectory(MapFileName);
            byte[] mapBytes = GoldenMapFiles.ReadWrmb(Path.Combine(dir, MapFileName), MapBytes);
            byte[] terrain = GoldenMapFiles.ReadWrmb(Path.Combine(dir, TerrainFileName), TerrainBytes);
            svc = MovementService.Create(mapBytes, terrain, MovementTables.Default());
            map = WrmbMap.Load(mapBytes);
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

        /// <summary>
        /// First ford on a river that already has plain marsh on both sides.
        /// Scan index order (y=0 south, x east). A ford is an MVFD cell. Plain
        /// marsh is MVLC 5 with the ford bit clear. The river axis is an opposite
        /// NS or EW marsh pair when one exists; otherwise the first two marsh
        /// 4-neighbors in W, E, S, N order (a bend). The wheeled bank is the
        /// first 4-neighbor that is not marsh and is wheeled-passable. This v1
        /// pair's match is (266, 1377), marshes (267, 1377) and (266, 1376),
        /// bank (265, 1377). After rivers become continuous marsh, a collinear
        /// marsh-ford-marsh earlier in scan order should win the same rule.
        /// </summary>
        static void FindMarshRiverFord(
            TerrainGrid grid,
            byte[] mvlc,
            byte[] mvfd,
            out int fordX,
            out int fordY,
            out int marshAx,
            out int marshAy,
            out int marshBx,
            out int marshBy,
            out int bankX,
            out int bankY)
        {
            int width = grid.Width;
            int height = grid.Height;
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < width * height; i++)
            {
                if (!FordBit(mvfd, i))
                {
                    continue;
                }

                int x = i % width;
                int y = i / width;
                int marshCount = 0;
                int[] mx = new int[4];
                int[] my = new int[4];
                int bankCount = 0;
                int[] bx = new int[4];
                int[] by = new int[4];
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d];
                    int ny = y + dy[d];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }

                    int ni = ny * width + nx;
                    if (IsPlainMarsh(mvlc, mvfd, ni))
                    {
                        mx[marshCount] = nx;
                        my[marshCount] = ny;
                        marshCount++;
                    }
                    else if (WheeledCell(grid, nx, ny))
                    {
                        bx[bankCount] = nx;
                        by[bankCount] = ny;
                        bankCount++;
                    }
                }

                bool collinear = false;
                if (IsPlainMarshNeighbor(mvlc, mvfd, width, height, x, y - 1)
                    && IsPlainMarshNeighbor(mvlc, mvfd, width, height, x, y + 1))
                {
                    mx[0] = x;
                    my[0] = y - 1;
                    mx[1] = x;
                    my[1] = y + 1;
                    marshCount = 2;
                    collinear = true;
                }
                else if (IsPlainMarshNeighbor(mvlc, mvfd, width, height, x - 1, y)
                    && IsPlainMarshNeighbor(mvlc, mvfd, width, height, x + 1, y))
                {
                    mx[0] = x - 1;
                    my[0] = y;
                    mx[1] = x + 1;
                    my[1] = y;
                    marshCount = 2;
                    collinear = true;
                }

                if ((!collinear && marshCount < 2) || bankCount < 1)
                {
                    continue;
                }

                fordX = x;
                fordY = y;
                marshAx = mx[0];
                marshAy = my[0];
                marshBx = mx[1];
                marshBy = my[1];
                bankX = bx[0];
                bankY = by[0];
                return;
            }

            throw new InvalidDataException(
                "Golden v1 terrain has no MVFD cell with plain marsh on both river sides.");
        }

        static bool IsPlainMarsh(byte[] mvlc, byte[] mvfd, int index)
        {
            return mvlc[index] == Wrmb.WrmbCoverMarsh && !FordBit(mvfd, index);
        }

        static bool IsPlainMarshNeighbor(byte[] mvlc, byte[] mvfd, int width, int height, int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return false;
            }

            return IsPlainMarsh(mvlc, mvfd, y * width + x);
        }

        static PathResult PathCells(MovementService svc, TerrainGrid grid, int x0, int y0, int x1, int y1)
        {
            return svc.PathOffRoad(
                grid.CellCenterXCm(x0),
                grid.CellCenterYCm(y0),
                grid.CellCenterXCm(x1),
                grid.CellCenterYCm(y1),
                MovementClass.Wheeled);
        }

        static bool VisitsCell(PathResult path, TerrainGrid grid, int cellX, int cellY)
        {
            int xCm = grid.CellCenterXCm(cellX);
            int yCm = grid.CellCenterYCm(cellY);
            for (int i = 0; i < path.Waypoints.Length; i++)
            {
                if (path.Waypoints[i].XCm == xCm && path.Waypoints[i].YCm == yCm)
                {
                    return true;
                }
            }

            return false;
        }

        static bool FordBit(byte[] mvfd, int index)
        {
            return (mvfd[index >> 3] & (1 << (index & 7))) != 0;
        }
    }
}
