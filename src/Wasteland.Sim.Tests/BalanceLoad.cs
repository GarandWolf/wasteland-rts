using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// One read of <c>units.json</c> shared by <see cref="BalanceCatalog"/>
    /// and <see cref="AiUnitCatalog"/>. No static cache.
    /// </summary>
    internal static class BalanceLoad
    {
        public static string DirectoryPath()
        {
            return Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance");
        }

        public static string ReadUnitsJson()
        {
            return File.ReadAllText(Path.Combine(DirectoryPath(), "units.json"));
        }

        public static string ReadBuildingsJson()
        {
            return File.ReadAllText(Path.Combine(DirectoryPath(), "buildings.json"));
        }

        /// <summary>
        /// Production pack load. Parses the real <c>buildings.json</c>.
        /// Throws until the Designer's <c>role</c> field lands.
        /// </summary>
        public static AiBalanceTables Tables()
        {
            return Tables(ReadUnitsJson(), ReadBuildingsJson());
        }

        public static AiBalanceTables Tables(string unitsJson)
        {
            return Tables(unitsJson, ReadBuildingsJson());
        }

        public static AiBalanceTables Tables(string unitsJson, string buildingsJson)
        {
            return AiBalanceTables.FromDirectory(DirectoryPath(), unitsJson, buildingsJson);
        }

        /// <summary>
        /// Personalities, units, rules, and economy without the real building
        /// pack. Used by decision tests until the Designer role PR lands.
        /// </summary>
        public static AiBalanceTables DecisionTables()
        {
            return Tables(ReadUnitsJson(), DummyBuildingsWithoutDepotRole);
        }

        internal const string DummyBuildingsWithoutDepotRole =
            "{\"buildings\":{\"probe_hq\":{\"category\":\"economy\",\"role\":\"production\"}}}";

        internal const string BuildingRolePendingSkip = "building role pending Designer data PR #22";

        public static BalanceCatalog ScenarioCatalog(string unitsJson)
        {
            string dir = DirectoryPath();
            return BalanceCatalog.Parse(
                unitsJson,
                File.ReadAllText(Path.Combine(dir, "buildings.json")),
                File.ReadAllText(Path.Combine(dir, "rules.json")),
                File.ReadAllText(Path.Combine(dir, "economy.json")),
                File.ReadAllText(Path.Combine(dir, "ai", "ai_personalities.json")));
        }

        public static SharedPack Shared()
        {
            string unitsJson = ReadUnitsJson();
            return new SharedPack(unitsJson, ScenarioCatalog(unitsJson), DecisionTables());
        }

        internal readonly struct SharedPack
        {
            public readonly string UnitsJson;
            public readonly BalanceCatalog Scenario;
            public readonly AiBalanceTables Tables;

            public SharedPack(string unitsJson, BalanceCatalog scenario, AiBalanceTables tables)
            {
                UnitsJson = unitsJson;
                Scenario = scenario;
                Tables = tables;
            }
        }
    }
}
