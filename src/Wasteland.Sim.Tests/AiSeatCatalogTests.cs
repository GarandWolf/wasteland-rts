using System;
using Wasteland.Sim.Ai;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiSeatCatalogTests
    {
        private const int CompactTicks = 240;
        private const int RemnantStartTick = 40;
        private static readonly int RemnantDurationTicks =
            AiBalanceTablesTests.LoadBalance().Tier2ResearchTicks;
        private static readonly int RemnantAvailableTick = RemnantStartTick + RemnantDurationTicks;
        private static readonly int RemnantTicks = RemnantAvailableTick + 50;

        [Fact]
        public void IAiSeatView_MirrorsLeadFieldNames()
        {
            IAiSeatView view = new FixtureAiSeatView();
            Assert.Equal(1, view.CurrentTier);
            Assert.Equal(-1, view.TierResearchCompleteTick);
            Assert.Equal(0, view.Manpower);
            Assert.Equal(0, view.Materials);
            Assert.Equal(0, view.PopUsed);
            Assert.Equal(0, view.PopCap);
            Assert.False(view.HasSeenEnemyArmourOrMech);
        }

        [Fact]
        public void CompactStyleSeat_EmptyResearch_NeverReachesTierII()
        {
            FixtureAiScenarioSeats seats = CompactSeats();
            Assert.False(seats.FuelEnabled);
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            FixtureAiSeatView view = ViewAt(seat, 0);
            IAiUnitCatalog catalog = Catalog();
            Assert.True(HasRaisableRole(seat, catalog, AiConstants.RoleLineInfantry, 1));
            Assert.True(HasRaisableRole(seat, catalog, AiConstants.RoleEngineer, 1));
            Assert.Equal(1, view.CurrentTier);
            Assert.Equal(-1, view.TierResearchCompleteTick);
            view.Refresh(seat, 10000);
            Assert.Equal(1, view.CurrentTier);
            Assert.Equal(-1, view.TierResearchCompleteTick);
            Assert.False(HasRaisableAboveTier(seat, catalog, 1));
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
                AiMvpAttackTests.PinWithRole(5, 0, 0, 1, AiConstants.RoleLineInfantry),
                AiMvpAttackTests.PinWithRole(7, 0, 0, 1, AiConstants.RoleEngineer));

            for (int tick = 0; tick < CompactTicks; tick++)
            {
                int before = harness.Sink.Log.Count;
                harness.Tick(tick);
                AssertSinkRespectsSeat(harness.Sink, harness.SeatView, before, seat, tick, false);
            }
        }

        [Fact]
        public void RemnantStyleSeat_RaisesTierIIOnlyAfterFixtureResearchCompletes()
        {
            FixtureAiScenarioSeats seats = RemnantSeats();
            Assert.False(seats.FuelEnabled);
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView seat));
            FixtureAiSeatView beforeDone = ViewAt(seat, RemnantAvailableTick - 1);
            Assert.Equal(1, beforeDone.CurrentTier);
            Assert.Equal(RemnantAvailableTick, beforeDone.TierResearchCompleteTick);
            FixtureAiSeatView afterDone = ViewAt(seat, RemnantAvailableTick);
            Assert.Equal(2, afterDone.CurrentTier);
            Assert.Equal(-1, afterDone.TierResearchCompleteTick);
            IAiUnitCatalog catalog = Catalog();
            string armoredId = FirstArmored(seat, catalog);
            Assert.False(string.IsNullOrEmpty(armoredId));
            Assert.False(seat.AllowsUnitForTier(armoredId, beforeDone.CurrentTier, catalog));
            Assert.True(seat.AllowsUnitForTier(armoredId, afterDone.CurrentTier, catalog));

            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 2);
            harness.SetBattalions();

            for (int tick = 0; tick < RemnantTicks; tick++)
            {
                int before = harness.Sink.Log.Count;
                harness.Tick(tick);
                AssertSinkRespectsSeat(harness.Sink, harness.SeatView, before, seat, tick, true);
                for (int i = before; i < harness.Sink.Log.Count; i++)
                {
                    AiIssuedCommand cmd = harness.Sink.Log[i];
                    if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                    {
                        continue;
                    }

                    Assert.True(catalog.TryGetTier(cmd.UnitId, out int tier));
                    if (tier >= 2)
                    {
                        Assert.True(tick >= RemnantAvailableTick);
                    }
                    else if (tick < RemnantAvailableTick)
                    {
                        Assert.True(tier < 2);
                    }
                }
            }

            Assert.True(seat.AllowsUnitForTier(armoredId, afterDone.CurrentTier, catalog));
        }

        [Fact]
        public void Sink_NeverRecordsAiCausedTierLockedOrPopCapReached()
        {
            RunCompactAndAssertNoIllegalReject();
            RunRemnantAndAssertNoIllegalReject();
        }

        [Fact]
        public void PopCapReached_SkipsRaiseAndBacksOff()
        {
            FixtureAiScenarioSeats seats = RemnantSeats();
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView seat));
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SeatView.PopUsed = 4;
            harness.SeatView.PopCap = 4;
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
            AiScenarioSeatView seat = new AiScenarioSeatView(
                0,
                "remnant_command",
                null,
                1UL,
                300,
                FixtureAiScenarioSeats.RemnantRaisableUnits,
                FixtureAiScenarioSeats.DefaultBuildableBuildings,
                new[] { new AiAutoResearch("tier2", RemnantStartTick) });
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Tier2ResearchTicks = tables.Tier2ResearchTicks;
            view.Refresh(seat, RemnantStartTick - 1);
            Assert.Equal(1, view.CurrentTier);
            Assert.Equal(-1, view.TierResearchCompleteTick);
            view.Refresh(seat, RemnantStartTick);
            Assert.Equal(1, view.CurrentTier);
            Assert.Equal(RemnantStartTick + tables.Tier2ResearchTicks, view.TierResearchCompleteTick);
            view.Refresh(seat, RemnantStartTick + tables.Tier2ResearchTicks);
            Assert.Equal(2, view.CurrentTier);
            Assert.Equal(-1, view.TierResearchCompleteTick);
        }

        private static void RunCompactAndAssertNoIllegalReject()
        {
            FixtureAiScenarioSeats seats = CompactSeats();
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.SetBattalions(
                AiMvpAttackTests.PinWithRole(5, 0, 0, 1, AiConstants.RoleLineInfantry),
                AiMvpAttackTests.PinWithRole(7, 0, 0, 1, AiConstants.RoleEngineer));
            for (int tick = 0; tick < CompactTicks; tick++)
            {
                harness.Tick(tick);
            }

            AssertNoAiCausedTierOrPopReject(harness);
        }

        private static void RunRemnantAndAssertNoIllegalReject()
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

            AssertNoAiCausedTierOrPopReject(harness);
        }

        private static void AssertNoAiCausedTierOrPopReject(AiMvpAttackTests.Harness harness)
        {
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                AiCommandReject reject = harness.Sink.RejectOf(cmd.Id);
                Assert.NotEqual(AiCommandReject.TierLocked, reject);
                Assert.NotEqual(AiCommandReject.PopCapReached, reject);
                if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                {
                    continue;
                }

                Assert.True(harness.Seat.AllowsUnitForTier(
                    cmd.UnitId,
                    harness.SeatView.CurrentTier,
                    harness.Catalog));

                if (harness.SeatView.PopCap > 0)
                {
                    Assert.True(harness.SeatView.PopUsed < harness.SeatView.PopCap);
                }
            }
        }

        private static void AssertSinkRespectsSeat(
            RecordingAiCommandSink sink,
            IAiSeatView view,
            int from,
            AiScenarioSeatView seat,
            int tick,
            bool allowTierIIAfterAvailable)
        {
            for (int i = from; i < sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = sink.Log[i];
                Assert.Equal(tick, cmd.Tick);
                Assert.NotEqual(AiCommandReject.TierLocked, sink.RejectOf(cmd.Id));
                Assert.NotEqual(AiCommandReject.PopCapReached, sink.RejectOf(cmd.Id));
                if (cmd.Kind == AiIssuedKind.RaiseBattalion)
                {
                    IAiUnitCatalog catalog = Catalog();
                    Assert.True(seat.AllowsUnitForTier(cmd.UnitId, view.CurrentTier, catalog));
                    Assert.True(catalog.TryGetTier(cmd.UnitId, out int tier));
                    if (!allowTierIIAfterAvailable || view.CurrentTier < 2)
                    {
                        Assert.True(tier < 2);
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

        private static FixtureAiSeatView ViewAt(AiScenarioSeatView seat, int tick)
        {
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Tier2ResearchTicks = RemnantDurationTicks;
            view.Refresh(seat, tick);
            return view;
        }

        private static IAiUnitCatalog Catalog()
        {
            return AiBalanceTablesTests.LoadBalance().UnitCatalog;
        }

        private static bool HasRaisableRole(
            AiScenarioSeatView seat,
            IAiUnitCatalog catalog,
            string role,
            int currentTier)
        {
            string[] ids = seat.RaisableUnits;
            for (int i = 0; i < ids.Length; i++)
            {
                if (!seat.AllowsUnitForTier(ids[i], currentTier, catalog))
                {
                    continue;
                }

                if (catalog.TryGetRole(ids[i], out string have) && have == role)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRaisableAboveTier(
            AiScenarioSeatView seat,
            IAiUnitCatalog catalog,
            int maxTier)
        {
            string[] ids = seat.RaisableUnits;
            for (int i = 0; i < ids.Length; i++)
            {
                if (catalog.TryGetTier(ids[i], out int tier) && tier > maxTier)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FirstArmored(AiScenarioSeatView seat, IAiUnitCatalog catalog)
        {
            return AiMvpAttackTests.FirstRoleOrTarget(
                seat,
                catalog,
                null,
                AiConstants.TargetClassArmored);
        }

        private static FixtureAiScenarioSeats CompactSeats()
        {
            return FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        0,
                        "homestead_compact",
                        "homestead_compact_settler",
                        1UL,
                        300,
                        FixtureAiScenarioSeats.DefaultRaisableUnits,
                        new[] { "supply_depot", "town_walls_l1" },
                        Array.Empty<AiMvpAutoResearchItem>())
                },
                new AiMvpMatchRulesShape(false, "draw"));
        }

        private static FixtureAiScenarioSeats RemnantSeats()
        {
            return FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        0,
                        "remnant_command",
                        null,
                        1UL,
                        300,
                        FixtureAiScenarioSeats.RemnantRaisableUnits,
                        new[] { "supply_depot" },
                        new[] { new AiMvpAutoResearchItem("tier2", RemnantStartTick) })
                },
                new AiMvpMatchRulesShape(false, "draw"));
        }
    }
}
