using System;
using Wasteland.Sim.Ai;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiSeatCatalogTests
    {
        private const int CompactTicks = 240;
        private const int RemnantStartTick = 40;
        private const int RemnantDurationTicks = 20;
        private const int RemnantAvailableTick = RemnantStartTick + RemnantDurationTicks;
        private const int RemnantTicks = 90;

        [Fact]
        public void CompactStyleSeat_EmptyResearch_NeverReachesTierII()
        {
            FixtureAiScenarioSeats seats = CompactSeats();
            Assert.False(seats.FuelEnabled);
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            FixtureAiSideReadback side = Bound(seat);
            Assert.True(seat.AllowsUnit(AiConstants.UnitKindInfantry, 0));
            Assert.True(seat.AllowsUnit(AiConstants.UnitKindEngineer, 0));
            Assert.Equal(1, side.CurrentTier(1, 0));
            Assert.Equal(1, side.CurrentTier(1, 10000));
            Assert.False(seat.AllowsUnitForTier(AiConstants.UnitKindTierII, side.CurrentTier(1, 10000)));
            Assert.False(seat.AllowsUnit(AiConstants.UnitKindTierIII, 0));
            Assert.True(seat.AllowsBuilding(AiConstants.BuildingTagDepot));
            Assert.True(seat.AllowsBuilding(AiConstants.BuildingTagFortL1));
            Assert.False(seat.AllowsBuilding(AiConstants.BuildingTagFortL2));
        }

        [Fact]
        public void CompactStyleMatch_NeverIssuesOutOfCatalogRaiseOrBuild()
        {
            FixtureAiScenarioSeats seats = CompactSeats();
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.SetBattalions(
                new AiBattalionPin(5, 0, 0, 1, AiConstants.UnitKindInfantry, false),
                new AiBattalionPin(7, 0, 0, 1, AiConstants.UnitKindEngineer, false));

            for (int tick = 0; tick < CompactTicks; tick++)
            {
                int before = harness.Sink.Log.Count;
                harness.Tick(tick);
                AssertSinkRespectsSeat(harness.Sink, harness.Side, before, seat, tick, false);
            }
        }

        [Fact]
        public void RemnantStyleSeat_RaisesTierIIOnlyAfterStartPlusDuration()
        {
            FixtureAiScenarioSeats seats = RemnantSeats();
            Assert.False(seats.FuelEnabled);
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView seat));
            FixtureAiSideReadback side = Bound(seat);
            Assert.Equal(1, side.CurrentTier(1, RemnantAvailableTick - 1));
            Assert.Equal(2, side.CurrentTier(1, RemnantAvailableTick));
            Assert.False(seat.AllowsUnitForTier(AiConstants.UnitKindTierII, side.CurrentTier(1, RemnantAvailableTick - 1)));
            Assert.True(seat.AllowsUnitForTier(AiConstants.UnitKindTierII, side.CurrentTier(1, RemnantAvailableTick)));

            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 2);
            harness.SetBattalions();

            bool sawTierII = false;
            for (int tick = 0; tick < RemnantTicks; tick++)
            {
                int before = harness.Sink.Log.Count;
                harness.Tick(tick);
                AssertSinkRespectsSeat(harness.Sink, harness.Side, before, seat, tick, true);
                for (int i = before; i < harness.Sink.Log.Count; i++)
                {
                    AiIssuedCommand cmd = harness.Sink.Log[i];
                    if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                    {
                        continue;
                    }

                    if (cmd.Arg0 == AiConstants.UnitKindTierII)
                    {
                        Assert.True(tick >= RemnantAvailableTick);
                        sawTierII = true;
                    }
                    else if (tick < RemnantAvailableTick)
                    {
                        Assert.NotEqual(AiConstants.UnitKindTierII, cmd.Arg0);
                    }
                }
            }

            Assert.True(sawTierII);
        }

        [Fact]
        public void Sink_NeverRecordsTierLockedRaiseRequest()
        {
            RunCompactAndAssertNoTierLocked();
            RunRemnantAndAssertNoTierLocked();
        }

        [Fact]
        public void PopCapReached_SkipsRaiseAndBacksOff()
        {
            FixtureAiScenarioSeats seats = RemnantSeats();
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView seat));
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.Side.PopUsed = 4;
            harness.Side.PopCap = 4;
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 2);
            harness.SetBattalions();
            harness.Tick(0);
            Assert.Empty(harness.Sink.Log);
            Assert.Equal((int)AiCommandReject.PopCapReached, harness.Faction.LastRejectCode);
            int until = harness.Faction.RejectBackoffUntilTick;
            Assert.Equal(AiAttackDefend.RejectBackoffStartTicks, until);
            harness.Tick(1);
            Assert.Empty(harness.Sink.Log);
        }

        [Fact]
        public void TierLockedReject_BacksOffWithoutPerTickRetry()
        {
            FixtureAiScenarioSeats seats = RemnantSeats();
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView seat));
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 2);
            harness.SetBattalions();
            harness.Sink.NextReject = AiCommandReject.TierLocked;
            harness.Tick(RemnantAvailableTick);
            Assert.Equal((int)AiCommandReject.TierLocked, harness.Faction.LastRejectCode);
            int until = harness.Faction.RejectBackoffUntilTick;
            Assert.Equal(RemnantAvailableTick + AiAttackDefend.RejectBackoffStartTicks, until);
            int count = harness.Sink.Log.Count;
            harness.Tick(RemnantAvailableTick + 1);
            Assert.Equal(count, harness.Sink.Log.Count);
        }

        [Fact]
        public void BalanceTables_ExposeTier2ResearchDuration()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.True(tables.Tier2ResearchTicks >= 0);
            AiAutoResearch research = new AiAutoResearch(40, 2, tables.Tier2ResearchTicks);
            Assert.Equal(40 + tables.Tier2ResearchTicks, research.AvailableTick);
        }

        private static void RunCompactAndAssertNoTierLocked()
        {
            FixtureAiScenarioSeats seats = CompactSeats();
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.SetBattalions(
                new AiBattalionPin(5, 0, 0, 1, AiConstants.UnitKindInfantry, false),
                new AiBattalionPin(7, 0, 0, 1, AiConstants.UnitKindEngineer, false));
            for (int tick = 0; tick < CompactTicks; tick++)
            {
                harness.Tick(tick);
            }

            AssertNoTierLockedRequest(harness);
        }

        private static void RunRemnantAndAssertNoTierLocked()
        {
            FixtureAiScenarioSeats seats = RemnantSeats();
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView seat));
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 2);
            harness.SetBattalions();
            for (int tick = 0; tick < RemnantTicks; tick++)
            {
                harness.Tick(tick);
            }

            AssertNoTierLockedRequest(harness);
        }

        private static void AssertNoTierLockedRequest(AiMvpAttackTests.Harness harness)
        {
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                Assert.NotEqual(AiCommandReject.TierLocked, harness.Sink.RejectOf(cmd.Id));
                if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                {
                    continue;
                }

                if (cmd.Arg0 == AiConstants.UnitKindTierII)
                {
                    Assert.True(harness.Side.CurrentTier(cmd.FactionId, cmd.Tick) >= 2);
                }
            }
        }

        private static void AssertSinkRespectsSeat(
            RecordingAiCommandSink sink,
            IAiSideReadback side,
            int from,
            AiScenarioSeatView seat,
            int tick,
            bool allowTierIIAfterAvailable)
        {
            for (int i = from; i < sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = sink.Log[i];
                Assert.Equal(tick, cmd.Tick);
                if (cmd.Kind == AiIssuedKind.RaiseBattalion)
                {
                    int unitKind = cmd.Arg0;
                    int currentTier = side.CurrentTier(cmd.FactionId, tick);
                    Assert.True(seat.AllowsUnitForTier(unitKind, currentTier));
                    Assert.NotEqual(AiConstants.UnitKindTierIII, unitKind);
                    if (!allowTierIIAfterAvailable || currentTier < 2)
                    {
                        Assert.NotEqual(AiConstants.UnitKindTierII, unitKind);
                    }

                    continue;
                }

                if (cmd.Kind == AiIssuedKind.BuildDepot)
                {
                    Assert.True(seat.AllowsBuilding(AiConstants.BuildingTagDepot));
                    continue;
                }

                Assert.True(cmd.Kind == AiIssuedKind.Move || cmd.Kind == AiIssuedKind.ClaimTown);
            }
        }

        private static FixtureAiSideReadback Bound(AiScenarioSeatView seat)
        {
            FixtureAiSideReadback side = new FixtureAiSideReadback();
            side.Bind(seat);
            return side;
        }

        private static FixtureAiScenarioSeats CompactSeats()
        {
            return FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        "homestead_compact",
                        "homestead_compact_settler",
                        10,
                        300,
                        new[]
                        {
                            "compact_militia",
                            "compact_rifle_battalion",
                            "compact_at_battalion",
                            "compact_engineers"
                        },
                        new[] { "supply_depot", "town_walls_l1" },
                        Array.Empty<AiMvpAutoResearchItem>())
                },
                new AiMvpMatchRulesShape(false, "draw"),
                RemnantDurationTicks);
        }

        private static FixtureAiScenarioSeats RemnantSeats()
        {
            return FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        "remnant_command",
                        string.Empty,
                        10,
                        300,
                        new[] { "remnant_motor_rifles", "remnant_engineers" },
                        new[] { "supply_depot" },
                        new[] { new AiMvpAutoResearchItem("tier2", RemnantStartTick) })
                },
                new AiMvpMatchRulesShape(false, "draw"),
                RemnantDurationTicks);
        }
    }
}
