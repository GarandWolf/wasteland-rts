using System;
using System.IO;
using System.Security.Cryptography;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class WrmbTests
    {
        [Fact]
        public void Header_IsWrmbDraftLayout()
        {
            byte[] blob = new RoadGraphBuilder()
                .AddNode(1, 0, 0)
                .ToBlob();

            Assert.Equal((byte)'W', blob[0]);
            Assert.Equal((byte)'R', blob[1]);
            Assert.Equal((byte)'M', blob[2]);
            Assert.Equal((byte)'B', blob[3]);
            Assert.Equal(0, blob[4] | (blob[5] << 8));
            Assert.Equal(1, blob[6] | (blob[7] << 8));
            Assert.Equal(64u, ReadU32(blob, 8));
            Assert.Equal(WrmbFile.FlagDraftLayout, ReadU32(blob, 12));

            WrmbFile file = WrmbFile.Parse(blob);
            Assert.True(file.TryGetSection("RNOD", out WrmbDirEntry rnod));
            Assert.Equal(Wrmb.RnodElementSize, rnod.ElementSize);
            Assert.True(file.TryGetSection("ZZZZ", out _) == false);
        }

        [Fact]
        public void Builder_SortsById_AndRoundtrips()
        {
            byte[] blob = new RoadGraphBuilder()
                .Origin(10, 20)
                .AddNode(7, 100, 200, RoadNodeFlags.BridgeHead)
                .AddNode(3, 0, 0, RoadNodeFlags.BridgeHead)
                .AddEdge(9, 3, 7, 150, RoadTier.Paved, RoadSurface.BridgeDeck, RoadEdgeFlags.BridgeSpan)
                .AddEdge(4, 3, 7, 400, RoadTier.Dirt, RoadSurface.Dirt)
                .ToBlob();

            RoadGraph graph = Wrmb.ReadRoadGraph(blob);
            Assert.Equal(2, graph.NodeCount);
            Assert.Equal(3, graph.GetNodeId(0));
            Assert.Equal(7, graph.GetNodeId(1));
            Assert.Equal(4, graph.GetEdgeId(0));
            Assert.Equal(9, graph.GetEdgeId(1));
            Assert.Equal(0, graph.OriginXCm);
            Assert.True((graph.GetNodeFlags(0) & RoadNodeFlags.BridgeHead) != 0);
            Assert.True((graph.GetEdgeFlags(1) & RoadEdgeFlags.BridgeSpan) != 0);
            Assert.Equal(RoadSurface.BridgeDeck, graph.GetEdgeSurface(1));
            Assert.Equal(RoadTier.Dirt, graph.GetEdgeTier(0));
            Assert.Equal(RoadTier.Paved, graph.GetEdgeTier(1));

            byte[] again = Wrmb.WriteRoadGraph(graph);
            Assert.Equal(blob, again);

            RoadGraph second = Wrmb.ReadRoadGraph(again);
            Assert.Equal(graph.NodeCount, second.NodeCount);
            Assert.Equal(graph.GetNodeXCm(1), second.GetNodeXCm(1));
        }

        [Fact]
        public void UnsortedNodes_AreRejected()
        {
            byte[] rnod = new byte[Wrmb.RnodElementSize * 2];
            WriteI32(rnod, 0, 2);
            WriteI32(rnod, Wrmb.RnodElementSize, 1);
            byte[] blob = WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", Wrmb.WriteMeta(RoadMeta(2, 0)), Wrmb.MetaElementSize),
                    new WrmbSectionPayload("RNOD", rnod, Wrmb.RnodElementSize),
                    new WrmbSectionPayload("REDG", Array.Empty<byte>(), Wrmb.RedgElementSize)
                });

            Assert.Throws<InvalidDataException>(() => Wrmb.ReadRoadGraph(blob));
        }

        [Fact]
        public void WrongMagic_IsRejected()
        {
            byte[] blob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            blob[0] = (byte)'X';
            Assert.Throws<InvalidDataException>(() => WrmbFile.Parse(blob));
        }

        [Fact]
        public void SectionCrcMismatch_IsRejected()
        {
            byte[] blob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            WrmbFile file = WrmbFile.Parse(blob);
            Assert.True(file.TryGetSection("RNOD", out WrmbDirEntry rnod));
            byte[] tampered = (byte[])blob.Clone();
            tampered[rnod.Offset] ^= 0xFF;
            RecomputeContentHash(tampered);
            Assert.Throws<InvalidDataException>(() => WrmbFile.Parse(tampered));
        }

        [Fact]
        public void ElementSizeMismatch_IsRejected()
        {
            byte[] rnod = new byte[24];
            WriteI32(rnod, 0, 1);
            byte[] blob = WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", Wrmb.WriteMeta(RoadMeta(2, 0)), Wrmb.MetaElementSize),
                    new WrmbSectionPayload("RNOD", rnod, 12),
                    new WrmbSectionPayload("REDG", Array.Empty<byte>(), Wrmb.RedgElementSize)
                });

            Assert.Throws<InvalidDataException>(() => Wrmb.ReadRoadGraph(blob));
        }

        [Fact]
        public void UnknownSectionTag_IsSkipped()
        {
            byte[] extra = new byte[8];
            byte[] baseBlob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            RoadGraph graph = Wrmb.ReadRoadGraph(baseBlob);
            byte[] blob = WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", Wrmb.WriteMeta(RoadMeta(1, 0)), Wrmb.MetaElementSize),
                    new WrmbSectionPayload("RNOD", SectionOf(baseBlob, "RNOD"), Wrmb.RnodElementSize),
                    new WrmbSectionPayload("REDG", SectionOf(baseBlob, "REDG"), Wrmb.RedgElementSize),
                    new WrmbSectionPayload("ZZZZ", extra, 8)
                });

            WrmbFile file = WrmbFile.Parse(blob);
            Assert.True(file.TryGetSection("ZZZZ", out WrmbDirEntry z));
            Assert.Equal(8, z.ElementSize);
            RoadGraph loaded = Wrmb.ReadRoadGraph(blob);
            Assert.Equal(graph.NodeCount, loaded.NodeCount);
            Assert.Equal(1, loaded.GetNodeId(0));
        }

        [Fact]
        public void BridgeSpan_WithoutHeads_IsRejected()
        {
            Assert.Throws<InvalidDataException>(() =>
            {
                byte[] blob = new RoadGraphBuilder()
                    .AddNode(1, 0, 0)
                    .AddNode(2, 10, 0)
                    .AddEdge(1, 1, 2, 10, RoadTier.Paved, RoadSurface.Asphalt, RoadEdgeFlags.BridgeSpan)
                    .ToBlob();
                Wrmb.ReadRoadGraph(blob);
            });
        }

        [Fact]
        public void Terrain_Roundtrip_PreservesSlopeCoverAndCellSize()
        {
            byte[] blob = new TerrainGridBuilder(3, 2, 8941)
                .Fill(12, LandCover.Forest)
                .SetCell(2, 1, 300, LandCover.Marsh)
                .ToBlob();

            WrmbFile file = WrmbFile.Parse(blob);
            Assert.Equal((byte)'W', blob[0]);
            Assert.True(file.TryGetSection("MVSL", out WrmbDirEntry sl));
            Assert.Equal(2, sl.ElementSize);
            Assert.True(file.TryGetSection("MVLC", out _));
            Assert.True(file.TryGetSection("MVFD", out _));

            TerrainGrid grid = Wrmb.ReadTerrain(blob);
            Assert.Equal(3, grid.Width);
            Assert.Equal(2, grid.Height);
            Assert.Equal(8941, grid.CellSizeCm);
            Assert.Equal(0, grid.OriginXCm);
            Assert.Equal(LandCover.Forest, grid.CoverAt(0));
            Assert.Equal(300, grid.SlopeAt(grid.Index(2, 1)));
            Assert.Equal(LandCover.Marsh, grid.CoverAt(grid.Index(2, 1)));
            Assert.Equal(blob, Wrmb.WriteTerrain(grid));
        }

        [Fact]
        public void Terrain_FordBit_OverridesLandCover()
        {
            byte[] blob = new TerrainGridBuilder(2, 1, 8940)
                .Fill(0, LandCover.Water)
                .SetCell(1, 0, 0, LandCover.Ford)
                .ToBlob();

            TerrainGrid grid = Wrmb.ReadTerrain(blob);
            Assert.Equal(LandCover.Water, grid.CoverAt(0));
            Assert.Equal(LandCover.Ford, grid.CoverAt(1));

            WrmbFile file = WrmbFile.Parse(blob);
            byte[] mvlc = file.SectionBytes(file.RequireSection("MVLC"));
            byte[] mvfd = file.SectionBytes(file.RequireSection("MVFD"));
            Assert.Equal(Wrmb.WrmbCoverWater, mvlc[0]);
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[1]);
            Assert.Equal(0, mvfd[0] & 1);
            Assert.Equal(2, mvfd[0] & 2);
        }

        [Fact]
        public void Terrain_WriteRead_KeepsFordWheeledPassableAndMarshBlocked()
        {
            byte[] blob = new TerrainGridBuilder(3, 3, MovementConfig.SyntheticFixtureCellSizeCm)
                .Fill(0, LandCover.Field)
                .SetCell(1, 0, 0, LandCover.Marsh)
                .SetCell(1, 1, 0, LandCover.Ford)
                .SetCell(1, 2, 0, LandCover.Marsh)
                .ToBlob();

            TerrainGrid grid = Wrmb.ReadTerrain(blob);
            Assert.Equal(LandCover.Marsh, grid.CoverAt(grid.Index(1, 0)));
            Assert.Equal(LandCover.Ford, grid.CoverAt(grid.Index(1, 1)));
            Assert.Equal(LandCover.Marsh, grid.CoverAt(grid.Index(1, 2)));

            WrmbFile file = WrmbFile.Parse(blob);
            byte[] mvlc = file.SectionBytes(file.RequireSection("MVLC"));
            byte[] mvfd = file.SectionBytes(file.RequireSection("MVFD"));
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[grid.Index(1, 0)]);
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[grid.Index(1, 1)]);
            Assert.Equal(Wrmb.WrmbCoverMarsh, mvlc[grid.Index(1, 2)]);
            Assert.Equal(0, mvfd[0] & (1 << grid.Index(1, 0)));
            Assert.NotEqual(0, mvfd[0] & (1 << grid.Index(1, 1)));
            Assert.Equal(0, mvfd[0] & (1 << grid.Index(1, 2)));

            MovementTables tables = MovementTables.Default();
            Assert.Equal(0, tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Marsh, 0));
            Assert.True(tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Ford, 0) > 0);

            byte[] roads = new RoadGraphBuilder().AddNode(1, 5000, 15000).ToBlob();
            MovementService svc = MovementService.Create(roads, blob, tables);
            PathResult acrossFord = svc.PathOffRoad(5000, 15000, 25000, 15000, MovementClass.Wheeled);
            PathResult fromMarsh = svc.PathOffRoad(15000, 5000, 25000, 5000, MovementClass.Wheeled);
            Assert.True(acrossFord.Reachable);
            Assert.False(fromMarsh.Reachable);
        }

        [Fact]
        public void Terrain_WrmbCoverCodes_MapToSimEnums()
        {
            Assert.Equal((byte)LandCover.Water, Wrmb.MapLandCover(0));
            Assert.Equal((byte)LandCover.Field, Wrmb.MapLandCover(1));
            Assert.Equal((byte)LandCover.Scrub, Wrmb.MapLandCover(2));
            Assert.Equal((byte)LandCover.Urban, Wrmb.MapLandCover(3));
            Assert.Equal((byte)LandCover.Forest, Wrmb.MapLandCover(4));
            Assert.Equal((byte)LandCover.Marsh, Wrmb.MapLandCover(5));
            Assert.Equal((byte)LandCover.Water, Wrmb.MapLandCover(6));
            Assert.Equal((byte)LandCover.Scrub, Wrmb.MapLandCover(7));
        }

        [Fact]
        public void Terrain_MoveCellsPerCell_DividesMileCellSize()
        {
            const int mileCm = 160938;
            const int per = 18;
            const int side = 18;
            int cells = side * side;
            ushort[] slope = new ushort[cells];
            byte[] cover = new byte[cells];
            for (int i = 0; i < cells; i++)
            {
                cover[i] = Wrmb.WrmbCoverField;
            }

            WrmbMetaFields meta = new WrmbMetaFields
            {
                MapWidthCm = mileCm,
                MapHeightCm = mileCm,
                CellSizeCm = mileCm,
                GridWidth = 1,
                GridHeight = 1,
                MoveWidth = side,
                MoveHeight = side,
                MoveCellsPerCell = per
            };

            byte[] slopeBytes = new byte[cells * 2];
            byte[] ford = new byte[(cells + 7) / 8];
            byte[] blob = WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", Wrmb.WriteMeta(meta), Wrmb.MetaElementSize),
                    new WrmbSectionPayload("MVSL", slopeBytes, 2),
                    new WrmbSectionPayload("MVLC", cover, 1),
                    new WrmbSectionPayload("MVFD", ford, 1)
                });

            TerrainGrid grid = Wrmb.ReadTerrain(blob);
            Assert.Equal(side, grid.Width);
            Assert.Equal(8941, grid.CellSizeCm);
            Assert.Equal(mileCm / per, grid.CellSizeCm);
        }

        [Fact]
        public void FloorDiv_HandlesNegativeWorldCoords()
        {
            Assert.Equal(-1, TerrainGrid.FloorDiv(-1, 8941));
            Assert.Equal(-1, TerrainGrid.FloorDiv(-8941, 8941));
            Assert.Equal(-2, TerrainGrid.FloorDiv(-8942, 8941));
            Assert.Equal(0, TerrainGrid.FloorDiv(0, 8941));
            Assert.Equal(1, TerrainGrid.FloorDiv(8941, 8941));
        }

        [Fact]
        public void UnknownLandCover_IsRejected()
        {
            byte[] blob = new TerrainGridBuilder(1, 1, 8941).ToBlob();
            WrmbFile file = WrmbFile.Parse(blob);
            WrmbDirEntry mvlc = file.RequireSection("MVLC");
            byte[] tampered = (byte[])blob.Clone();
            tampered[mvlc.Offset] = 99;
            PatchSectionCrc(tampered, file, "MVLC");
            RecomputeContentHash(tampered);
            Assert.Throws<InvalidDataException>(() => Wrmb.ReadTerrain(tampered));
        }

        [Fact]
        public void FormatVersion1_WithoutV1Final_IsRejected()
        {
            byte[] blob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            blob[4] = 1;
            blob[5] = 0;
            RecomputeContentHash(blob);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => WrmbFile.Parse(blob));
            Assert.Contains("V1_FINAL", ex.Message);
        }

        [Fact]
        public void FormatVersion1_RequiresPipelineMetaSize()
        {
            byte[] blob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            blob[4] = 1;
            blob[5] = 0;
            uint flags = ReadU32(blob, 12) | WrmbFile.FlagV1Final;
            blob[12] = (byte)flags;
            blob[13] = (byte)(flags >> 8);
            blob[14] = (byte)(flags >> 16);
            blob[15] = (byte)(flags >> 24);
            RecomputeContentHash(blob);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => WrmbFile.Parse(blob));
            Assert.Contains("META", ex.Message);
        }

        [Fact]
        public void FormatVersion1_TerrainWithoutWtra_IsRejected()
        {
            WrmbMetaFields meta = new WrmbMetaFields
            {
                MapWidthCm = 1,
                MapHeightCm = 1,
                CellSizeCm = 1,
                GridWidth = 1,
                GridHeight = 1,
                MoveWidth = 1,
                MoveHeight = 1,
                MoveCellsPerCell = 1
            };
            byte[] blob = WrmbFile.Encode(
                WrmbFile.FormatVersionV1,
                WrmbFile.FlagV1Final,
                new[]
                {
                    new WrmbSectionPayload("META", Wrmb.WritePipelineMeta(meta, 1), Wrmb.PipelineMetaElementSize),
                    new WrmbSectionPayload("MVSL", new byte[2], 2),
                    new WrmbSectionPayload("MVLC", new byte[] { Wrmb.WrmbCoverField }, 1),
                    new WrmbSectionPayload("MVFD", new byte[1], 1)
                });

            WrmbFile file = WrmbFile.Parse(blob);
            Assert.Equal(WrmbFile.FormatVersionV1, file.FileFormatVersion);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => Wrmb.ReadTerrain(file));
            Assert.Contains("WTRA", ex.Message);
        }

        [Fact]
        public void FormatVersion1_SkipsWtraContent_AndHashesPayloadWithoutIt()
        {
            WrmbMetaFields meta = new WrmbMetaFields
            {
                MapWidthCm = 1,
                MapHeightCm = 1,
                CellSizeCm = 1,
                GridWidth = 1,
                GridHeight = 1,
                MoveWidth = 1,
                MoveHeight = 1,
                MoveCellsPerCell = 1
            };
            byte[] slope = { 0, 0 };
            byte[] cover = { Wrmb.WrmbCoverMarsh };
            byte[] ford = { 0 };
            byte[] elev = { 0, 0 };
            byte[] wtraA = { 1, 2, 3, 4 };
            byte[] wtraB = { 9, 9, 9, 9 };
            byte[] a = WrmbFile.Encode(
                WrmbFile.FormatVersionV1,
                WrmbFile.FlagV1Final,
                TerrainV1Sections(meta, slope, cover, ford, elev, wtraA));
            byte[] b = WrmbFile.Encode(
                WrmbFile.FormatVersionV1,
                WrmbFile.FlagV1Final,
                TerrainV1Sections(meta, slope, cover, ford, elev, wtraB));

            TerrainGrid grid = Wrmb.ReadTerrain(a);
            Assert.Equal(LandCover.Marsh, grid.CoverAt(0));
            WrmbFile fa = WrmbFile.Parse(a);
            WrmbFile fb = WrmbFile.Parse(b);
            Assert.Equal(fa.TerrainPayloadSha256(), fb.TerrainPayloadSha256());
            Assert.False(BytesEqual(fa.ContentHash, fb.ContentHash));
        }

        [Fact]
        public void TwoFiles_LoadIndependently()
        {
            byte[] map = new RoadGraphBuilder()
                .AddNode(1, 0, 0)
                .AddNode(2, 100, 0)
                .AddEdge(1, 1, 2, 100, RoadTier.Highway, RoadSurface.Asphalt)
                .ToBlob();
            byte[] terrain = new TerrainGridBuilder(2, 1, 8941).Fill(0, LandCover.Field).ToBlob();
            MovementService svc = MovementService.Create(map, terrain, MovementTables.Default());
            Assert.Equal(2, svc.Graph.NodeCount);
            Assert.Equal(8941, svc.Terrain.CellSizeCm);
            Assert.Equal(RoadTier.Highway, svc.Graph.GetEdgeTier(0));
        }

        private static WrmbSectionPayload[] TerrainV1Sections(
            WrmbMetaFields meta,
            byte[] slope,
            byte[] cover,
            byte[] ford,
            byte[] elev,
            byte[] wtra)
        {
            return new[]
            {
                new WrmbSectionPayload("META", Wrmb.WritePipelineMeta(meta, 1), Wrmb.PipelineMetaElementSize),
                new WrmbSectionPayload("MVSL", slope, 2),
                new WrmbSectionPayload("MVLC", cover, 1),
                new WrmbSectionPayload("MVFD", ford, 1),
                new WrmbSectionPayload("MVEL", elev, 2),
                new WrmbSectionPayload("WTRA", wtra, 1)
            };
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static WrmbMetaFields RoadMeta(int nodes, int edges)
        {
            return new WrmbMetaFields
            {
                MapWidthCm = 1,
                MapHeightCm = 1,
                CellSizeCm = 1,
                GridWidth = 1,
                GridHeight = 1,
                RoadNodeCount = nodes,
                RoadEdgeCount = edges,
                MoveWidth = 1,
                MoveHeight = 1,
                MoveCellsPerCell = 1
            };
        }

        private static byte[] SectionOf(byte[] blob, string tag)
        {
            WrmbFile file = WrmbFile.Parse(blob);
            return file.SectionBytes(file.RequireSection(tag));
        }

        private static void PatchSectionCrc(byte[] blob, WrmbFile original, string tag)
        {
            int index = -1;
            WrmbDirEntry entry = default;
            for (int i = 0; i < original.Directory.Length; i++)
            {
                if (original.Directory[i].Tag == tag)
                {
                    index = i;
                    entry = original.Directory[i];
                    break;
                }
            }

            Assert.True(index >= 0);
            uint crc = WrmbCrc32.Compute(blob, entry.Offset, entry.ByteLength);
            int crcOff = original.DirectoryOffset + index * WrmbFile.DirectoryEntrySize + 32;
            blob[crcOff] = (byte)crc;
            blob[crcOff + 1] = (byte)(crc >> 8);
            blob[crcOff + 2] = (byte)(crc >> 16);
            blob[crcOff + 3] = (byte)(crc >> 24);
        }

        private static void RecomputeContentHash(byte[] blob)
        {
            byte[] input = (byte[])blob.Clone();
            for (int i = 0; i < 32; i++)
            {
                input[WrmbFile.HeaderSize - 32 + i] = 0;
            }

            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(input);
            }

            Array.Copy(hash, 0, blob, WrmbFile.HeaderSize - 32, 32);
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            return data[offset]
                | ((uint)data[offset + 1] << 8)
                | ((uint)data[offset + 2] << 16)
                | ((uint)data[offset + 3] << 24);
        }

        private static void WriteI32(byte[] data, int offset, int value)
        {
            uint u = (uint)value;
            data[offset] = (byte)u;
            data[offset + 1] = (byte)(u >> 8);
            data[offset + 2] = (byte)(u >> 16);
            data[offset + 3] = (byte)(u >> 24);
        }
    }

    public sealed class MovementTablesTests
    {
        [Fact]
        public void BuiltinJson_MatchesPackagedFile()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "movement-tables-v1.json");
            Assert.True(File.Exists(path), "Expected " + path);
            string file = File.ReadAllText(path).Replace("\r\n", "\n").Trim();
            string builtin = MovementTables.BuiltinJson.Replace("\r\n", "\n").Trim();
            Assert.Equal(file, builtin);
        }

        [Fact]
        public void Speed_FloorFormula()
        {
            Assert.Equal(32, MovementTables.Speed(80, 400, 1000));
            Assert.Equal(0, MovementTables.Speed(8, 50, 50));
            Assert.Equal(11, MovementTables.Speed(80, 400, 350));
        }

        [Fact]
        public void Wheeled_RefusesSlope300_Allows299()
        {
            MovementTables tables = MovementTables.Default();
            Assert.Equal(0, tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Field, 300));
            Assert.True(tables.OffRoadSpeedCmPerTick(MovementClass.Wheeled, LandCover.Field, 299) > 0);
            Assert.True(tables.OffRoadSpeedCmPerTick(MovementClass.Tracked, LandCover.Field, 300) > 0);
            Assert.Equal(0, tables.OffRoadSpeedCmPerTick(MovementClass.Tracked, LandCover.Field, 600));
            Assert.True(tables.OffRoadSpeedCmPerTick(MovementClass.Foot, LandCover.Field, 999) > 0);
            Assert.Equal(0, tables.OffRoadSpeedCmPerTick(MovementClass.Foot, LandCover.Field, 1000));
        }

        [Fact]
        public void RoadsIgnoreSlope_AirIgnoresTerrain()
        {
            MovementTables tables = MovementTables.Default();
            int road = tables.RoadSpeedCmPerTick(MovementClass.Wheeled, RoadSurface.Asphalt);
            Assert.Equal(80, road);
            Assert.Equal(200, tables.OffRoadSpeedCmPerTick(MovementClass.Air, LandCover.Water, 9000));
            Assert.Equal(200, tables.RoadSpeedCmPerTick(MovementClass.Air, RoadSurface.Dirt));
        }

        [Fact]
        public void Parse_RejectsFractionalNumbers()
        {
            Assert.Throws<FormatException>(() => MovementTables.Parse("{\"schemaVersion\": 1.0}"));
        }

        [Fact]
        public void PackagedFile_Parses()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "movement-tables-v1.json");
            MovementTables fromFile = MovementTables.Parse(File.ReadAllText(path));
            MovementTables fromBuiltin = MovementTables.Default();
            Assert.Equal(
                fromBuiltin.OffRoadSpeedCmPerTick(MovementClass.Foot, LandCover.Field, 400),
                fromFile.OffRoadSpeedCmPerTick(MovementClass.Foot, LandCover.Field, 400));
        }
    }
}
