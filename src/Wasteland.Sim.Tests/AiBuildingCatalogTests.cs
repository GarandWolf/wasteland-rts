using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Random;
using Wasteland.Sim.Scenario;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiBuildingCatalogTests
    {
        [Fact]
        public void HoldsNoStaticParsedState()
        {
            FieldInfo[] fields = typeof(AiBuildingCatalog).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.Single(fields);
            Assert.Equal("Empty", fields[0].Name);
            Assert.Same(AiBuildingCatalog.Empty, fields[0].GetValue(null));
            Assert.Equal(0, AiBuildingCatalog.Empty.Count);

            string json = SyntheticBuilding("probe_depot", AiConstants.BuildingRoleSupplyDepot);
            AiBuildingCatalog first = AiBuildingCatalog.Parse(json);
            AiBuildingCatalog second = AiBuildingCatalog.Parse(json);
            Assert.NotSame(first, second);
            Assert.NotSame(AiBuildingCatalog.Empty, first);
            Assert.Equal(1, first.Count);
            Assert.Equal(first.Count, second.Count);
        }

        [Fact]
        public void FromDirectory_RealDocumentMissingOneRole_ThrowsAndNamesBuilding()
        {
            const string buildingId = "supply_depot";
            JsonNode root = JsonNode.Parse(BalanceLoad.ReadBuildingsJson())
                ?? throw new InvalidOperationException("buildings.json parsed to null.");
            JsonObject buildings = root["buildings"]?.AsObject()
                ?? throw new InvalidOperationException("buildings.json missing buildings.");
            JsonObject depot = buildings[buildingId]?.AsObject()
                ?? throw new InvalidOperationException("buildings.json missing " + buildingId + ".");
            Assert.True(depot.Remove("role"));
            string mutated = root.ToJsonString();
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiBalanceTables.FromDirectory(
                    BalanceLoad.DirectoryPath(),
                    BalanceLoad.ReadUnitsJson(),
                    mutated));
            Assert.Contains(buildingId, ex.Message);
            Assert.Contains("role", ex.Message);
        }

        [Fact]
        public void FromDirectory_NullBuildingsJson_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                AiBalanceTables.FromDirectory(
                    BalanceLoad.DirectoryPath(),
                    BalanceLoad.ReadUnitsJson(),
                    null!));
        }

        [Fact]
        public void FromDirectory_RealPack_ParsesBuildingRoles()
        {
            AiBalanceTables tables = BalanceLoad.Tables();
            Assert.True(tables.BuildingCatalog.TryGetBuildingRole("supply_depot", out string role));
            Assert.Equal(AiConstants.BuildingRoleSupplyDepot, role);
        }

        [Fact]
        public void Parse_ReadsAlreadyLoadedSyntheticDocument()
        {
            AiBuildingCatalog catalog = AiBuildingCatalog.Parse(
                SyntheticMany(
                    new[] { "wall_a", "depot_a", "yard_a" },
                    new[]
                    {
                        AiConstants.BuildingRoleWall,
                        AiConstants.BuildingRoleSupplyDepot,
                        AiConstants.BuildingRoleProduction
                    }));
            Assert.True(catalog.TryGetBuildingRole("depot_a", out string depot));
            Assert.Equal(AiConstants.BuildingRoleSupplyDepot, depot);
            Assert.True(catalog.TryGetBuildingRole("wall_a", out string wall));
            Assert.Equal(AiConstants.BuildingRoleWall, wall);
            Assert.True(catalog.TryGetBuildingRole("yard_a", out string yard));
            Assert.Equal(AiConstants.BuildingRoleProduction, yard);
            Assert.False(catalog.TryGetBuildingRole("missing", out string missing));
            Assert.Equal(string.Empty, missing);
        }

        [Fact]
        public void Parse_AcceptsClosedSevenRoleList()
        {
            Assert.Equal(
                new[]
                {
                    "hq",
                    "production",
                    "supply_depot",
                    "wall",
                    "fortification",
                    "resource",
                    "recruitment"
                },
                AiConstants.BuildingRoles);
            AiBuildingCatalog catalog = AiBuildingCatalog.Parse(
                SyntheticMany(AiConstants.BuildingRoles, AiConstants.BuildingRoles));
            for (int i = 0; i < AiConstants.BuildingRoles.Length; i++)
            {
                string role = AiConstants.BuildingRoles[i];
                Assert.True(catalog.TryGetBuildingRole(role, out string have));
                Assert.Equal(role, have);
            }
        }

        [Fact]
        public void Parse_RejectsMissingRole()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiBuildingCatalog.Parse(SyntheticBuilding("probe", null)));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("role", ex.Message);
        }

        [Fact]
        public void Parse_RejectsUnknownRole()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiBuildingCatalog.Parse(SyntheticBuilding("probe", "economy")));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("economy", ex.Message);
            Assert.Contains("unknown role", ex.Message);
        }

        [Fact]
        public void CanBuildDepot_UsesCatalogRoleNotAuthoredId()
        {
            AiScenarioSeatView seat = FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        0,
                        "homestead_compact",
                        "homestead_compact_settler",
                        1UL,
                        300,
                        FixtureAiScenarioSeats.DefaultRaisableUnits,
                        new[] { "supply_depot", "hq" },
                        System.Array.Empty<AiMvpAutoResearchItem>())
                },
                new AiMvpMatchRulesShape(false, "draw")).SeatAt(0);

            Assert.False(AiAttackDefend.CanBuildDepot(seat, AiBuildingCatalog.Empty));
            Assert.False(AiAttackDefend.CanBuildDepot(
                seat,
                AiBuildingCatalog.Parse(SyntheticBuilding("hq", AiConstants.BuildingRoleHq))));
            Assert.False(AiAttackDefend.CanBuildDepot(
                seat,
                AiBuildingCatalog.Parse(SyntheticBuilding("hq", AiConstants.BuildingRoleProduction))));
            Assert.True(AiAttackDefend.CanBuildDepot(
                seat,
                AiBuildingCatalog.Parse(SyntheticBuilding("supply_depot", AiConstants.BuildingRoleSupplyDepot))));
        }

        [Fact]
        public void RealGorgeSeats_CarryAuthoredIds_AndEmptyCatalogBlocksDepot()
        {
            SimScenario scenario = LoadMvp();
            ScenarioAiSeats seats = ScenarioAiSeats.From(scenario);
            for (int i = 0; i < seats.SeatCount; i++)
            {
                AiScenarioSeatView seat = seats.SeatAt(i);
                Assert.Equal(new[] { "supply_depot" }, seat.BuildableBuildings);
                Assert.False(AiAttackDefend.CanBuildDepot(seat, AiBuildingCatalog.Empty));
            }
        }

        [Fact]
        public void RealGorgePack_PlacesForwardDepotDeterministically()
        {
            byte[] first = PlaceGorgeForwardDepot();
            byte[] second = PlaceGorgeForwardDepot();
            Assert.Equal(first, second);
        }

        private static string SyntheticBuilding(string id, string? role)
        {
            string body = "\"category\":\"economy\"";
            if (role != null)
            {
                body += ",\"role\":\"" + role + "\"";
            }

            string json = "{\"buildings\":{\"" + id + "\":{" + body + "}}}";
            if (role != null)
            {
                Assert.Contains("\"category\":\"economy\",\"role\":\"" + role + "\"", json);
            }

            return json;
        }

        private static string SyntheticMany(string[] ids, string[] roles)
        {
            System.Text.StringBuilder json = new System.Text.StringBuilder();
            json.Append("{\"buildings\":{");
            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0)
                {
                    json.Append(',');
                }

                json.Append('"').Append(ids[i])
                    .Append("\":{\"category\":\"economy\",\"role\":\"")
                    .Append(roles[i]).Append("\"}");
            }

            json.Append("}}");
            return json.ToString();
        }

        private static byte[] PlaceGorgeForwardDepot()
        {
            AiBalanceTables tables = BalanceLoad.Tables();
            SimScenario scenario = LoadMvp();
            ScenarioAiSeats seats = ScenarioAiSeats.From(scenario);
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(AiSeamsTests.LoadGorgeV1Bytes());
            AiScenarioSeatView seat = seats.SeatAt(0);
            Assert.True(AiAttackDefend.CanBuildDepot(seat, tables.BuildingCatalog));
            Assert.True(map.TryGetTown(seat.HomeTownId, out AiTownRecord home));
            AiScenarioSeatView enemySeat = seats.SeatAt(1);
            Assert.True(map.TryGetTown(enemySeat.HomeTownId, out AiTownRecord enemyHome));

            RecordingAiCommandSink sink = new RecordingAiCommandSink();
            TownQuery towns = new TownQuery();
            towns.Add(home.Id, new AiTownReadback(AiConstants.DefaultAiFactionA, false, AiConstants.DefaultAiFactionA, 1, 0, 0));
            towns.Add(enemyHome.Id, new AiTownReadback(AiConstants.DefaultAiFactionB, false, AiConstants.DefaultAiFactionB, 1, 0, 0));
            AiAttackDefend planner = new AiAttackDefend(sink, towns, sink);
            AiFactionState faction = NewFaction(AiConstants.DefaultAiFactionA);
            faction.HqNodeId = home.RoadNodeId;
            faction.HomeTownId = home.Id;
            faction.HomeTerritoryId = home.TerritoryId;
            faction.WorkUnitsPerTick = 50000;

            AiAttackTownInfo[] townRows = new AiAttackTownInfo[map.TownCount];
            for (int i = 0; i < map.TownCount; i++)
            {
                AiTownRecord town = map.TownAt(i);
                ulong territoryKey = 0UL;
                if (map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory))
                {
                    territoryKey = territory.StableKey;
                }

                townRows[i] = new AiAttackTownInfo(
                    town.Id,
                    town.TerritoryId,
                    town.RoadNodeId,
                    town.XCm,
                    town.YCm,
                    town.Id == home.Id || town.Id == enemyHome.Id,
                    territoryKey);
            }

            string lineId = AiMvpAttackTests.FirstRoleOrTarget(
                seat,
                tables.UnitCatalog,
                AiConstants.RoleLineInfantry,
                null);
            string engineerId = AiMvpAttackTests.FirstRoleOrTarget(
                seat,
                tables.UnitCatalog,
                AiConstants.RoleEngineer,
                null);
            Assert.False(string.IsNullOrEmpty(lineId));
            Assert.False(string.IsNullOrEmpty(engineerId));
            AiBattalionPin[] battalions =
            {
                new AiBattalionPin(5, home.XCm, home.YCm, home.RoadNodeId, lineId, false),
                new AiBattalionPin(7, home.XCm, home.YCm, home.RoadNodeId, engineerId, false)
            };

            AiPersonalityProfile personality = tables.GetPersonalityOrGeneric(seat.AiPersonality ?? string.Empty);
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Tier2ResearchTicks = tables.Tier2ResearchTicks;
            view.Manpower = seat.StartingManpower;
            view.Refresh(seat, 0);
            faction.WorkUnitsThisTick = 0;
            sink.Tick = 0;
            planner.Tick(
                0,
                faction,
                seat,
                townRows,
                townRows.Length,
                battalions,
                battalions.Length,
                new[] { home.RoadNodeId },
                1,
                map.Roads,
                tables.GetDifficulty(AiConstants.DifficultyNormal).AttackStrengthRatioPpt,
                1,
                seats.NeutralPrizeTerritoryKey,
                personality.PrizeTargetBonusPpt,
                view,
                tables.UnitCatalog,
                tables.BuildingCatalog,
                personality.LineUnitsPerAntiArmour,
                personality.MinGarrisonPerTown);

            bool built = false;
            for (int i = 0; i < sink.Log.Count; i++)
            {
                if (sink.Log[i].Kind == AiIssuedKind.BuildDepot)
                {
                    built = true;
                    break;
                }
            }

            Assert.True(built);
            return IssuedBytes(sink.Log);
        }

        private static byte[] IssuedBytes(IReadOnlyList<AiIssuedCommand> log)
        {
            List<byte> buffer = new List<byte>();
            for (int i = 0; i < log.Count; i++)
            {
                AiIssuedCommand cmd = log[i];
                WriteInt32(buffer, cmd.Tick);
                WriteInt32(buffer, cmd.FactionId);
                WriteInt32(buffer, (int)cmd.Kind);
                WriteInt32(buffer, cmd.Arg0);
                WriteInt32(buffer, cmd.Arg1);
                WriteInt32(buffer, cmd.Arg2);
                WriteInt32(buffer, cmd.Arg3);
                byte[] unitBytes = Encoding.UTF8.GetBytes(cmd.UnitId);
                WriteInt32(buffer, unitBytes.Length);
                for (int b = 0; b < unitBytes.Length; b++)
                {
                    buffer.Add(unitBytes[b]);
                }
            }

            return buffer.ToArray();
        }

        private static void WriteInt32(List<byte> buffer, int value)
        {
            uint bits = (uint)value;
            buffer.Add((byte)bits);
            buffer.Add((byte)(bits >> 8));
            buffer.Add((byte)(bits >> 16));
            buffer.Add((byte)(bits >> 24));
        }

        private static AiFactionState NewFaction(int factionId)
        {
            return new AiFactionState(
                factionId,
                AiConstants.PersonalityWarlord,
                AiConstants.DifficultyNormal,
                0,
                40,
                4,
                1,
                60,
                8,
                1,
                0,
                50000,
                4,
                4,
                AiPosture.Attack,
                new SimRandom(1UL),
                new SimRandom(3UL),
                new SimRandom(5UL),
                new SimRandom(7UL));
        }

        private sealed class TownQuery : IAiTownQuery
        {
            private readonly Dictionary<int, AiTownReadback> _towns = new Dictionary<int, AiTownReadback>();

            public void Add(int townId, AiTownReadback readback)
            {
                _towns[townId] = readback;
            }

            public bool TryGet(int townId, out AiTownReadback readback)
            {
                return _towns.TryGetValue(townId, out readback);
            }
        }

        private static SimScenario LoadMvp()
        {
            BalanceLoad.SharedPack pack = BalanceLoad.Shared();
            byte[] map = System.IO.File.ReadAllBytes(
                System.IO.Path.Combine(
                    GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName),
                    GoldenMapV1MarshTests.MapFileName));
            return SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                pack.Scenario,
                map,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");
        }
    }
}
