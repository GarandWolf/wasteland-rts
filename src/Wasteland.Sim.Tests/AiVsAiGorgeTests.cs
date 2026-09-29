using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiVsAiGorgeTests
    {
        private const ulong MatchSeed = 0xC01055A1UL;

        [Fact]
        public void TwoAiFactions_OnGorgeV1_SameSeed_SameHashStream()
        {
            List<ulong> first = Run(MatchSeed, out List<string> logsA, out int theDallesTownId, out AiMatchHost hostA);
            List<ulong> second = Run(MatchSeed, out List<string> logsB, out int theDallesTownIdB, out AiMatchHost hostB);

            Assert.Equal(14, theDallesTownId);
            Assert.Equal(theDallesTownId, theDallesTownIdB);
            Assert.Equal(first, second);
            Assert.Equal(logsA, logsB);
            Assert.True(first.Count >= 1 + (AiConstants.M1DeterminismTicks / AiConstants.HashPeriodTicks));

            AssertRequiredHeadlessLogs(logsA);
            Assert.Contains("theDallesTown=14", logsA[0]);
            Assert.True(hostA.Ai.Map.UsesMapNeighborLists);
            Assert.Equal(AiMapV1Tests.ExpectedWheeledBlockedChokepoints, hostA.Ai.Map.WheeledBlockedCount);

            hostA.Shutdown();
            hostB.Shutdown();
        }

        [Fact]
        public void SaveLoadMidRun_ReplaysCommandsAndRestoresAiHash()
        {
            AiMatchHost original = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            original.Run(AiConstants.SaveLoadMidRunTick);
            ulong atSave = original.HashWorld();
            byte[] blob = original.Ai.Save();
            SimCommand[] log = CopyLog(original.CommandLog);

            AiMatchHost restored = AiMatchHost.ReplayTo(
                MatchConfig.Default(MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance(),
                log,
                blob,
                AiConstants.SaveLoadMidRunTick);

            Assert.Equal(original.Tick, restored.Tick);
            Assert.Equal(atSave, restored.HashWorld());
            Assert.Equal(
                original.Ai.PendingCountOf(AiConstants.DefaultAiFactionA)
                    + original.Ai.PendingCountOf(AiConstants.DefaultAiFactionB),
                restored.Ai.PendingCountOf(AiConstants.DefaultAiFactionA)
                    + restored.Ai.PendingCountOf(AiConstants.DefaultAiFactionB));

            int remain = AiConstants.M1DeterminismTicks - AiConstants.SaveLoadMidRunTick;
            original.Run(remain);
            restored.Run(remain);

            List<AiHashSample> originalTail = TailFrom(original.Hashes, AiConstants.SaveLoadMidRunTick);
            List<AiHashSample> restoredTail = TailFrom(restored.Hashes, AiConstants.SaveLoadMidRunTick);
            Assert.Equal(originalTail.Count, restoredTail.Count);
            for (int i = 0; i < originalTail.Count; i++)
            {
                Assert.Equal(originalTail[i].Tick, restoredTail[i].Tick);
                Assert.Equal(originalTail[i].Hash, restoredTail[i].Hash);
            }

            restored.Shutdown();
            original.Shutdown();
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
            List<ulong> first = Run(MatchSeed, out _, out _, out AiMatchHost hostA);
            List<ulong> other = Run(MatchSeed ^ 1UL, out _, out _, out AiMatchHost hostB);
            Assert.NotEqual(first, other);
            hostA.Shutdown();
            hostB.Shutdown();
        }

        private static List<ulong> Run(ulong seed, out List<string> logs, out int theDallesTownId, out AiMatchHost host)
        {
            host = Start(seed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(AiConstants.M1DeterminismTicks);
            logs = new List<string>(host.HeadlessLogs);
            theDallesTownId = host.Ai.Map.TheDallesTownId;
            List<ulong> hashes = new List<ulong>(host.Hashes.Count);
            for (int i = 0; i < host.Hashes.Count; i++)
            {
                hashes.Add(host.Hashes[i].Hash);
            }

            return hashes;
        }

        private static AiMatchHost Start(ulong seed, string difficultyId, AiBalanceTables balance)
        {
            return AiMatchHost.Start(
                MatchConfig.Default(seed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                difficultyId,
                balance);
        }

        private static SimCommand[] CopyLog(IReadOnlyList<SimCommand> log)
        {
            SimCommand[] copy = new SimCommand[log.Count];
            for (int i = 0; i < log.Count; i++)
            {
                copy[i] = log[i];
            }

            return copy;
        }

        private static List<AiHashSample> TailFrom(IReadOnlyList<AiHashSample> hashes, int tick)
        {
            List<AiHashSample> tail = new List<AiHashSample>();
            for (int i = 0; i < hashes.Count; i++)
            {
                if (hashes[i].Tick >= tick)
                {
                    tail.Add(hashes[i]);
                }
            }

            return tail;
        }

        private static void AssertRequiredHeadlessLogs(List<string> logs)
        {
            HashSet<int> ticks = new HashSet<int>();
            for (int i = 0; i < logs.Count; i++)
            {
                string line = logs[i];
                Assert.Contains("manpower=", line);
                Assert.Contains("tierIITick=" + AiConstants.TierIITickNone, line);
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
