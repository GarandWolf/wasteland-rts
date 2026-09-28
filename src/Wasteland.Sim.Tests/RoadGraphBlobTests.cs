using System;
using System.IO;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class RoadGraphBlobTests
    {
        [Fact]
        public void OneNode_ExactLittleEndianBytes()
        {
            byte[] blob = new RoadGraphBuilder()
                .AddNode(1, 0, 0)
                .ToBlob();

            byte[] expected =
            {
                (byte)'W', (byte)'R', (byte)'G', (byte)'1',
                1, 0,
                32, 0,
                1, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                1, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0,
                0, 0
            };

            Assert.Equal(expected, blob);
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

            RoadGraph graph = RoadGraphBlob.Read(blob);
            Assert.Equal(2, graph.NodeCount);
            Assert.Equal(3, graph.GetNodeId(0));
            Assert.Equal(7, graph.GetNodeId(1));
            Assert.Equal(4, graph.GetEdgeId(0));
            Assert.Equal(9, graph.GetEdgeId(1));
            Assert.Equal(10, graph.OriginXCm);
            Assert.True((graph.GetNodeFlags(0) & RoadNodeFlags.BridgeHead) != 0);
            Assert.True((graph.GetEdgeFlags(1) & RoadEdgeFlags.BridgeSpan) != 0);

            byte[] again = RoadGraphBlob.Write(graph);
            Assert.Equal(blob, again);

            RoadGraph second = RoadGraphBlob.Read(again);
            Assert.Equal(graph.NodeCount, second.NodeCount);
            Assert.Equal(graph.GetNodeXCm(1), second.GetNodeXCm(1));
        }

        [Fact]
        public void UnsortedNodes_AreRejected()
        {
            byte[] blob = new RoadGraphBuilder()
                .AddNode(1, 0, 0)
                .AddNode(2, 1, 0)
                .ToBlob();

            blob[32] = 2;
            blob[48] = 1;
            Assert.Throws<System.IO.InvalidDataException>(() => RoadGraphBlob.Read(blob));
        }

        [Fact]
        public void WrongMagic_IsRejected()
        {
            byte[] blob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            blob[0] = (byte)'X';
            Assert.Throws<System.IO.InvalidDataException>(() => RoadGraphBlob.Read(blob));
        }

        [Fact]
        public void BridgeSpan_WithoutHeads_IsRejected()
        {
            Assert.Throws<System.IO.InvalidDataException>(() =>
            {
                byte[] blob = new RoadGraphBuilder()
                    .AddNode(1, 0, 0)
                    .AddNode(2, 10, 0)
                    .AddEdge(1, 1, 2, 10, RoadTier.Paved, RoadSurface.Asphalt, RoadEdgeFlags.BridgeSpan)
                    .ToBlob();
                RoadGraphBlob.Read(blob);
            });
        }
    }

    public sealed class TerrainGridBlobTests
    {
        [Fact]
        public void OneCell_ExactLittleEndianBytes()
        {
            byte[] blob = new TerrainGridBuilder(1, 1, 10000)
                .SetCell(0, 0, 0, LandCover.Field)
                .ToBlob();

            byte[] expected =
            {
                (byte)'W', (byte)'T', (byte)'G', (byte)'1',
                1, 0,
                32, 0,
                1, 0, 0, 0,
                1, 0, 0, 0,
                16, 39, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0,
                0,
                0
            };

            Assert.Equal(expected, blob);
            Assert.Equal(10000, expected[16] | (expected[17] << 8) | (expected[18] << 16) | (expected[19] << 24));
        }

        [Fact]
        public void Roundtrip_PreservesSlopeAndCover()
        {
            byte[] blob = new TerrainGridBuilder(3, 2, 5000, originXCm: -100, originYCm: 50)
                .Fill(12, LandCover.Forest)
                .SetCell(2, 1, 300, LandCover.Marsh)
                .ToBlob();

            TerrainGrid grid = TerrainGridBlob.Read(blob);
            Assert.Equal(3, grid.Width);
            Assert.Equal(2, grid.Height);
            Assert.Equal(5000, grid.CellSizeCm);
            Assert.Equal(-100, grid.OriginXCm);
            Assert.Equal(LandCover.Forest, grid.CoverAt(0));
            Assert.Equal(300, grid.SlopeAt(grid.Index(2, 1)));
            Assert.Equal(LandCover.Marsh, grid.CoverAt(grid.Index(2, 1)));
            Assert.Equal(blob, TerrainGridBlob.Write(grid));
        }

        [Fact]
        public void FloorDiv_HandlesNegativeWorldCoords()
        {
            Assert.Equal(-1, TerrainGrid.FloorDiv(-1, 10000));
            Assert.Equal(-1, TerrainGrid.FloorDiv(-10000, 10000));
            Assert.Equal(-2, TerrainGrid.FloorDiv(-10001, 10000));
            Assert.Equal(0, TerrainGrid.FloorDiv(0, 10000));
            Assert.Equal(1, TerrainGrid.FloorDiv(10000, 10000));
        }

        [Fact]
        public void UnknownLandCover_IsRejected()
        {
            byte[] blob = new TerrainGridBuilder(1, 1).ToBlob();
            blob[34] = 99;
            Assert.Throws<System.IO.InvalidDataException>(() => TerrainGridBlob.Read(blob));
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
