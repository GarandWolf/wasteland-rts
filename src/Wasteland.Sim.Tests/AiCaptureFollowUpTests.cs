using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiCaptureFollowUpTests
    {
        [Fact]
        public void GorgeAi_ClaimDepotAndLoss_IsDeterministicAndSurvivesSaveLoad()
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();

            AiMatchHost first = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            AiMatchHost second = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            bool claimed = false;
            bool depotBuilt = false;
            bool depotLost = false;
            int forcedLossTick = -1;
            List<ulong> firstHashes = new List<ulong>();
            List<ulong> secondHashes = new List<ulong>();
            List<int> firstEventKinds = new List<int>();
            List<int> secondEventKinds = new List<int>();

            for (int i = 0; i < AiConstants.M1DeterminismTicks; i++)
            {
                if (forcedLossTick == i)
                {
                    ForceDepotLoss(first);
                    ForceDepotLoss(second);
                }

                first.Step();
                second.Step();
                firstHashes.Add(first.HashWorld());
                secondHashes.Add(second.HashWorld());
                FingerprintEvents(first.World, firstEventKinds);
                FingerprintEvents(second.World, secondEventKinds);
                Assert.Equal(firstHashes[i], secondHashes[i]);
                Assert.Equal(firstEventKinds.Count, secondEventKinds.Count);
                Assert.Equal(firstEventKinds[firstEventKinds.Count - 1], secondEventKinds[secondEventKinds.Count - 1]);

                if (!claimed)
                {
                    claimed = HasKind(first.World, SimEventKind.TownOwnerChanged);
                }

                if (!depotBuilt && HasKind(first.World, SimEventKind.DepotStarted))
                {
                    depotBuilt = true;
                    if (forcedLossTick < 0)
                    {
                        forcedLossTick = i + 2;
                    }
                }

                if (!depotLost)
                {
                    depotLost = HasKind(first.World, SimEventKind.OrderRefunded)
                        || HasKind(first.World, SimEventKind.DepotDestroyed);
                }
            }

            Assert.True(claimed, "Gorge AI never claimed a town.");
            Assert.True(depotBuilt, "Gorge AI never reserved a depot.");
            Assert.True(depotLost, "Gorge AI never lost a depot (refund or destroy).");
            Assert.Equal(firstHashes, secondHashes);
            Assert.Equal(firstEventKinds, secondEventKinds);

            first.Shutdown();
            second.Shutdown();

            AiMatchHost original = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            int mid = AiConstants.SaveLoadMidRunTick;
            original.Run(mid);
            ulong atSave = original.HashWorld();
            byte[] blob = original.Ai.Save();
            SimCommand[] log = CopyLog(original.CommandLog);
            AiMatchHost restored = AiMatchHost.ReplayTo(
                match,
                map,
                factions,
                AiConstants.DifficultyNormal,
                balance,
                log,
                blob,
                mid);
            Assert.Equal(original.Tick, restored.Tick);
            Assert.Equal(atSave, restored.HashWorld());
            int remain = AiConstants.M1DeterminismTicks - mid;
            original.Run(remain);
            restored.Run(remain);
            Assert.Equal(original.HashWorld(), restored.HashWorld());
            restored.Shutdown();
            original.Shutdown();
        }

        [Fact]
        public void PendingIntent_ClearsOnCommandRejectedKey()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            AiFactionState remnant = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA);
            int before = remnant.IntentCount;
            int battalionId = 11;
            if (host.World.BattalionCount > 0)
            {
                battalionId = host.World.Battalions[0].Id;
            }

            SimCommand stamped = host.World.Submit(
                SimCommand.ClaimTown(host.Tick, remnant.FactionId, battalionId, 0));
            remnant.EnqueueIntent(new AiPendingIntent
            {
                Sequence = stamped.Sequence,
                Kind = AiIssuedKind.ClaimTown,
                BattalionId = battalionId,
                TargetId = 0
            });
            Assert.True(remnant.IntentCount > before);
            host.Run(host.World.CommandDelayTicks + 2);
            bool stillPending = false;
            for (int i = 0; i < remnant.IntentCount; i++)
            {
                if (remnant.Intents[i].Sequence == stamped.Sequence)
                {
                    stillPending = true;
                }
            }

            Assert.False(stillPending);
            host.Shutdown();
        }

        private static void ForceDepotLoss(AiMatchHost host)
        {
            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            for (int i = 0; i < depots.Count; i++)
            {
                DepotView depot = depots[i];
                if (depot.BuilderBattalionId == 0)
                {
                    continue;
                }

                host.World.Submit(SimCommand.Move(
                    host.Tick,
                    depot.OwnerFactionId,
                    depot.BuilderBattalionId,
                    0,
                    0));
                return;
            }
        }

        private static void FingerprintEvents(SimWorld world, List<int> into)
        {
            IReadOnlyList<SimEvent> events = world.Events;
            int mix = events.Count;
            for (int i = 0; i < events.Count; i++)
            {
                mix = (mix * 33) + (int)events[i].Kind;
                mix = (mix * 33) + events[i].SubjectId;
                mix = (mix * 33) + events[i].AuxId;
            }

            into.Add(mix);
        }

        private static bool HasKind(SimWorld world, SimEventKind kind)
        {
            IReadOnlyList<SimEvent> events = world.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
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
    }
}
