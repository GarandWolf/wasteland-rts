using System;
using System.Reflection;
using Wasteland.Sim.Ai;
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

            string unitsJson = BalanceLoad.ReadUnitsJson();
            AiUnitCatalog first = AiUnitCatalog.Parse(unitsJson);
            AiUnitCatalog second = AiUnitCatalog.Parse(unitsJson);
            Assert.NotSame(first, second);
            Assert.NotSame(AiUnitCatalog.Empty, first);
            Assert.True(first.Count > 0);
            Assert.Equal(first.Count, second.Count);

            AiBalanceTables withoutUnits = AiBalanceTables.FromDirectory(
                BalanceLoad.DirectoryPath(),
                null,
                BalanceLoad.DummyBuildingsWithoutDepotRole);
            Assert.Same(AiUnitCatalog.Empty, withoutUnits.UnitCatalog);
        }

        [Fact]
        public void Parse_SharesTheAlreadyLoadedUnitsDocument()
        {
            BalanceLoad.SharedPack pack = BalanceLoad.Shared();
            AiUnitCatalog catalog = AiUnitCatalog.Parse(pack.UnitsJson);
            Assert.True(pack.Tables.UnitCatalog.TryGetRole("compact_rifle_battalion", out string role));
            Assert.Equal(AiConstants.RoleLineInfantry, role);
            Assert.True(catalog.TryGetRole("compact_rifle_battalion", out string again));
            Assert.Equal(role, again);
            Assert.True(catalog.TryGetTargetClass("compact_at_battalion", out string targetClass));
            Assert.Equal(AiConstants.TargetClassSoft, targetClass);
        }

        [Fact]
        public void Parse_RejectsUnknownRole()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.Parse(SyntheticUnit("probe", "rifle", "soft")));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("rifle", ex.Message);
            Assert.Contains("unknown role", ex.Message);
        }

        [Fact]
        public void Parse_RejectsUnknownTargetClass()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.Parse(SyntheticUnit("probe", AiConstants.RoleLineInfantry, "hard")));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("hard", ex.Message);
            Assert.Contains("unknown targetClass", ex.Message);
        }

        [Fact]
        public void Parse_RejectsMissingRole()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.Parse(SyntheticUnit("probe", null, "soft")));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("role", ex.Message);
        }

        [Fact]
        public void Parse_RejectsMissingTargetClass()
        {
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.Parse(SyntheticUnit("probe", AiConstants.RoleLineInfantry, null)));
            Assert.Contains("probe", ex.Message);
            Assert.Contains("targetClass", ex.Message);
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

        private static string SyntheticUnit(string id, string? role, string? targetClass)
        {
            string roleField = role == null
                ? string.Empty
                : ",\"role\":\"" + role + "\"";
            string targetField = targetClass == null
                ? string.Empty
                : ",\"targetClass\":\"" + targetClass + "\"";
            return "{\"units\":{\"" + id + "\":{"
                + "\"techTier\":1,\"cost\":{\"manpower\":10}"
                + roleField
                + targetField
                + "}}}";
        }
    }
}
