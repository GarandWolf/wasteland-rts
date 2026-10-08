using System;
using System.IO;
using System.Text;
using Wasteland.Sim.Scenario;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class ScenarioLoaderTests
    {
        private const string ScenarioId = "columbia_gorge_100mi_mvp";
        private const string ScenarioRelative = "data/balance/scenarios/columbia_gorge_100mi_mvp.json";
        private const string MapSha256 = "76a801a37862ca5858f455ba44a73d48434791db07bb8558eb4086c6afd2107f";
        private const ulong RemnantHome = 0x1000000008FF3E87UL;
        private const ulong CompactHome = 0x1000000008FFA335UL;
        private const ulong NeutralPrize = 0x10000000B3787512UL;

        [Fact]
        public void LoadsGorgeMvp_AgainstV1MarshMap()
        {
            SimScenario scenario = Load();
            SimScenario again = Load();

            Assert.Equal(ScenarioId, scenario.Id);
            Assert.Equal("golden_ci_columbia_gorge_100_v1_marsh", scenario.MapFile);
            Assert.Equal(1, scenario.MapFormatVersion);
            Assert.Equal(MapSha256, scenario.MapFileSha256);
            Assert.Equal(607152, GoldenMapV1MarshTests.MapBytes);
            Assert.Equal(607152, MarshMapLength());
            Assert.Equal(scenario.ContentHash, again.ContentHash);
            Assert.Equal(scenario.JsonHash, again.JsonHash);
            Assert.NotEqual(0UL, scenario.ContentHash);
            Assert.Equal(2, scenario.SeatCount);
            Assert.Equal(2, scenario.MatchRules.AllowedFactionCount);
            Assert.Equal(1, scenario.MatchRules.AllowedAiSeats);
            Assert.Equal(ScenarioMatchRules.VictoryCaptureEnemyHq, scenario.MatchRules.Victory);
            Assert.Equal(ScenarioMatchRules.OwnershipHoldSeat, scenario.MatchRules.TerritoryOwnershipRule);
            Assert.False(scenario.MatchRules.FuelEnabled);
            Assert.Equal(ScenarioMatchRules.SimultaneousHqLossDraw, scenario.MatchRules.SimultaneousHqLoss);
            Assert.True(scenario.MatchRules.HasNeutralPrize);
            Assert.Equal(NeutralPrize, scenario.MatchRules.NeutralPrizeTerritoryKey);
            Assert.True(scenario.IsNeutralTerritory(NeutralPrize));
            Assert.Equal(11, scenario.NeutralTerritoryKeys.Length);

            ScenarioSeat seat0 = scenario.SeatAt(0);
            Assert.Equal(0, seat0.Slot);
            Assert.Equal("remnant_command", seat0.Faction);
            Assert.Equal(ScenarioController.Player, seat0.Controller);
            Assert.Null(seat0.AiPersonality);
            Assert.False(seat0.IsAi);
            Assert.Equal(RemnantHome, seat0.HomeTerritoryKey);
            Assert.Equal(11, seat0.HomeTerritoryId);
            Assert.True(seat0.HomeTownId > 0);
            Assert.Equal(300, seat0.StartingManpower);
            Assert.Equal(
                new[]
                {
                    "hq",
                    "muster_camp",
                    "heavy_works",
                    "town_walls_l1",
                    "salvage_yard"
                },
                seat0.StartingBuildings);
            Assert.Equal(
                new[]
                {
                    "remnant_motor_rifles",
                    "remnant_mech_battalion",
                    "remnant_armoured_battalion",
                    "remnant_engineers"
                },
                seat0.RaisableUnits);
            Assert.Equal(new[] { "supply_depot" }, seat0.BuildableBuildings);
            Assert.Equal(3, seat0.StartingUnits.Length);
            Assert.Equal("remnant_engineers", seat0.StartingUnits[0].UnitId);
            Assert.Equal(1, seat0.StartingUnits[0].Count);
            Assert.Equal("remnant_motor_rifles", seat0.StartingUnits[1].UnitId);
            Assert.Equal(2, seat0.StartingUnits[1].Count);
            Assert.Equal("remnant_recon", seat0.StartingUnits[2].UnitId);
            Assert.Equal(1, seat0.StartingUnits[2].Count);
            Assert.Single(seat0.AutoResearch);
            Assert.Equal("tier2", seat0.AutoResearch[0].Item);
            Assert.Equal(1200, seat0.AutoResearch[0].NotBeforeTick);
            Assert.Single(seat0.StartingHoldings);
            Assert.Equal(RemnantHome, seat0.StartingHoldings[0].TerritoryKey);

            ScenarioSeat seat1 = scenario.SeatAt(1);
            Assert.Equal(1, seat1.Slot);
            Assert.Equal("homestead_compact", seat1.Faction);
            Assert.Equal(ScenarioController.Ai, seat1.Controller);
            Assert.True(seat1.IsAi);
            Assert.Equal("homestead_compact_settler", seat1.AiPersonality);
            Assert.Equal(CompactHome, seat1.HomeTerritoryKey);
            Assert.Equal(6, seat1.HomeTerritoryId);
            Assert.True(seat1.HomeTownId > 0);
            Assert.Equal(300, seat1.StartingManpower);
            Assert.Empty(seat1.AutoResearch);
            Assert.Equal(new[] { "supply_depot" }, seat1.BuildableBuildings);
            Assert.True(scenario.TryGetSeatByFaction("homestead_compact", out ScenarioSeat byFaction));
            Assert.Equal(CompactHome, byFaction.HomeTerritoryKey);
            Assert.True(scenario.TryGetTerritory(NeutralPrize, out ScenarioDerivedTerritory neutral));
            Assert.Equal("the_dalles", neutral.Id);
            Assert.Equal(4, neutral.MapTerritoryId);
            Assert.False(scenario.IsNeutralTerritory(RemnantHome));
            Assert.False(scenario.IsNeutralTerritory(CompactHome));
        }

        [Fact]
        public void IdLists_KeepAuthoredOrderOnEveryLoad()
        {
            SimScenario first = Load();
            SimScenario second = Load();
            string[] remnantRaisable =
            {
                "remnant_motor_rifles",
                "remnant_mech_battalion",
                "remnant_armoured_battalion",
                "remnant_engineers"
            };
            string[] compactRaisable =
            {
                "compact_militia",
                "compact_rifle_battalion",
                "compact_at_battalion",
                "compact_engineers"
            };
            Assert.Equal(remnantRaisable, first.SeatAt(0).RaisableUnits);
            Assert.Equal(remnantRaisable, second.SeatAt(0).RaisableUnits);
            Assert.Equal(compactRaisable, first.SeatAt(1).RaisableUnits);
            Assert.Equal(compactRaisable, second.SeatAt(1).RaisableUnits);
            Assert.Equal(first.SeatAt(0).BuildableBuildings, second.SeatAt(0).BuildableBuildings);
            Assert.Equal(new[] { "supply_depot" }, first.SeatAt(0).BuildableBuildings);
            Assert.Equal(new[] { "supply_depot" }, first.SeatAt(1).BuildableBuildings);
            Assert.Equal("tier2", first.SeatAt(0).AutoResearch[0].Item);
            Assert.Equal(1200, first.SeatAt(0).AutoResearch[0].NotBeforeTick);
            Assert.Equal(first.SeatAt(0).AutoResearch[0].Item, second.SeatAt(0).AutoResearch[0].Item);
            Assert.Equal(first.SeatAt(0).AutoResearch[0].NotBeforeTick, second.SeatAt(0).AutoResearch[0].NotBeforeTick);
            Assert.Empty(first.SeatAt(1).AutoResearch);
            Assert.Empty(second.SeatAt(1).AutoResearch);

            string swapped = ReadScenario().Replace(
                "\"remnant_motor_rifles\",\n        \"remnant_mech_battalion\"",
                "\"remnant_mech_battalion\",\n        \"remnant_motor_rifles\"",
                StringComparison.Ordinal);
            Assert.NotEqual(ReadScenario(), swapped);
            SimScenario reordered = Load(swapped);
            Assert.Equal("remnant_mech_battalion", reordered.SeatAt(0).RaisableUnits[0]);
            Assert.Equal("remnant_motor_rifles", reordered.SeatAt(0).RaisableUnits[1]);
            Assert.NotEqual(first.SeatAt(0).RaisableUnits, reordered.SeatAt(0).RaisableUnits);
        }

        [Fact]
        public void RejectsLfsPointer_InsteadOfShaMismatch()
        {
            byte[] pointer = Encoding.ASCII.GetBytes(
                "version https://git-lfs.github.com/spec/v1\n"
                + "oid sha256:" + MapSha256 + "\n"
                + "size 607152\n");
            LfsNotPulledException ex = Assert.Throws<LfsNotPulledException>(() => Load(ReadScenario(), pointer));
            Assert.Equal(LfsNotPulledException.Code, "LfsNotPulled");
            Assert.Contains("LfsNotPulled", ex.Message);
            Assert.Equal(GoldenMapV1MarshTests.MapFileName, ex.FileName);
            Assert.Contains(GoldenMapV1MarshTests.MapFileName, ex.Message);
            Assert.DoesNotContain("does not match", ex.Message);
        }

        [Fact]
        public void RejectsUnknownKey()
        {
            string json = ReadScenario();
            int brace = json.IndexOf('{');
            string mutated = json.Substring(0, brace + 1) + "\n  \"notAScenarioKey\": 1," + json.Substring(brace + 1);
            FormatException ex = Assert.Throws<FormatException>(() => Load(mutated));
            Assert.Contains("notAScenarioKey", ex.Message);
            Assert.Contains("unknown key", ex.Message);
        }

        [Fact]
        public void RejectsFloatValue()
        {
            string mutated = ReplaceFirst(ReadScenario(), "\"startingManpower\": 300", "\"startingManpower\": 300.5");
            FormatException ex = Assert.Throws<FormatException>(() => Load(mutated));
            Assert.Contains("startingManpower", ex.Message);
            Assert.Contains("Fractional", ex.Message);
        }

        [Fact]
        public void RejectsBadHexKey()
        {
            string mutated = ReplaceFirst(
                ReadScenario(),
                "\"homeTerritoryKey\": \"0x1000000008FF3E87\"",
                "\"homeTerritoryKey\": \"0x1000000008ff3e87\"");
            FormatException ex = Assert.Throws<FormatException>(() => Load(mutated));
            Assert.Contains("homeTerritoryKey", ex.Message);
            Assert.Contains("uppercase hex", ex.Message);
        }

        [Fact]
        public void RejectsUnknownUnitId()
        {
            string mutated = ReplaceFirst(ReadScenario(), "\"remnant_recon\": 1", "\"remnant_no_such_unit\": 1");
            FormatException ex = Assert.Throws<FormatException>(() => Load(mutated));
            Assert.Contains("remnant_no_such_unit", ex.Message);
            Assert.Contains("unknown unit id", ex.Message);
        }

        [Fact]
        public void RejectsUnknownTerritory()
        {
            string mutated = ReplaceFirst(
                ReadScenario(),
                "\"homeTerritoryKey\": \"0x1000000008FF3E87\"",
                "\"homeTerritoryKey\": \"0x1000000000000001\"");
            FormatException ex = Assert.Throws<FormatException>(() => Load(mutated));
            Assert.Contains("homeTerritoryKey", ex.Message);
            Assert.Contains("unknown territory", ex.Message);
            Assert.Contains("0x1000000000000001", ex.Message);
        }

        [Fact]
        public void RejectsSha256Mismatch()
        {
            string mutated = ReadScenario().Replace(MapSha256, MapSha256.Substring(0, 63) + "e", StringComparison.Ordinal);
            FormatException ex = Assert.Throws<FormatException>(() => Load(mutated));
            Assert.Contains("mapFileSha256", ex.Message);
            Assert.Contains("does not match", ex.Message);
        }

        private static SimScenario Load()
        {
            return Load(ReadScenario());
        }

        private static SimScenario Load(string scenarioJson)
        {
            string mapPath = MarshMapPath();
            byte[] map = GoldenMapFiles.ReadWrmb(mapPath, GoldenMapV1MarshTests.MapBytes);
            return Load(scenarioJson, map);
        }

        private static SimScenario Load(string scenarioJson, byte[] map)
        {
            string root = GoldenMapFiles.FindRepoRoot();
            BalanceCatalog balance = BalanceCatalog.LoadFromDirectory(Path.Combine(root, "data", "balance"));
            return SimScenario.Load(
                scenarioJson,
                balance,
                map,
                GoldenMapV1MarshTests.MapFileName,
                ScenarioId);
        }

        private static string MarshMapPath()
        {
            return Path.Combine(
                GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapFileName);
        }

        private static long MarshMapLength()
        {
            return new FileInfo(MarshMapPath()).Length;
        }

        private static string ReadScenario()
        {
            return GoldenMapFiles.ReadRepoText(ScenarioRelative);
        }

        private static string ReplaceFirst(string text, string oldValue, string newValue)
        {
            int index = text.IndexOf(oldValue, StringComparison.Ordinal);
            Assert.True(index >= 0, "pattern not found: " + oldValue);
            return text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
        }
    }
}
