using System;
using System.Reflection;
using System.Text.Json.Nodes;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiUnitCatalogTests
    {
        [Fact]
        public void HoldsNoStaticParsedState()
        {
            FieldInfo[] fields = typeof(AiUnitCatalog).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.Single(fields);
            Assert.Equal("Empty", fields[0].Name);
            Assert.Same(AiUnitCatalog.Empty, fields[0].GetValue(null));
            Assert.Equal(0, AiUnitCatalog.Empty.Count);

            AiUnitCatalog first = AiUnitCatalog.FromRows(
                new AiUnitCatalogRow("probe_line", 1, AiConstants.RoleLineInfantry, AiConstants.TargetClassSoft, 10));
            AiUnitCatalog second = AiUnitCatalog.FromRows(
                new AiUnitCatalogRow("probe_line", 1, AiConstants.RoleLineInfantry, AiConstants.TargetClassSoft, 10));
            Assert.NotSame(first, second);
            Assert.NotSame(AiUnitCatalog.Empty, first);
            Assert.Equal(1, first.Count);
            Assert.Equal(first.Count, second.Count);

            AiBalanceTables dummyBuildings = AiBalanceTables.FromDirectory(
                BalanceLoad.DirectoryPath(),
                null,
                BalanceLoad.DummyBuildingsWithoutDepotRole);
            Assert.IsType<BalanceAiUnitCatalog>(dummyBuildings.UnitCatalog);
            Assert.True(dummyBuildings.UnitCatalog.TryGetRole("compact_rifle_battalion", out string role));
            Assert.Equal(AiConstants.RoleLineInfantry, role);
        }

        [Fact]
        public void FromDirectory_RealPack_ReadsCatalogRoles()
        {
            AiBalanceTables tables = BalanceLoad.Tables();
            Assert.IsType<BalanceAiUnitCatalog>(tables.UnitCatalog);
            Assert.IsType<BalanceAiBuildingCatalog>(tables.BuildingCatalog);
            Assert.True(tables.UnitCatalog.TryGetRole("compact_rifle_battalion", out string role));
            Assert.Equal(AiConstants.RoleLineInfantry, role);
            Assert.True(tables.UnitCatalog.TryGetTargetClass("compact_at_battalion", out string targetClass));
            Assert.Equal(AiConstants.TargetClassSoft, targetClass);
            Assert.True(tables.UnitCatalog.TryGetTier("compact_rifle_battalion", out int tier));
            Assert.Equal(1, tier);
            Assert.True(tables.UnitCatalog.TryGetManpowerCost("compact_rifle_battalion", out int cost));
            Assert.True(cost > 0);
            Assert.True(tables.UnitCatalog.TryGetMovementClass("compact_rifle_battalion", out MovementClass rifleMove));
            Assert.Equal(MovementClass.Wheeled, rifleMove);
            Assert.True(tables.UnitCatalog.TryGetMovementClass("compact_at_battalion", out MovementClass atMove));
            Assert.Equal(MovementClass.Foot, atMove);
            Assert.True(tables.UnitCatalog.TryGetMovementClass("compact_militia", out MovementClass militiaMove));
            Assert.Equal(MovementClass.Foot, militiaMove);
            Assert.True(tables.BuildingCatalog.TryGetBuildingRole("supply_depot", out string buildingRole));
            Assert.Equal(AiConstants.BuildingRoleSupplyDepot, buildingRole);
        }

        [Fact]
        public void FromDirectory_UnknownUnitRole_ThrowsAllowlist()
        {
            JsonNode root = JsonNode.Parse(BalanceLoad.ReadUnitsJson())
                ?? throw new InvalidOperationException("units.json parsed to null.");
            JsonObject units = root["units"]?.AsObject()
                ?? throw new InvalidOperationException("units.json missing units.");
            JsonObject rifle = units["compact_rifle_battalion"]?.AsObject()
                ?? throw new InvalidOperationException("units.json missing compact_rifle_battalion.");
            rifle["role"] = "rifle";
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiBalanceTables.FromDirectory(
                    BalanceLoad.DirectoryPath(),
                    root.ToJsonString(),
                    BalanceLoad.ReadBuildingsJson()));
            Assert.Contains("compact_rifle_battalion", ex.Message);
            Assert.Contains("rifle", ex.Message);
            Assert.Contains("unknown role", ex.Message);
        }

        [Fact]
        public void FromRows_RejectsUnknownRoleAndDoesNotDefaultToRifle()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.FromRows(
                    new AiUnitCatalogRow("probe", 1, "rifle", AiConstants.TargetClassSoft, 10)));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("rifle", ex.Message);
        }

        [Fact]
        public void FromRows_RejectsUnknownTargetClass()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.FromRows(
                    new AiUnitCatalogRow("probe", 1, AiConstants.RoleLineInfantry, "hard", 10)));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("hard", ex.Message);
        }
    }
}
