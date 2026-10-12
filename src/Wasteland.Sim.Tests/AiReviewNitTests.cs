using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiReviewNitTests
    {
        [Fact]
        public void MoveIntents_CannotFillSlotsReservedForOtherOrders()
        {
            AiOwnedState owned = Owned(1);
            AiFactionState faction = owned.GetFaction(1);
            int moveCap = AiConstants.MoveIntentCapacity;
            Assert.True(moveCap < AiConstants.PendingIntentCapacity);
            Assert.Equal(AiConstants.ReservedNonMoveIntentSlots, AiConstants.PendingIntentCapacity - moveCap);

            for (int i = 0; i < moveCap + 4; i++)
            {
                owned.EnqueueIntent(1, MoveIntent(i + 1, 1, i + 1));
            }

            Assert.Equal(moveCap, CountKind(faction, AiIssuedKind.Move));
            Assert.Equal(moveCap, faction.IntentCount);
            Assert.False(HasIntent(faction, 1));
            Assert.True(HasIntent(faction, moveCap + 4));

            int claimSequence = 50000;
            owned.EnqueueIntent(1, new AiPendingIntent
            {
                Sequence = claimSequence,
                Kind = AiIssuedKind.ClaimTown,
                BattalionId = 1,
                TargetId = 77,
                IssuedTick = 0
            });
            Assert.True(HasIntent(faction, claimSequence));
            Assert.Equal(moveCap + 1, faction.IntentCount);

            owned.EnqueueIntent(1, MoveIntent(50001, 1, 50001));
            Assert.True(HasIntent(faction, claimSequence));
            Assert.Equal(moveCap, CountKind(faction, AiIssuedKind.Move));
            Assert.Equal(1, CountKind(faction, AiIssuedKind.ClaimTown));

            int guard = 0;
            while (faction.IntentCount < faction.Intents.Length)
            {
                int sequence = 60000 + guard;
                owned.EnqueueIntent(1, new AiPendingIntent
                {
                    Sequence = sequence,
                    Kind = AiIssuedKind.BuildDepot,
                    BattalionId = 1,
                    TargetId = sequence,
                    IssuedTick = 0
                });
                guard++;
                Assert.True(guard < AiConstants.PendingIntentCapacity);
            }

            int movesBefore = CountKind(faction, AiIssuedKind.Move);
            Assert.True(movesBefore > 0);
            owned.EnqueueIntent(1, new AiPendingIntent
            {
                Sequence = 70000,
                Kind = AiIssuedKind.BuildDepot,
                BattalionId = 1,
                TargetId = 70000,
                IssuedTick = 0
            });
            Assert.Equal(AiConstants.PendingIntentCapacity, faction.IntentCount);
            Assert.True(HasIntent(faction, claimSequence));
            Assert.True(HasIntent(faction, 70000));
            Assert.True(CountKind(faction, AiIssuedKind.Move) < movesBefore);
        }

        [Fact]
        public void MoveSuccess_CountsOnlyAfterExecuteTickWithNoReject()
        {
            AiMatchHost host = StartHost();
            Quiet(host);
            Assert.True(TryIdleBattalion(host, out SimBattalion battalion));
            int nodeId = NearestNodeId(host.Ai.Map.Roads, battalion.PositionXCm, battalion.PositionYCm);
            int seatId = battalion.OwnerId;
            const int openSequence = 424242;
            host.Ai.Owned.NoteOrderReject(seatId, battalion.Id, CommandKind.Move, nodeId, host.Tick);
            host.Ai.Owned.EnqueueIntent(seatId, new AiPendingIntent
            {
                Sequence = openSequence,
                Kind = AiIssuedKind.Move,
                BattalionId = battalion.Id,
                TargetId = nodeId,
                IssuedTick = host.Tick
            });

            int delay = host.World.CommandDelayTicks;
            int issued = host.Tick;
            host.Run(2);
            Assert.Equal(issued + 2, host.Tick);
            Assert.True(issued + delay > host.Tick - 1);
            Assert.True(HasIntent(host.Ai.Owned.GetFaction(seatId), openSequence));
            Assert.Equal(1, Streak(host, seatId, battalion.Id, nodeId));
            Assert.False(host.World.GetBattalion(battalion.Id).HasTarget);

            int pastTick = issued + delay + 1;
            if (host.Tick < pastTick)
            {
                host.Run(pastTick - host.Tick);
            }

            host.Run(1);
            Assert.False(HasIntent(host.Ai.Owned.GetFaction(seatId), openSequence));
            Assert.Equal(0, Streak(host, seatId, battalion.Id, nodeId));

            Quiet(host);
            Assert.False(host.World.GetBattalion(battalion.Id).HasTarget);
            nodeId = NearestNodeId(host.Ai.Map.Roads, host.World.GetBattalion(battalion.Id).PositionXCm, host.World.GetBattalion(battalion.Id).PositionYCm);
            host.Ai.Owned.NoteOrderReject(seatId, battalion.Id, CommandKind.Move, nodeId, host.Tick);
            host.World.ForceNextRoadNoPath = true;
            SimCommand stamped = host.World.Submit(
                SimCommand.Move(host.Tick, seatId, battalion.Id, battalion.PositionXCm + 500000, battalion.PositionYCm));
            host.Ai.Owned.EnqueueIntent(seatId, new AiPendingIntent
            {
                Sequence = stamped.Sequence,
                Kind = AiIssuedKind.Move,
                BattalionId = battalion.Id,
                TargetId = nodeId,
                IssuedTick = host.Tick
            });
            int rejectIssued = host.Tick;
            host.Run(2);
            Assert.True(HasIntent(host.Ai.Owned.GetFaction(seatId), stamped.Sequence));
            Assert.Equal(1, Streak(host, seatId, battalion.Id, nodeId));
            int rejectPast = rejectIssued + delay + 1;
            if (host.Tick < rejectPast)
            {
                host.Run(rejectPast - host.Tick);
            }

            host.Run(1);
            Assert.False(HasIntent(host.Ai.Owned.GetFaction(seatId), stamped.Sequence));
            Assert.Equal(2, Streak(host, seatId, battalion.Id, nodeId));
            Assert.True(RejectedMove(host, seatId, stamped.Sequence));
            host.Shutdown();
        }

        [Fact]
        public void StockShortfall_SkipsKeyedBackoffLikeManpower()
        {
            AssertKeyedBackoffSkipped(AiCommandReject.InsufficientManpower);
            AssertKeyedBackoffSkipped(AiCommandReject.InsufficientMaterials);
            AssertKeyedBackoffSkipped(AiCommandReject.InsufficientFuel);

            AiMvpAttackTests.Harness rejected = AttackHarness();
            AiOwnedState owned = new AiOwnedState(new[] { rejected.Faction }, 1);
            AiAttackDefend planner = new AiAttackDefend(rejected.Sink, rejected, rejected.Sink, null, owned);
            rejected.Sink.NextReject = AiCommandReject.Rejected;
            Tick(rejected, planner, 0);
            Assert.True(rejected.Sink.Log.Count > 0);
            Assert.True(owned.BackoffCount > 0);
            Assert.True(rejected.Faction.RejectBackoffUntilTick > 0);
        }

        [Fact]
        public void RejectionByKey_IndexesNewEventsInsteadOfRescanningHistory()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            SimCommand first = world.Submit(SimCommand.Move(0, 1, 99, 10, 10));
            SimCommand second = world.Submit(SimCommand.Move(0, 4, 98, 12, 12));
            world.Step();
            world.Step();
            world.Step();
            ulong firstKey = RejectKey(1, first.Sequence);
            ulong secondKey = RejectKey(4, second.Sequence);
            Assert.True(sink.TryGetRejectionByKey(firstKey, out CommandRejectReason code, out int param0, out int param1));
            Assert.Equal(CommandRejectReason.UnknownBattalion, code);
            Assert.Equal(0, param0);
            Assert.Equal(0, param1);
            int indexed = world.Events.Count;
            Assert.True(indexed > 0);
            Assert.Equal(indexed, sink.LastRejectionScanCount);

            world.Step();
            world.Step();
            int added = world.Events.Count - indexed;
            Assert.True(sink.TryGetRejectionByKey(secondKey, out code, out _, out _));
            Assert.Equal(CommandRejectReason.UnknownBattalion, code);
            Assert.Equal(added, sink.LastRejectionScanCount);
            Assert.True(sink.LastRejectionScanCount < world.Events.Count);
            Assert.True(sink.TryGetRejectionByKey(firstKey, out code, out _, out _));
            Assert.Equal(CommandRejectReason.UnknownBattalion, code);
            Assert.Equal(0, sink.LastRejectionScanCount);
        }

        [Fact]
        public void MvpSeatIds_FollowBindingsAndScenarioFaction_NotSlotPlusOne()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json")
                .Replace("\"factionId\": 1", "\"factionId\": 40")
                .Replace("\"factionId\": 2", "\"factionId\": 70");
            AiBalanceTables shifted = AiBalanceTables.Parse(
                personalities,
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json"));
            SimScenario scenario = LoadMvp();
            Assert.True(AiMatchHost.TryScenarioSeatForFaction(shifted, scenario, 40, out ScenarioSeat remnant));
            Assert.Equal("remnant_command", remnant.Faction);
            Assert.Equal(0, remnant.Slot);
            Assert.True(AiMatchHost.TryScenarioSeatForFaction(shifted, scenario, 70, out ScenarioSeat compact));
            Assert.Equal("homestead_compact", compact.Faction);
            Assert.Equal(1, compact.Slot);
            Assert.False(AiMatchHost.TryScenarioSeatForFaction(shifted, scenario, 1, out _));
            Assert.False(AiMatchHost.TryScenarioSeatForFaction(shifted, scenario, 2, out _));

            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            AiMatchHost host = StartHost();
            Assert.True(balance.BindingCount >= 2);
            for (int i = 0; i < balance.BindingCount; i++)
            {
                int factionId = balance.BindingFactionIdAt(i);
                Assert.True(AiMatchHost.TryScenarioSeatForFaction(balance, scenario, factionId, out ScenarioSeat seat));
                Assert.True(host.World.TryGetSeat(factionId, out SeatView view));
                Assert.Equal(seat.Slot, view.Slot);
                Assert.Equal(seat.HomeTownId, view.HomeTownId);
            }

            host.Shutdown();
        }

        private static void AssertKeyedBackoffSkipped(AiCommandReject reject)
        {
            AiMvpAttackTests.Harness harness = AttackHarness();
            AiOwnedState owned = new AiOwnedState(new[] { harness.Faction }, 1);
            AiAttackDefend planner = new AiAttackDefend(harness.Sink, harness, harness.Sink, null, owned);
            harness.Sink.NextReject = reject;
            Tick(harness, planner, 0);
            Assert.True(harness.Sink.Log.Count > 0);
            Assert.Equal(0, owned.BackoffCount);
            Assert.Equal((int)reject, harness.Faction.LastRejectCode);
            Assert.Equal(AiAttackDefend.RejectBackoffStartTicks, harness.Faction.RejectBackoffUntilTick);
        }

        private static void Tick(AiMvpAttackTests.Harness harness, AiAttackDefend planner, int tick)
        {
            harness.Faction.WorkUnitsThisTick = 0;
            harness.Sink.Tick = tick;
            harness.SeatView.Refresh(harness.Seat, tick);
            AiBattalionPin[] pins =
            {
                AiMvpAttackTests.PinWithRole(5, 0, 0, 1, AiConstants.RoleLineInfantry, false),
                AiMvpAttackTests.PinWithRole(6, 0, 0, 1, AiConstants.RoleLineInfantry, false)
            };
            planner.Tick(
                tick,
                harness.Faction,
                harness.Seat,
                harness.TownRows,
                harness.TownRows.Length,
                pins,
                pins.Length,
                harness.Network,
                1,
                harness.Roads,
                1500,
                harness.SupplyReachCm,
                harness.PrizeTerritoryKey,
                harness.PrizeTargetBonusPpt,
                harness.SeatView,
                harness.Catalog,
                harness.Buildings,
                harness.LineUnitsPerAntiArmour,
                harness.MinGarrisonPerTown,
                harness.TaskForceTargetSize);
        }

        private static AiMvpAttackTests.Harness AttackHarness()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            return harness;
        }

        private static AiPendingIntent MoveIntent(int sequence, int battalionId, int targetId)
        {
            return new AiPendingIntent
            {
                Sequence = sequence,
                Kind = AiIssuedKind.Move,
                BattalionId = battalionId,
                TargetId = targetId,
                IssuedTick = 0
            };
        }

        private static int CountKind(AiFactionState faction, AiIssuedKind kind)
        {
            int count = 0;
            for (int i = 0; i < faction.IntentCount; i++)
            {
                if (faction.Intents[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasIntent(AiFactionState faction, int sequence)
        {
            for (int i = 0; i < faction.IntentCount; i++)
            {
                if (faction.Intents[i].Sequence == sequence)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Streak(AiMatchHost host, int seatId, int battalionId, int targetId)
        {
            AiOrderBackoffEntry[] rows = host.Ai.Owned.OrderBackoff;
            for (int i = 0; i < host.Ai.Owned.BackoffCount; i++)
            {
                AiOrderBackoffEntry row = rows[i];
                if (row.SeatId == seatId
                    && row.BattalionId == battalionId
                    && row.Kind == (byte)CommandKind.Move
                    && row.TargetId == targetId)
                {
                    return row.Streak;
                }
            }

            return 0;
        }

        private static bool RejectedMove(AiMatchHost host, int issuerId, int sequence)
        {
            ulong key = RejectKey(issuerId, sequence);
            IReadOnlyList<SimEvent> events = host.World.Events;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind == SimEventKind.CommandRejected && ev.Key == key)
                {
                    return ev.RejectReason == CommandRejectReason.NoPath;
                }
            }

            return false;
        }

        private static ulong RejectKey(int issuerId, int sequence)
        {
            return ((ulong)(uint)issuerId << 32) | (uint)sequence;
        }

        private static bool TryIdleBattalion(AiMatchHost host, out SimBattalion battalion)
        {
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion candidate = battalions[i];
                if (candidate.HasTarget || !host.Ai.Owned.TryGetFaction(candidate.OwnerId, out _))
                {
                    continue;
                }

                battalion = candidate;
                return true;
            }

            battalion = default;
            return false;
        }

        private static int NearestNodeId(RoadGraph roads, int xCm, int yCm)
        {
            int bestIndex = 0;
            int bestId = roads.GetNodeId(0);
            long bestDist = long.MaxValue;
            for (int i = 0; i < roads.NodeCount; i++)
            {
                long dx = (long)roads.GetNodeXCm(i) - xCm;
                long dy = (long)roads.GetNodeYCm(i) - yCm;
                long dist = (dx * dx) + (dy * dy);
                int id = roads.GetNodeId(i);
                if (dist < bestDist || (dist == bestDist && id < bestId))
                {
                    bestDist = dist;
                    bestId = id;
                    bestIndex = i;
                }
            }

            return roads.GetNodeId(bestIndex);
        }

        private static void Quiet(AiMatchHost host)
        {
            for (int i = 0; i < host.Ai.Owned.FactionCount; i++)
            {
                AiFactionState faction = host.Ai.Owned.FactionAt(i);
                faction.OrderCount = 0;
                faction.PendingCount = 0;
                faction.StrategicDeferred = false;
                faction.RejectBackoffUntilTick = int.MaxValue;
            }
        }

        private static AiMatchHost StartHost()
        {
            return AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
        }

        private static SimScenario LoadMvp()
        {
            BalanceLoad.SharedPack pack = BalanceLoad.Shared();
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            return SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                pack.Scenario,
                map,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");
        }

        private static AiOwnedState Owned(int factionId)
        {
            AiFactionState faction = new AiFactionState(
                factionId,
                AiConstants.PersonalitySettler,
                AiConstants.DifficultyNormal,
                0,
                40,
                4,
                1,
                60,
                8,
                1,
                0,
                50000,
                4,
                4,
                AiPosture.Attack,
                new SimRandom(1UL),
                new SimRandom(3UL),
                new SimRandom(5UL),
                new SimRandom(7UL));
            return new AiOwnedState(new[] { faction }, 1);
        }
    }
}
