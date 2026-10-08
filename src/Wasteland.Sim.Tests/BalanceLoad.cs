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

        public static AiBalanceTables Tables()
        {
            string unitsJson = ReadUnitsJson();
            return Tables(unitsJson);
        }

        public static AiBalanceTables Tables(string unitsJson)
        {
            return AiBalanceTables.FromDirectory(DirectoryPath(), unitsJson);
        }

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
            return new SharedPack(unitsJson, ScenarioCatalog(unitsJson), Tables(unitsJson));
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
