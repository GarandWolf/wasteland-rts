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

        // MVFD + MVLC 5 on blocking water (only three such cells in this blob).
        public const int Mvlc5FordX = 1066;
        public const int Mvlc5FordY = 888;
        public const ulong Mvlc5FordWheeledHash = 0x3A5FAED3775013F6UL;

        // OSM ford with plain marsh one cell south. Ford MVLC is 4 in this pair
        // (forest); the bit still overrides cover to Ford on read.
        public const int MarshRiverFordX = 266;
        public const int MarshRiverFordY = 1377;
        public const int MarshRiverMarshX = 266;
        public const int MarshRiverMarshY = 1376;
        public const ulong MarshRiverFordWheeledHash = 0x257E2982615CAA18UL;
        public const ulong MarshRiverDetourWheeledHash = 0xABFCA2CE5327D796UL;

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

            int ford5 = grid.Index(Mvlc5FordX, Mvlc5FordY);
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[ford5]);
            Assert.True(FordBit(mvfd, ford5));
            Assert.Equal(LandCover.Ford, grid.CoverAt(ford5));
            Assert.True(tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Ford, grid.SlopeAt(ford5)) > 0);

            int marshFord = grid.Index(MarshRiverFordX, MarshRiverFordY);
            int marsh = grid.Index(MarshRiverMarshX, MarshRiverMarshY);
            Assert.True(FordBit(mvfd, marshFord));
            Assert.False(FordBit(mvfd, marsh));
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[marsh]);
            Assert.Equal(LandCover.Ford, grid.CoverAt(marshFord));
            Assert.Equal(LandCover.Marsh, grid.CoverAt(marsh));
            Assert.True(tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Ford, grid.SlopeAt(marshFord)) > 0);
            Assert.Equal(0, tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Marsh, grid.SlopeAt(marsh)));

            PathResult acrossMvlc5 = PathCells(svc, grid, Mvlc5FordX, Mvlc5FordY, Mvlc5FordX + 1, Mvlc5FordY);
            PathResult acrossFord = PathCells(svc, grid, MarshRiverFordX, MarshRiverFordY, MarshRiverFordX, MarshRiverFordY + 1);
            PathResult acrossMarsh = PathCells(svc, grid, MarshRiverMarshX - 1, MarshRiverMarshY, MarshRiverMarshX + 1, MarshRiverMarshY);
            PathResult fromMarsh = PathCells(svc, grid, MarshRiverMarshX, MarshRiverMarshY, MarshRiverMarshX + 1, MarshRiverMarshY);

            Assert.True(acrossMvlc5.Reachable);
            Assert.True(acrossFord.Reachable);
            Assert.True(acrossMarsh.Reachable);
            Assert.False(fromMarsh.Reachable);
            Assert.True(VisitsCell(acrossMvlc5, grid, Mvlc5FordX, Mvlc5FordY));
            Assert.True(VisitsCell(acrossFord, grid, MarshRiverFordX, MarshRiverFordY));
            Assert.False(VisitsCell(acrossMarsh, grid, MarshRiverMarshX, MarshRiverMarshY));
            Assert.Equal(Mvlc5FordWheeledHash, acrossMvlc5.Hash);
            Assert.Equal(MarshRiverFordWheeledHash, acrossFord.Hash);
            Assert.Equal(MarshRiverDetourWheeledHash, acrossMarsh.Hash);
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
