using System;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiCaptureTests
    {
        public const int ExpectedHamletTicks = 600;
        public const int ExpectedVillageTicks = 1200;
        public const int ExpectedTownTicks = 2400;
        public const int ExpectedCityTicks = 3600;

        [Fact]
        public void CaptureTicks_AreReadFromRulesTimerTicksByTier()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.True(tables.HasCaptureTable);
            Assert.Equal(ExpectedHamletTicks, tables.CaptureTicksForClass(0));
            Assert.Equal(ExpectedVillageTicks, tables.CaptureTicksForClass(1));
            Assert.Equal(ExpectedTownTicks, tables.CaptureTicksForClass(2));
            Assert.Equal(ExpectedCityTicks, tables.CaptureTicksForClass(3));
            FormatException ex = Assert.Throws<FormatException>(() => tables.CaptureTicksForClass(4));
            Assert.Contains("populationClass 4", ex.Message);
        }

        [Fact]
        public void CaptureLoader_FailsWithRuleIdWhenTierMissing()
        {
            string rules = GoldenMapFiles.ReadRepoText("data/balance/rules.json")
                .Replace("      \"hamlet\": 600,\n", string.Empty);
            FormatException ex = Assert.Throws<FormatException>(() =>
            {
                AiBalanceTables.ParseCaptureRules(rules, out _, out _, out _, out _);
            });
            Assert.Contains(AiBalanceTables.CaptureRuleHamlet, ex.Message);
        }

        [Fact]
        public void CaptureLoader_FailsWithRuleIdWhenTierIsNotInteger()
        {
            string rules = GoldenMapFiles.ReadRepoText("data/balance/rules.json")
                .Replace(
                    "\"timerTicksByTier\": {\n      \"hamlet\": 600",
                    "\"timerTicksByTier\": {\n      \"hamlet\": \"600\"");
            FormatException ex = Assert.Throws<FormatException>(() =>
            {
                AiBalanceTables.ParseCaptureRules(rules, out _, out _, out _, out _);
            });
            Assert.Contains(AiBalanceTables.CaptureRuleHamlet, ex.Message);
        }

        [Fact]
        public void GorgeTowns_UsePopulationClassZeroThroughTwo()
        {
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(AiSeamsTests.LoadGorgeV1Bytes());
            int[] counts = new int[4];
            int maxClass = 0;
            for (int i = 0; i < map.TownCount; i++)
            {
                int populationClass = map.TownAt(i).PopulationClass;
                Assert.InRange(populationClass, 0, 3);
                counts[populationClass]++;
                if (populationClass > maxClass)
                {
                    maxClass = populationClass;
                }
            }

            Assert.Equal(2, maxClass);
            Assert.True(counts[0] > 0);
            Assert.True(counts[1] > 0);
            Assert.True(counts[2] > 0);
            Assert.Equal(0, counts[3]);
        }

        [Fact]
        public void MixedOccupancy_PausesCaptureTimer()
        {
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                balance);

            int townIndex = FindNeutralTown(host);
            AiTownRecord town = host.Ai.Map.TownAt(townIndex);
            int delay = host.World.CommandDelayTicks;

            host.World.Submit(SimCommand.SpawnBattalion(
                host.Tick,
                AiConstants.DefaultAiFactionA,
                9000001,
                town.XCm,
                town.YCm,
                0));
            host.Run(delay + 8);

            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            int heldBeforeContest = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.True(heldBeforeContest > 0);
            Assert.True(heldBeforeContest < balance.CaptureTicksForClass(town.PopulationClass));

            host.World.Submit(SimCommand.SpawnBattalion(
                host.Tick,
                AiConstants.DefaultAiFactionB,
                9000002,
                town.XCm,
                town.YCm,
                0));
            host.Run(delay + 1);

            int paused = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            host.Run(80);

            Assert.Equal(paused, host.Ai.Owned.CaptureHeldTicksAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));

            host.Shutdown();
        }

        private static int FindNeutralTown(AiMatchHost host)
        {
            AiMapSnapshot map = host.Ai.Map;
            Assert.True(map.TryGetTown(map.GoldendaleTownId, out AiTownRecord goldendale));
            Assert.True(map.TryGetTown(map.HoodRiverTownId, out AiTownRecord hoodRiver));
            long minSepSq = 3L * AiConstants.MileCm * AiConstants.MileCm;
            for (int i = 0; i < map.TownCount; i++)
            {
                if (host.Ai.Owned.TownOwnerAt(i) != 0)
                {
                    continue;
                }

                AiTownRecord town = map.TownAt(i);
                if (DistSq(town, goldendale) < minSepSq || DistSq(town, hoodRiver) < minSepSq)
                {
                    continue;
                }

                return i;
            }

            throw new InvalidOperationException("No far unowned town on Gorge marsh.");
        }

        private static long DistSq(AiTownRecord a, AiTownRecord b)
        {
            long dx = (long)a.XCm - b.XCm;
            long dy = (long)a.YCm - b.YCm;
            return (dx * dx) + (dy * dy);
        }
    }
}
