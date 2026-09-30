using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiVsAiGorgeTests
    {
        private const ulong MatchSeed = 0xC01055A1UL;

        [Fact]
        public void TwoAiFactions_OnGorgeV0_SameSeed_SameHashStream()
        {
            List<ulong> first = Run(MatchSeed, out List<string> logsA, out int theDallesTownId);
            List<ulong> second = Run(MatchSeed, out List<string> logsB, out int theDallesTownIdB);

            Assert.Equal(14, theDallesTownId);
            Assert.Equal(theDallesTownId, theDallesTownIdB);
            Assert.Equal(first, second);
            Assert.Equal(logsA, logsB);
            Assert.True(first.Count >= 1 + (AiConstants.M0DeterminismTicks / AiConstants.HashPeriodTicks));

            AssertRequiredHeadlessLogs(logsA);
            for (int i = 0; i < logsA.Count; i++)
            {
                Assert.Contains("manpower=0", logsA[i]);
                Assert.Contains("reserve=0", logsA[i]);
                Assert.Contains("techSavings=0", logsA[i]);
                Assert.Contains("battalions=0", logsA[i]);
                Assert.Contains("theDallesTown=14", logsA[i]);
                Assert.Contains("theDallesOwner=0", logsA[i]);
            }
        }

        [Fact]
        public void DifferentDifficultyId_ChangesWorldHash()
        {
            AiBalanceTables baseline = AiBalanceTablesTests.LoadBalance();
            AiMatchHost normal = Start(MatchSeed, AiConstants.DifficultyNormal, baseline);
            normal.Run(40);
            ulong normalHash = normal.HashWorld();
            normal.Shutdown();

            AiMatchHost brutal = Start(MatchSeed, AiConstants.DifficultyBrutal, baseline);
            brutal.Run(40);
            Assert.NotEqual(normalHash, brutal.HashWorld());
            Assert.Equal(100, brutal.Ai.Difficulty.EconomyBonusPpt);
            brutal.Shutdown();
        }

        [Fact]
        public void DifferentSeed_ProducesDifferentHashStream()
        {
            List<ulong> first = Run(MatchSeed, out _, out _);
            List<ulong> other = Run(MatchSeed ^ 1UL, out _, out _);
            Assert.NotEqual(first, other);
        }

        private static List<ulong> Run(ulong seed, out List<string> logs, out int theDallesTownId)
        {
            AiMatchHost host = Start(seed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(AiConstants.M0DeterminismTicks);
            logs = new List<string>(host.HeadlessLogs);
            theDallesTownId = host.Ai.Map.TheDallesTownId;
            List<ulong> hashes = new List<ulong>(host.Hashes.Count);
            for (int i = 0; i < host.Hashes.Count; i++)
            {
                hashes.Add(host.Hashes[i].Hash);
            }

            host.Shutdown();
            return hashes;
        }

        private static AiMatchHost Start(ulong seed, string difficultyId, AiBalanceTables balance)
        {
            return AiMatchHost.Start(
                MatchConfig.Default(seed),
                AiSeamsTests.LoadGorgeBytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                difficultyId,
                balance);
        }

        private static void AssertRequiredHeadlessLogs(List<string> logs)
        {
            HashSet<int> ticks = new HashSet<int>();
            for (int i = 0; i < logs.Count; i++)
            {
                string line = logs[i];
                int eq = line.IndexOf('=');
                int space = line.IndexOf(' ');
                Assert.True(eq > 0 && space > eq);
                int tick = int.Parse(line.Substring(eq + 1, space - eq - 1));
                ticks.Add(tick);
            }

            for (int tick = AiConstants.HeadlessLogFromTick;
                 tick <= AiConstants.HeadlessLogThroughTickMinimum;
                 tick += AiConstants.HeadlessLogPeriodTicks)
            {
                Assert.True(ticks.Contains(tick), "missing headless log at tick " + tick);
            }
        }
    }
}
