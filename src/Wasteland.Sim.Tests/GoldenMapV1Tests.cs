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
    }
}
