using System;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class PopCapBalanceTests
    {
        [Fact]
        public void RealEconomy_PopCap_IsBasePlusHqBonusUnderHardCap()
        {
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(BalanceLoad.DirectoryPath());
            Assert.Equal(0, catalog.PopCapBase);
            Assert.Equal(30, catalog.PopCapHqBonus);
            Assert.Equal(100, catalog.PopCapHardCap);
            Assert.Equal(30, catalog.StartingSeatPopCap);
        }

        [Fact]
        public void HostSeat_UsesInMemoryPopCap_OnStartAndReplay()
        {
            string economy = ReplacePopCap(
                ReadEconomy(),
                "\"popCap\": { \"base\": 3, \"hqBonus\": 12, \"hardCap\": 100 }");
            BalanceCatalog catalog = Parse(economy);
            Assert.Equal(3, catalog.PopCapBase);
            Assert.Equal(12, catalog.PopCapHqBonus);
            Assert.Equal(15, catalog.StartingSeatPopCap);

            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance().WithScenarioCatalog(catalog);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions =
            {
                AiConstants.DefaultAiFactionA,
                AiConstants.DefaultAiFactionB
            };
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(1UL),
                map,
                factions,
                AiConstants.DifficultyNormal,
                tables);
            AssertPopCap(host, AiConstants.DefaultAiFactionA, 15);
            AssertPopCap(host, AiConstants.DefaultAiFactionB, 15);

            byte[] blob = host.Ai.Save();
            AiMatchHost replay = AiMatchHost.ReplayTo(
                MatchConfig.Default(1UL),
                map,
                factions,
                AiConstants.DifficultyNormal,
                tables,
                new SimCommand[0],
                blob,
                0);
            AssertPopCap(replay, AiConstants.DefaultAiFactionA, 15);
            AssertPopCap(replay, AiConstants.DefaultAiFactionB, 15);
            host.Shutdown();
            replay.Shutdown();
        }

        [Fact]
        public void PopCap_HardCap_ClampsBasePlusHqBonus()
        {
            string economy = ReplacePopCap(
                ReadEconomy(),
                "\"popCap\": { \"base\": 40, \"hqBonus\": 80, \"hardCap\": 50 }");
            BalanceCatalog catalog = Parse(economy);
            Assert.Equal(40, catalog.PopCapBase);
            Assert.Equal(80, catalog.PopCapHqBonus);
            Assert.Equal(50, catalog.PopCapHardCap);
            Assert.Equal(50, catalog.StartingSeatPopCap);
        }

        [Fact]
        public void PopCap_MissingHqBonus_IsRejected()
        {
            string economy = ReplacePopCap(
                ReadEconomy(),
                "\"popCap\": { \"base\": 0, \"hardCap\": 100 }");
            FormatException ex = Assert.Throws<FormatException>(() => Parse(economy));
            Assert.Contains("hqBonus", ex.Message);
            Assert.Contains("missing required key", ex.Message);
        }

        [Fact]
        public void PopCap_NegativeField_IsRejected()
        {
            string economy = ReplacePopCap(
                ReadEconomy(),
                "\"popCap\": { \"base\": 0, \"hqBonus\": -1, \"hardCap\": 100 }");
            FormatException ex = Assert.Throws<FormatException>(() => Parse(economy));
            Assert.Contains("hqBonus", ex.Message);
            Assert.Contains("out of range", ex.Message);
        }

        private static void AssertPopCap(AiMatchHost host, int factionId, int popCap)
        {
            Assert.True(host.World.TryGetSeat(factionId, out SeatView seat));
            Assert.Equal(popCap, seat.PopCap);
        }

        private static string ReadEconomy()
        {
            return GoldenMapFiles.ReadRepoText("data/balance/economy.json").Replace("\r\n", "\n");
        }

        private static string ReplacePopCap(string economy, string replacement)
        {
            const string startMark = "\"popCap\": {";
            int start = economy.IndexOf(startMark, StringComparison.Ordinal);
            Assert.True(start >= 0);
            int end = economy.IndexOf('}', start);
            Assert.True(end > start);
            return economy.Substring(0, start) + replacement + economy.Substring(end + 1);
        }

        private static BalanceCatalog Parse(string economyJson)
        {
            string dir = BalanceLoad.DirectoryPath();
            return BalanceCatalog.Parse(
                File.ReadAllText(Path.Combine(dir, "units.json")),
                File.ReadAllText(Path.Combine(dir, "buildings.json")),
                File.ReadAllText(Path.Combine(dir, "rules.json")),
                economyJson,
                File.ReadAllText(Path.Combine(dir, "ai", "ai_personalities.json")));
        }
    }
}
