using System;
using System.Reflection;
using Wasteland.Sim.Ai;
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
        public void FromDirectory_RealPack_ThrowsOnMissingBuildingRole()
        {
            FormatException ex = Assert.Throws<FormatException>(() => BalanceLoad.Tables());
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

        [Fact(Skip = BalanceLoad.BuildingRolePendingSkip)]
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
