#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Audio;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class MapPresentationTests
    {
        [Fact]
        public void UnityAxes_AreEastX_NorthZ_WithElevationUp()
        {
            double x;
            double y;
            double z;
            MapSpace.ToUnity(100, 0, 0, out x, out y, out z);
            Assert.Equal(1.0, x);
            Assert.Equal(0.0, y);
            Assert.Equal(0.0, z);

            MapSpace.ToUnity(0, 50, 100, out x, out y, out z);
            Assert.Equal(0.0, x);
            Assert.Equal(0.5, y);
            Assert.Equal(1.0, z);
        }

        [Fact]
        public void CellLookup_Uses64BitMulDiv_NotARoundedCellSize()
        {
            const int extent = 16093400;
            const int width = 1800;
            const int x = 8940;
            int column = MapSpace.Column(x, 0, width, extent);
            Assert.Equal(0, column);
            Assert.NotEqual(x / 8940, column);
        }

        [Fact]
        public void Hillshade_LightsNorthWestFaces_AndDoesNotRewriteElevation()
        {
            ushort[] flat = new ushort[9];
            float[] shade = new float[9];
            Hillshade.Compute(3, 3, flat, 0, 1, 300, 300, Hillshade.Exaggeration, shade);
            Assert.Equal(0.707106781f, shade[4], 5);

            ushort[] risesEast = Grid(0, 0, 100, 0, 0, 100, 0, 0, 100);
            ushort[] copy = (ushort[])risesEast.Clone();
            Hillshade.Compute(3, 3, risesEast, 0, 1, 300, 300, Hillshade.Exaggeration, shade);
            Assert.Equal(copy, risesEast);
            float east = shade[4];

            ushort[] risesWest = Grid(100, 0, 0, 100, 0, 0, 100, 0, 0);
            Hillshade.Compute(3, 3, risesWest, 0, 1, 300, 300, Hillshade.Exaggeration, shade);
            Assert.True(east > shade[4]);

            float[] milder = new float[9];
            Hillshade.Compute(3, 3, risesEast, 0, 1, 300, 300, 1.0, milder);
            Assert.NotEqual(milder[4], east);
            Hillshade.Compute(3, 3, flat, 0, 1, 300, 300, 1.0, milder);
            Assert.Equal(0.707106781f, milder[4], 5);
        }

        [Fact]
        public void PaintOrder_IsPaper_ThenHillshade_ThenWater_ThenWash()
        {
            Rgb paper = Rgb.FromHex(PaperPaint.Paper);
            Rgb shaded = PaperPaint.MultiplyHillshade(paper, 0f);
            Assert.Equal((byte)184, shaded.R);

            Rgb blocking = PaperPaint.PaintWater(shaded, 1);
            Assert.Equal(Rgb.FromHex(PaperPaint.WaterFill).R, blocking.R);
            Assert.Equal(Rgb.FromHex(PaperPaint.FordGravel).R, PaperPaint.PaintWater(shaded, 3).R);
            Assert.Equal(shaded.R, PaperPaint.PaintWater(shaded, 0).R);

            Rgb fordable = PaperPaint.PaintWater(shaded, 2);
            byte expected = (byte)(Rgb.FromHex(PaperPaint.WaterFill).R * PaperPaint.FordableAlpha + shaded.R * (1f - PaperPaint.FordableAlpha) + 0.5f);
            Assert.Equal(expected, fordable.R);

            Rgb wetland = PaperPaint.PaintWater(shaded, 4);
            byte wet = (byte)(Rgb.FromHex(PaperPaint.WaterFill).R * PaperPaint.WetlandAlpha + shaded.R * (1f - PaperPaint.WetlandAlpha) + 0.5f);
            Assert.Equal(wet, wetland.R);

            Rgb owner = Rgb.FromHex(MarkerArt.Remnant);
            Rgb washed = PaperPaint.PaintWash(blocking, owner, PaperPaint.WashAlpha);
            Assert.NotEqual(blocking.R, washed.R);
            Assert.Equal(2, PaperPaint.EdgePx(1));
            Assert.Equal(1, PaperPaint.EdgePx(2));
            Assert.Equal(0, PaperPaint.EdgePx(3));
            Assert.Equal(0, PaperPaint.EdgePx(4));
        }

        [Fact]
        public void Roads_KeepScreenPixels_AtEveryZoom()
        {
            RoadStroke paved = RoadArt.ForSurface(RoadArt.Paved);
            Assert.Equal(4f, paved.BodyPx);
            Assert.Equal(1f, paved.InkSidePx);
            Assert.Equal(6f, paved.TotalPx);
            Assert.Equal(RoadArt.Amber, paved.BodyRgb);
            Assert.False(paved.Dashed);

            RoadStroke gravel = RoadArt.ForSurface(RoadArt.Gravel);
            Assert.Equal(2f, gravel.BodyPx);
            Assert.True(gravel.InkOnly);
            Assert.False(gravel.Dashed);

            RoadStroke dirt = RoadArt.ForSurface(RoadArt.Dirt);
            Assert.Equal(1.5f, dirt.BodyPx);
            Assert.True(dirt.Dashed);
            Assert.Equal(6f, dirt.DashOnPx);
            Assert.Equal(4f, dirt.DashOffPx);

            float close = MapSpace.WorldWidth(paved.BodyPx, 10f, 100);
            float far = MapSpace.WorldWidth(paved.BodyPx, 20f, 100);
            Assert.Equal(0.8f, close, 5);
            Assert.Equal(1.6f, far, 5);
            Assert.Equal(4f, close / MapSpace.WorldUnitsPerPixel(10f, 100), 5);
            Assert.Equal(4f, far / MapSpace.WorldUnitsPerPixel(20f, 100), 5);
        }

        [Fact]
        public void Markers_UsePopulationClass_SeatTowns_AndScenarioHq()
        {
            Assert.Equal(10, MarkerArt.TownDiameterPx(0));
            Assert.Equal(14, MarkerArt.TownDiameterPx(1));
            Assert.Equal(18, MarkerArt.TownDiameterPx(2));
            Assert.Equal(22, MarkerArt.TownDiameterPx(3));
            Assert.False(MarkerArt.ShowsCaptureRing(false));
            Assert.True(MarkerArt.ShowsCaptureRing(true));
            Assert.True(MarkerArt.ShowsHq(true));
            Assert.False(MarkerArt.ShowsHq(false));
            Assert.Equal(MarkerArt.Remnant, MarkerArt.OwnerColor(1, MarkerArt.RemnantFaction).R << 16 | MarkerArt.OwnerColor(1, MarkerArt.RemnantFaction).G << 8 | MarkerArt.OwnerColor(1, MarkerArt.RemnantFaction).B);
            Assert.Equal(Rgb.FromHex(MarkerArt.Neutral).R, MarkerArt.OwnerColor(-1, null).R);
            Assert.Equal(1.5f, MarkerArt.OutlinePx);
            Assert.Equal(4f, MarkerArt.StripePx);
            Assert.Equal(2f, MarkerArt.SelectionBonePx);
            Assert.Equal(1f, MarkerArt.SelectionInkPx);
            Assert.Equal(22, MarkerArt.BattalionWidthPx);
            Assert.Equal(14, MarkerArt.BattalionHeightPx);
        }

        [Fact]
        public void MissingGlyph_IsAPlainFrame_AndWarnsOnce()
        {
            GlyphBook book = new GlyphBook();
            Assert.Equal(BattalionGlyph.Rifle, book.Resolve("remnant_motor_rifles"));
            Assert.Equal(BattalionGlyph.Plain, book.Resolve("not_in_the_table"));
            Assert.Equal(BattalionGlyph.Plain, book.Resolve("not_in_the_table"));
            Assert.Single(book.Warnings);
            Assert.Equal("not_in_the_table", book.Warnings[0]);
        }

        [Fact]
        public void TownsAndRoads_ArePlacedFromTheFile_NotHardCodedIds()
        {
            byte[] main;
            byte[] terrain;
            ArtMapFixture.WritePair(out main, out terrain);
            MapDocument map = MapDocument.Load(main, terrain);

            MapTown seat = Require(map, "Seat Alpha");
            double x;
            double y;
            double z;
            MapSpace.ToUnity(seat.XCm, map.ElevationCmAt(seat.XCm, seat.YCm), seat.YCm, out x, out y, out z);
            Assert.Equal(10.0, x);
            Assert.Equal(1.6, y);
            Assert.Equal(10.0, z);
            Assert.Equal(ArtMapFixture.ElevOffsetCm, map.ElevOffsetCm);
            Assert.Equal(ArtMapFixture.ElevUnitCm, map.ElevUnitCm);
            Assert.Equal(160, map.ElevationCmAt(seat.XCm, seat.YCm));
            Assert.Equal(map.Terrain.IndexOf(seat.XCm, seat.YCm), map.CellIndex(seat.XCm, seat.YCm));
            Assert.Equal(1, map.WaterAt(seat.XCm, seat.YCm));

            MapRoad road = null;
            for (int i = 0; i < map.Roads.Length; i++)
            {
                if (map.Roads[i].FromNodeId == seat.RoadNodeId || map.Roads[i].ToNodeId == seat.RoadNodeId)
                {
                    road = map.Roads[i];
                }
            }

            Assert.NotNull(road);
            int nodeX;
            int nodeY;
            Assert.True(map.TryNode(road.FromNodeId, out nodeX, out nodeY));
            Assert.Equal(nodeX, road.XCm[0]);
            Assert.Equal(nodeY, road.YCm[0]);
            Assert.Equal(4000, road.XCm[1]);
            Assert.Equal(RoadArt.Paved, road.Surface);

            map.BindScenarioSeats(new ulong[] { ArtMapFixture.SeatKey });
            Assert.True(seat.IsSeatTown);
            Assert.True(seat.IsScenarioHq);
            Assert.True(MarkerArt.ShowsHq(seat.IsScenarioHq));
            Assert.True(MarkerArt.ShowsCaptureRing(seat.IsSeatTown));

            MapTown prize = Require(map, "Prize");
            Assert.True(prize.IsSeatTown);
            Assert.False(prize.IsScenarioHq);
            Assert.True(MarkerArt.ShowsCaptureRing(prize.IsSeatTown));
            Assert.False(MarkerArt.ShowsHq(prize.IsScenarioHq));

            string[] noRing = { "Dallesport", "Celilo Village", "Wishram", "Bingen" };
            for (int i = 0; i < noRing.Length; i++)
            {
                MapTown town = Require(map, noRing[i]);
                Assert.False(town.IsSeatTown);
                Assert.False(MarkerArt.ShowsCaptureRing(town.IsSeatTown));
            }
        }

        static MapTown Require(MapDocument map, string name)
        {
            MapTown town;
            Assert.True(map.TryTownByName(name, out town));
            return town;
        }

        static ushort[] Grid(params int[] values)
        {
            ushort[] raw = new ushort[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                raw[i] = (ushort)values[i];
            }

            return raw;
        }
    }

    public sealed class GorgeMapPresentationTests : IClassFixture<GorgeMapHolder>
    {
        readonly GorgeMapHolder _gorge;

        public GorgeMapPresentationTests(GorgeMapHolder gorge)
        {
            _gorge = gorge;
        }

        [Fact]
        public void GorgeTownsAndRoads_ComeFromTheMainFile()
        {
            MapDocument map = _gorge.Map;
            MapTown hood = Require(map, "Hood River");
            MapTown salmon = Require(map, "White Salmon");
            Assert.Equal(0, map.ElevOffsetCm);
            Assert.Equal(10, map.ElevUnitCm);
            Assert.Equal(map.Terrain.IndexOf(hood.XCm, hood.YCm), map.CellIndex(hood.XCm, hood.YCm));
            Assert.Equal(0, map.CellIndex(8940, 0) % map.Width);
            Assert.NotEqual(0, 8940 / 8940);

            int dm = map.Terrain.Sample(hood.XCm, hood.YCm).ElevationDm;
            Assert.Equal(dm * 10, map.ElevationCmAt(hood.XCm, hood.YCm));

            double x;
            double y;
            double z;
            MapSpace.ToUnity(hood.XCm, map.ElevationCmAt(hood.XCm, hood.YCm), hood.YCm, out x, out y, out z);
            Assert.Equal(hood.XCm / 100.0, x);
            Assert.Equal(hood.YCm / 100.0, z);

            MapRoad road = null;
            for (int i = 0; i < map.Roads.Length; i++)
            {
                if (map.Roads[i].FromNodeId == hood.RoadNodeId || map.Roads[i].ToNodeId == hood.RoadNodeId)
                {
                    road = map.Roads[i];
                    break;
                }
            }

            Assert.NotNull(road);
            int nodeX;
            int nodeY;
            int endId = road.FromNodeId == hood.RoadNodeId ? road.FromNodeId : road.ToNodeId;
            Assert.True(map.TryNode(endId, out nodeX, out nodeY));
            bool listed = false;
            for (int p = 0; p < road.XCm.Length; p++)
            {
                if (road.XCm[p] == nodeX && road.YCm[p] == nodeY)
                {
                    listed = true;
                }
            }

            Assert.True(listed);

            map.BindScenarioSeats(_gorge.HomeKeys);
            Assert.True(hood.IsScenarioHq);
            Assert.True(Require(map, "Goldendale").IsScenarioHq);
            MapTown dalles = Require(map, "The Dalles");
            Assert.True(dalles.IsSeatTown);
            Assert.False(dalles.IsScenarioHq);
            Assert.True(MarkerArt.ShowsCaptureRing(dalles.IsSeatTown));

            string[] noRing = { "Dallesport", "Celilo Village", "Wishram", "Bingen" };
            for (int i = 0; i < noRing.Length; i++)
            {
                MapTown town = Require(map, noRing[i]);
                Assert.False(town.IsSeatTown);
                Assert.False(town.IsScenarioHq);
                Assert.False(MarkerArt.ShowsCaptureRing(town.IsSeatTown));
            }

            Assert.NotEqual(hood.XCm, salmon.XCm);

            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(0, new[]
            {
                new BedEvent(CombatContestEvents.Started, hood.Id, hood.XCm, hood.YCm),
                new BedEvent(CombatContestEvents.Started, salmon.Id, salmon.XCm, salmon.YCm),
                new BedEvent(CombatContestEvents.Started, dalles.Id, dalles.XCm, dalles.YCm)
            });
            List<AudibleBed> beds = new List<AudibleBed>();
            clock.CopyAudible(beds);
            Assert.Equal(2, beds.Count);
            bool shared = false;
            for (int i = 0; i < beds.Count; i++)
            {
                if (beds[i].MemberCount == 2)
                {
                    shared = true;
                }
            }

            Assert.True(shared);
        }

        static MapTown Require(MapDocument map, string name)
        {
            MapTown town;
            Assert.True(map.TryTownByName(name, out town), name);
            return town;
        }
    }

    public sealed class GorgeMapHolder
    {
        public GorgeMapHolder()
        {
            string root = FindRepoRoot();
            byte[] main = File.ReadAllBytes(Path.Combine(root, "data", "golden", "golden-ci-columbia-gorge-100.v1-marsh.wrmb"));
            byte[] terrain = File.ReadAllBytes(Path.Combine(root, "data", "golden", "golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb"));
            if (main.Length < 1000 || terrain.Length < 1000 || main[0] != (byte)'W')
            {
                throw new InvalidDataException("Marsh WRMB files are not materialized.");
            }

            Map = MapDocument.Load(main, terrain);
            string json = File.ReadAllText(Path.Combine(root, "data", "balance", "scenarios", "columbia_gorge_100mi_mvp.json"));
            HomeKeys = ReadHomeKeys(json);
        }

        public MapDocument Map { get; }

        public ulong[] HomeKeys { get; }

        static ulong[] ReadHomeKeys(string json)
        {
            ulong[] keys = new ulong[8];
            int count = 0;
            int at = 0;
            while (count < keys.Length)
            {
                int found = json.IndexOf("\"homeTerritoryKey\"", at, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                int hex = json.IndexOf("0x", found, StringComparison.Ordinal);
                int end = json.IndexOf('"', hex);
                keys[count] = Convert.ToUInt64(json.Substring(hex, end - hex), 16);
                count++;
                at = end;
            }

            ulong[] trimmed = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                trimmed[i] = keys[i];
            }

            return trimmed;
        }

        static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "balance", "scenarios", "columbia_gorge_100mi_mvp.json")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
