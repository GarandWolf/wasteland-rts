using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Random;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiPendingRaiseTests
    {
        private const int Faction = 1;
        private const string LineUnit = "compact_rifle_battalion";
        private const string AtUnit = "compact_at_battalion";

        [Fact]
        public void TwoRaisesSameWindow_OnlyAffordableOneIsIssued()
        {
            AiMvpAttackTests.Harness harness = RaiseHarness();
            Assert.True(harness.Catalog.TryGetManpowerCost(LineUnit, out int lineCost));
            harness.SeatView.Manpower = lineCost;
            harness.Sink.CommandDelayTicks = SimConfig.DefaultCommandDelayTicks;
            harness.Tick(0);
            int first = CountRaises(harness.Sink);
            Assert.Equal(1, first);
            harness.Tick(1);
            Assert.Equal(first, CountRaises(harness.Sink));
        }

        [Fact]
        public void AfterFirstRaiseExecutes_NextDecisionSeesChargedManpower()
        {
            const int delay = 2;
            SimWorld world = new SimWorld(new MatchConfig(1UL, delay));
            BalanceCatalog catalog = Catalog();
            world.BindCatalog(catalog);
            Assert.True(catalog.TryGetManpowerCost(LineUnit, out int lineCost));
            world.BindSeat(Seat(Faction, 1, -1, lineCost * 2, 1000, 400, false, 10, LineUnit));
            SimWorldCommandSink inner = new SimWorldCommandSink(world);
            ForwardingAiCommandSink sink = new ForwardingAiCommandSink(inner, Owned());
            int id = sink.SubmitRaiseBattalion(Faction, LineUnit, 100, 100);
            Assert.Equal(1, id);
            world.Step();
            world.Step();
            Assert.Equal(delay, world.Tick);
            Assert.Equal(lineCost * 2, SeatOf(world).Manpower);
            world.ProcessQueue();
            Assert.Equal(lineCost, SeatOf(world).Manpower);
            Assert.True(world.TryCopyProductionQueue(Faction, new List<ProductionQueueItem>()));
            Assert.Single(Queue(world));

            AiMvpAttackTests.Harness harness = RaiseHarness();
            harness.SeatView.Manpower = SeatOf(world).Manpower;
            harness.Sink.CommandDelayTicks = delay;
            harness.Tick(world.Tick);
            Assert.Equal(1, CountRaises(harness.Sink));
            harness.SeatView.Manpower = 0;
            int after = CountRaises(harness.Sink);
            harness.Tick(world.Tick + 1);
            Assert.Equal(after, CountRaises(harness.Sink));
        }

        [Fact]
        public void CostReject_DoesNotResubmitUntilManpowerChanges()
        {
            AiMvpAttackTests.Harness harness = RaiseHarness();
            harness.Sink.NextReject = AiCommandReject.InsufficientManpower;
            harness.Tick(0);
            Assert.Equal((int)AiCommandReject.InsufficientManpower, harness.Faction.LastRejectCode);
            int until = harness.Faction.RejectBackoffUntilTick;
            Assert.Equal(AiAttackDefend.RejectBackoffStartTicks, until);
            int count = harness.Sink.Log.Count;
            harness.Tick(1);
            Assert.Equal(count, harness.Sink.Log.Count);
            harness.SeatView.Manpower = 0;
            harness.Tick(until);
            Assert.Equal(count, harness.Sink.Log.Count);
            harness.SeatView.Manpower = 300;
            harness.Tick(until + 1);
            Assert.True(CountRaises(harness.Sink) >= 1);
        }

        [Fact]
        public void ProductionSink_TakesBattalionIdsFromAllocateBattalionId()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 2));
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 1, -1, 1000, 1000, 400, false, 10, LineUnit));
            AiOwnedState owned = Owned();
            Assert.Equal(Faction * AiConstants.BattalionIdStride + 1, owned.GetFaction(Faction).NextBattalionId);
            ForwardingAiCommandSink sink = new ForwardingAiCommandSink(new SimWorldCommandSink(world), owned);
            int first = sink.SubmitRaiseBattalion(Faction, LineUnit, 0, 0);
            int second = sink.SubmitRaiseBattalion(Faction, LineUnit, 1, 1);
            Assert.Equal(1, first);
            Assert.Equal(2, second);
            Assert.Equal(Faction * AiConstants.BattalionIdStride + 1, owned.GetFaction(Faction).NextBattalionId);
        }

        [Fact]
        public void ExecuteTick_DoesNotDoubleCountPendingAndQueue()
        {
            const int delay = 2;
            SimWorld world = new SimWorld(new MatchConfig(1UL, delay));
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 1, -1, 1000, 1000, 400, false, 10, LineUnit));
            ForwardingAiCommandSink sink = new ForwardingAiCommandSink(new SimWorldCommandSink(world), Owned());
            sink.SubmitRaiseBattalion(Faction, LineUnit, 0, 0);
            world.Step();
            world.Step();
            Assert.Equal(delay, world.Tick);
            world.ProcessQueue();
            Assert.True(sink.RaiseHasStarted(1));
            Assert.Single(Queue(world));
            string[] pending = new string[8];
            int pendingCount = AiAttackDefend.CopyPendingRaiseUnitIds(sink, world.Tick, Faction, pending);
            Assert.Equal(0, pendingCount);
            int queuedLine = 0;
            List<ProductionQueueItem> queue = Queue(world);
            IAiUnitCatalog catalog = BalanceLoad.Tables().UnitCatalog;
            for (int i = 0; i < queue.Count; i++)
            {
                if (catalog.TryGetRole(queue[i].UnitId, out string role) && role == AiConstants.RoleLineInfantry)
                {
                    queuedLine++;
                }
            }

            Assert.Equal(1, queuedLine);
        }

        [Fact]
        public void RejectedRaise_LeavesPendingCount()
        {
            const int delay = 2;
            SimWorld world = new SimWorld(new MatchConfig(1UL, delay));
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 1, -1, 10, 1000, 400, false, 10, LineUnit));
            ForwardingAiCommandSink sink = new ForwardingAiCommandSink(new SimWorldCommandSink(world), Owned());
            int id = sink.SubmitRaiseBattalion(Faction, LineUnit, 0, 0);
            world.Step();
            world.Step();
            world.ProcessQueue();
            Assert.Equal(AiCommandReject.InsufficientManpower, sink.RejectOf(id));
            Assert.Empty(Queue(world));
            string[] pending = new string[8];
            int pendingCount = AiAttackDefend.CopyPendingRaiseUnitIds(sink, world.Tick, Faction, pending);
            Assert.Equal(0, pendingCount);
            AiAttackDefend.SumPendingRaises(sink, world.Tick, Faction, BalanceLoad.Tables().UnitCatalog, out int mp, out int pop);
            Assert.Equal(0, mp);
            Assert.Equal(0, pop);
        }

        [Fact]
        public void ClaimTown_DoesNotQueueMoveToTownIdAsCoordinates()
        {
            const int delay = 2;
            const int battalionId = 5;
            const int townId = 14;
            SimWorld world = new SimWorld(new MatchConfig(1UL, delay));
            world.BindCatalog(Catalog());
            world.Submit(SimCommand.SpawnBattalion(0, Faction, battalionId, 1000, 2000, 24000));
            world.Step();
            world.Step();
            world.ProcessQueue();
            SimBattalion before = world.GetBattalion(battalionId);
            Assert.False(before.HasTarget);
            ForwardingAiCommandSink sink = new ForwardingAiCommandSink(new SimWorldCommandSink(world), Owned());
            sink.SubmitClaimTown(Faction, battalionId, townId);
            world.Step();
            world.Step();
            world.ProcessQueue();
            SimBattalion after = world.GetBattalion(battalionId);
            Assert.False(after.HasTarget && after.TargetXCm == townId && after.TargetYCm == 0);
            Assert.Equal(before.PositionXCm, after.PositionXCm);
            Assert.Equal(before.PositionYCm, after.PositionYCm);
        }

        private static AiMvpAttackTests.Harness RaiseHarness()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = FixtureAiScenarioSeats.LaunchSeat("homestead_compact", 1UL);
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 3);
            harness.SetBattalions();
            harness.SeatView.Manpower = harness.Seat.StartingManpower;
            return harness;
        }

        private static int CountRaises(RecordingAiCommandSink sink)
        {
            int count = 0;
            for (int i = 0; i < sink.Log.Count; i++)
            {
                if (sink.Log[i].Kind == AiIssuedKind.RaiseBattalion)
                {
                    count++;
                }
            }

            return count;
        }

        private static AiOwnedState Owned()
        {
            AiFactionState faction = new AiFactionState(
                Faction,
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

        private static SeatSetup Seat(
            int factionId,
            int tier,
            int researchTick,
            int manpower,
            int materials,
            int fuel,
            bool fuelEnabled,
            int popCap,
            params string[] units)
        {
            return new SeatSetup(
                factionId,
                factionId,
                "homestead_compact",
                7,
                0,
                0,
                tier,
                researchTick,
                manpower,
                materials,
                fuel,
                fuelEnabled,
                popCap,
                units);
        }

        private static BalanceCatalog Catalog()
        {
            return BalanceCatalog.LoadFromDirectory(Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance"));
        }

        private static SeatView SeatOf(SimWorld world)
        {
            Assert.True(world.TryGetSeat(Faction, out SeatView view));
            return view;
        }

        private static List<ProductionQueueItem> Queue(SimWorld world)
        {
            List<ProductionQueueItem> items = new List<ProductionQueueItem>();
            Assert.True(world.TryCopyProductionQueue(Faction, items));
            return items;
        }
    }
}
