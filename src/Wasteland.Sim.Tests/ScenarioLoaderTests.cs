using System;
using System.IO;
using System.Text;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class ScenarioLoaderTests
    {
        private const string ScenarioId = "columbia_gorge_100mi_mvp";
        private const string ScenarioRelative = "data/balance/scenarios/columbia_gorge_100mi_mvp.json";
        private const string MapSha256 = "76a801a37862ca5858f455ba44a73d48434791db07bb8558eb4086c6afd2107f";
        private const string AbsentTerritoryKey = "0x1000000000000001";

        [Fact]
        public void LoadsGorgeMvp_AgainstV1MarshMap()
        {
            byte[] map = ReadMarshMap();
            SimScenario scenario = Load(ReadScenario(), map);
            SimScenario again = Load(ReadScenario(), map);

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

            ScenarioSeat seat0 = scenario.SeatAt(0);
            ScenarioSeat seat1 = scenario.SeatAt(1);
            ulong home0 = seat0.HomeTerritoryKey;
            ulong home1 = seat1.HomeTerritoryKey;
            ulong prize = scenario.MatchRules.NeutralPrizeTerritoryKey;
            Assert.NotEqual(0UL, home0);
            Assert.NotEqual(0UL, home1);
            Assert.NotEqual(home0, home1);
            Assert.NotEqual(prize, home0);
            Assert.NotEqual(prize, home1);
            Assert.True(TryFindMapTerritory(map, home0, out int home0Id));
            Assert.Equal(home0Id, seat0.HomeTerritoryId);
            Assert.True(TryFindMapTerritory(map, home1, out int home1Id));
            Assert.Equal(home1Id, seat1.HomeTerritoryId);
            Assert.True(TryFindMapTerritory(map, prize, out int prizeId));
            Assert.True(scenario.TryGetTerritory(prize, out ScenarioDerivedTerritory prizeTerritory));
            Assert.Equal(prizeId, prizeTerritory.MapTerritoryId);
            Assert.True(scenario.IsNeutralTerritory(prize));
            Assert.False(scenario.IsNeutralTerritory(home0));
            Assert.False(scenario.IsNeutralTerritory(home1));
            Assert.Equal(CountKeyedTerritories(map) - CountDistinctHoldings(scenario), scenario.NeutralTerritoryKeys.Length);

            Assert.Equal(0, seat0.Slot);
            Assert.Equal("remnant_command", seat0.Faction);
            Assert.Equal(ScenarioController.Player, seat0.Controller);
            Assert.Null(seat0.AiPersonality);
            Assert.False(seat0.IsAi);
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
            Assert.Equal(home0, seat0.StartingHoldings[0].TerritoryKey);

            Assert.Equal(1, seat1.Slot);
            Assert.Equal("homestead_compact", seat1.Faction);
            Assert.Equal(ScenarioController.Ai, seat1.Controller);
            Assert.True(seat1.IsAi);
            Assert.Equal("homestead_compact_settler", seat1.AiPersonality);
            Assert.True(seat1.HomeTownId > 0);
            Assert.Equal(300, seat1.StartingManpower);
            Assert.Empty(seat1.AutoResearch);
            Assert.Equal(new[] { "supply_depot" }, seat1.BuildableBuildings);
            Assert.True(scenario.TryGetSeatByFaction("homestead_compact", out ScenarioSeat byFaction));
            Assert.Equal(home1, byFaction.HomeTerritoryKey);
            Assert.NotEmpty(seat1.StartingHoldings);
            Assert.Equal(home1, seat1.StartingHoldings[0].TerritoryKey);
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
            string authored = FormatKey(Load().SeatAt(0).HomeTerritoryKey);
            string lower = "0x" + authored.Substring(2).ToLowerInvariant();
            Assert.NotEqual(authored, lower);
            string mutated = ReplaceFirst(
                ReadScenario(),
                "\"homeTerritoryKey\": \"" + authored + "\"",
                "\"homeTerritoryKey\": \"" + lower + "\"");
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
            byte[] map = ReadMarshMap();
            Assert.False(TryFindMapTerritory(map, 0x1000000000000001UL, out int unused));
            Assert.Equal(0, unused);
            string authored = FormatKey(Load(ReadScenario(), map).SeatAt(0).HomeTerritoryKey);
            string mutated = ReplaceFirst(
                ReadScenario(),
                "\"homeTerritoryKey\": \"" + authored + "\"",
                "\"homeTerritoryKey\": \"" + AbsentTerritoryKey + "\"");
            FormatException ex = Assert.Throws<FormatException>(() => Load(mutated, map));
            Assert.Contains("homeTerritoryKey", ex.Message);
            Assert.Contains("unknown territory", ex.Message);
            Assert.Contains(AbsentTerritoryKey, ex.Message);
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
            return Load(scenarioJson, ReadMarshMap());
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

        private static byte[] ReadMarshMap()
        {
            return GoldenMapFiles.ReadWrmb(MarshMapPath(), GoldenMapV1MarshTests.MapBytes);
        }

        private static bool TryFindMapTerritory(byte[] map, ulong key, out int territoryId)
        {
            Territory[] rows = Wrmb.ReadTerritories(WrmbFile.Parse(map));
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].StableKey == key)
                {
                    territoryId = rows[i].Id;
                    return true;
                }
            }

            territoryId = 0;
            return false;
        }

        private static int CountKeyedTerritories(byte[] map)
        {
            Territory[] rows = Wrmb.ReadTerritories(WrmbFile.Parse(map));
            int count = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].StableKey != 0UL)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountDistinctHoldings(SimScenario scenario)
        {
            int count = 0;
            ulong[] seen = new ulong[scenario.SeatCount * 4];
            for (int s = 0; s < scenario.SeatCount; s++)
            {
                ScenarioHolding[] holdings = scenario.SeatAt(s).StartingHoldings;
                for (int h = 0; h < holdings.Length; h++)
                {
                    ulong key = holdings[h].TerritoryKey;
                    bool already = false;
                    for (int i = 0; i < count; i++)
                    {
                        if (seen[i] == key)
                        {
                            already = true;
                        }
                    }

                    if (!already)
                    {
                        seen[count] = key;
                        count++;
                    }
                }
            }

            return count;
        }

        private static string FormatKey(ulong key)
        {
            return "0x" + key.ToString("X16");
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
