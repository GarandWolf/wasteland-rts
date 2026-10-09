using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// AI capture follow-up. Two Gorge AI runs must match through a ClaimTown,
    /// a BuildDepot reserve, a reserved-site refund, and a mid-run Save/ReplayTo.
    /// Each run must have a non-empty command log, a Move from every AI seat, a
    /// RaiseBattalion in the log, and at least one ClaimTown and one BuildDepot.
    /// Remnant's authored prize group does not raise in this window; Compact does.
    /// Lead's 4000-tick hash/event identity belongs in DeterminismTests; scripted
    /// capture belongs in GorgeCaptureDeterminismTests. This file does not add
    /// to either.
    /// </summary>
    public sealed class AiCaptureFollowUpTests
    {
        [Fact]
        public void GorgeAi_ClaimDepotAndLoss_IsDeterministicAndSurvivesSaveLoad()
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            AiBalanceTables balance = BalanceLoad.Tables();

            AiMatchHost first = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            AiMatchHost second = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            bool claimed = false;
            bool depotBuilt = false;
            bool depotLost = false;
            List<ulong> firstHashes = new List<ulong>();
            List<ulong> secondHashes = new List<ulong>();
            List<int> firstEventKinds = new List<int>();
            List<int> secondEventKinds = new List<int>();

            for (int i = 0; i < AiConstants.M1DeterminismTicks; i++)
            {
                if (!depotLost && HasReservedBuilder(first))
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
                    claimed = SawClaim(first);
                }

                if (!depotBuilt
                    && (HasKind(first.World, SimEventKind.DepotStarted) || HasReservedDepot(first)))
                {
                    depotBuilt = true;
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
            AssertAiActed(first, factions);
            AssertAiActed(second, factions);
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
            AssertAiActed(original, factions);
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

        [Fact]
        public void PendingClaimTown_DropsAfterNoProgressTicks()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            AiFactionState remnant = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA);
            remnant.EnqueueIntent(new AiPendingIntent
            {
                Sequence = 77,
                Kind = AiIssuedKind.ClaimTown,
                BattalionId = 11,
                TargetId = host.Ai.Map.TheDallesTownId,
                IssuedTick = host.Tick - AiConstants.ClaimTownNoProgressTicks
            });
            Assert.True(remnant.IntentCount > 0);
            host.Run(1);
            bool stillPending = false;
            for (int i = 0; i < remnant.IntentCount; i++)
            {
                if (remnant.Intents[i].Sequence == 77)
                {
                    stillPending = true;
                }
            }

            Assert.False(stillPending);
            host.Shutdown();
        }

        [Fact]
        public void GorgeAi_RealBuildings_DoesNotSpamDepotRejectsOrMoves()
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            AiMatchHost host = AiMatchHost.Start(
                match,
                map,
                factions,
                AiConstants.DifficultyNormal,
                BalanceLoad.Tables());
            host.Run(AiConstants.DepotRejectSpamTicks);

            List<RejectCount> rejects = new List<RejectCount>();
            IReadOnlyList<SimEvent> events = host.World.Events;
            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind != SimEventKind.CommandRejected || !IsDepotNodeReject(ev.RejectReason))
                {
                    continue;
                }

                int issuer = (int)(ev.Key >> 32);
                int nodeId = ev.Param0;
                if (nodeId == 0)
                {
                    continue;
                }

                int row = IndexOfReject(rejects, issuer, nodeId);
                if (row < 0)
                {
                    RejectCount next = default;
                    next.IssuerId = issuer;
                    next.NodeId = nodeId;
                    next.Count = 1;
                    rejects.Add(next);
                }
                else
                {
                    RejectCount have = rejects[row];
                    have.Count++;
                    rejects[row] = have;
                }
            }

            for (int i = 0; i < rejects.Count; i++)
            {
                Assert.True(
                    rejects[i].Count <= AiConstants.MaxDepotRejectsPerNode,
                    "seat " + rejects[i].IssuerId
                    + " node " + rejects[i].NodeId
                    + " rejected " + rejects[i].Count + " times.");
            }

            List<MoveCount> moves = new List<MoveCount>();
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind != CommandKind.Move)
                {
                    continue;
                }

                int battalionId = log[i].BattalionId;
                int row = IndexOfMove(moves, battalionId);
                if (row < 0)
                {
                    MoveCount next = default;
                    next.BattalionId = battalionId;
                    next.Count = 1;
                    moves.Add(next);
                }
                else
                {
                    MoveCount have = moves[row];
                    have.Count++;
                    moves[row] = have;
                }
            }

            for (int i = 0; i < moves.Count; i++)
            {
                Assert.True(
                    moves[i].Count <= AiConstants.MaxMovesPerBattalion,
                    "battalion " + moves[i].BattalionId
                    + " received " + moves[i].Count + " Move commands.");
            }

            List<SeatCount> depots = new List<SeatCount>();
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind != CommandKind.BuildDepot)
                {
                    continue;
                }

                int issuer = log[i].IssuerId;
                int row = IndexOfSeat(depots, issuer);
                if (row < 0)
                {
                    SeatCount next = default;
                    next.SeatId = issuer;
                    next.Count = 1;
                    depots.Add(next);
                }
                else
                {
                    SeatCount have = depots[row];
                    have.Count++;
                    depots[row] = have;
                }
            }

            for (int i = 0; i < depots.Count; i++)
            {
                Assert.True(
                    depots[i].Count <= AiConstants.MaxBuildDepotsPerSeat,
                    "seat " + depots[i].SeatId
                    + " issued " + depots[i].Count + " BuildDepot commands.");
            }

            host.Shutdown();
        }

        private static bool IsDepotNodeReject(CommandRejectReason reason)
        {
            return reason == CommandRejectReason.UnknownNode
                || reason == CommandRejectReason.TerritoryNotOwned
                || reason == CommandRejectReason.NotConnectedToSupply
                || reason == CommandRejectReason.DepotTooClose
                || reason == CommandRejectReason.NodeHasDepot;
        }

        private static int IndexOfReject(List<RejectCount> rows, int issuerId, int nodeId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IssuerId == issuerId && rows[i].NodeId == nodeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfMove(List<MoveCount> rows, int battalionId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].BattalionId == battalionId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfSeat(List<SeatCount> rows, int seatId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].SeatId == seatId)
                {
                    return i;
                }
            }

            return -1;
        }

        private struct RejectCount
        {
            public int IssuerId;
            public int NodeId;
            public int Count;
        }

        private struct MoveCount
        {
            public int BattalionId;
            public int Count;
        }

        private struct SeatCount
        {
            public int SeatId;
            public int Count;
        }

        private static void AssertAiActed(AiMatchHost host, int[] factions)
        {
            IReadOnlyList<SimCommand> log = host.CommandLog;
            Assert.True(log.Count > 0, "AI command log is empty.");
            bool claimed = false;
            bool built = false;
            bool raised = false;
            int seatCount = factions.Length;
            int[] moves = new int[seatCount];
            int[] orders = new int[seatCount];
            int[] seatIds = new int[seatCount];
            for (int s = 0; s < seatCount; s++)
            {
                seatIds[s] = factions[s];
            }

            SortIds(seatIds);
            for (int i = 0; i < log.Count; i++)
            {
                SimCommand command = log[i];
                if (command.Kind == CommandKind.ClaimTown)
                {
                    claimed = true;
                }

                if (command.Kind == CommandKind.BuildDepot)
                {
                    built = true;
                }

                if (command.Kind == CommandKind.RaiseBattalion)
                {
                    raised = true;
                }

                int seat = IndexOfId(seatIds, command.IssuerId);
                if (seat < 0)
                {
                    continue;
                }

                if (command.Kind == CommandKind.RaiseBattalion
                    || command.Kind == CommandKind.ClaimTown
                    || command.Kind == CommandKind.BuildDepot)
                {
                    orders[seat]++;
                }

                if (command.Kind == CommandKind.Move)
                {
                    moves[seat]++;
                }
            }

            Assert.True(claimed, "AI never issued ClaimTown.");
            Assert.True(built, "AI never issued BuildDepot.");
            Assert.True(raised, "AI never issued RaiseBattalion.");
            for (int s = 0; s < seatCount; s++)
            {
                Assert.True(
                    moves[s] > 0,
                    "AI seat " + seatIds[s] + " never issued Move.");
                Assert.True(
                    orders[s] > 0,
                    "AI seat " + seatIds[s] + " never issued RaiseBattalion, ClaimTown, or BuildDepot.");
            }
        }

        private static int IndexOfId(int[] ids, int value)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SortIds(int[] ids)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                int row = ids[i];
                int j = i - 1;
                while (j >= 0 && ids[j] > row)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = row;
            }
        }

        private static bool HasReservedBuilder(AiMatchHost host)
        {
            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            for (int i = 0; i < depots.Count; i++)
            {
                DepotView depot = depots[i];
                if (depot.State == DepotSiteState.Reserved && depot.BuilderBattalionId != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ForceDepotLoss(AiMatchHost host)
        {
            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            for (int i = 0; i < depots.Count; i++)
            {
                DepotView depot = depots[i];
                if (depot.State != DepotSiteState.Reserved || depot.BuilderBattalionId == 0)
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

        private static bool HasReservedDepot(AiMatchHost host)
        {
            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind == CommandKind.BuildDepot)
                {
                    return true;
                }
            }

            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            return depots.Count > 0;
        }

        private static bool SawClaim(AiMatchHost host)
        {
            if (HasKind(host.World, SimEventKind.TownOwnerChanged)
                || HasKind(host.World, SimEventKind.TownContestStarted))
            {
                return true;
            }

            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind == CommandKind.ClaimTown)
                {
                    return true;
                }
            }

            List<TownView> towns = new List<TownView>();
            host.World.CopyTowns(towns);
            for (int i = 0; i < towns.Count; i++)
            {
                TownView town = towns[i];
                if (!town.IsSeatTown)
                {
                    continue;
                }

                if (town.CaptureFactionId != 0 || town.CaptureProgressTicks > 0 || town.Contested)
                {
                    return true;
                }
            }

            return false;
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
