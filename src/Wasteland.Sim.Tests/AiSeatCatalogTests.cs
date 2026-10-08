using System;
using Wasteland.Sim.Ai;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiSeatCatalogTests
    {
        private const int CompactTicks = 240;
        private const int RemnantUnlockTick = 40;
        private const int RemnantTicks = 80;

        [Fact]
        public void CompactStyleSeat_EmptyResearch_ExcludesTierIITierIIIAndFortAboveL1()
        {
            FixtureAiScenarioSeats seats = CompactSeats();
            Assert.False(seats.FuelEnabled);
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            Assert.True(seat.AllowsUnit(AiConstants.UnitKindInfantry, 0));
            Assert.True(seat.AllowsUnit(AiConstants.UnitKindEngineer, 0));
            Assert.False(seat.AllowsUnit(AiConstants.UnitKindTierII, 0));
            Assert.False(seat.AllowsUnit(AiConstants.UnitKindTierII, 10000));
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
                AssertSinkRespectsSeat(harness.Sink, before, seat, tick, false);
            }
        }

        [Fact]
        public void RemnantStyleSeat_RaisesTierIIOnlyAtOrAfterUnlock()
        {
            FixtureAiScenarioSeats seats = RemnantSeats();
            Assert.False(seats.FuelEnabled);
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView seat));
            Assert.False(seat.AllowsUnit(AiConstants.UnitKindTierII, RemnantUnlockTick - 1));
            Assert.True(seat.AllowsUnit(AiConstants.UnitKindTierII, RemnantUnlockTick));

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
                AssertSinkRespectsSeat(harness.Sink, before, seat, tick, true);
                for (int i = before; i < harness.Sink.Log.Count; i++)
                {
                    AiIssuedCommand cmd = harness.Sink.Log[i];
                    if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                    {
                        continue;
                    }

                    if (cmd.Arg0 == AiConstants.UnitKindTierII)
                    {
                        Assert.True(tick >= RemnantUnlockTick);
                        sawTierII = true;
                    }
                    else if (tick < RemnantUnlockTick)
                    {
                        Assert.NotEqual(AiConstants.UnitKindTierII, cmd.Arg0);
                    }
                }
            }

            Assert.True(sawTierII);
        }

        private static void AssertSinkRespectsSeat(
            RecordingAiCommandSink sink,
            int from,
            AiScenarioSeatView seat,
            int tick,
            bool allowTierIIAfterUnlock)
        {
            for (int i = from; i < sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = sink.Log[i];
                Assert.Equal(tick, cmd.Tick);
                if (cmd.Kind == AiIssuedKind.RaiseBattalion)
                {
                    int unitKind = cmd.Arg0;
                    Assert.True(seat.AllowsUnit(unitKind, tick));
                    Assert.NotEqual(AiConstants.UnitKindTierIII, unitKind);
                    if (!allowTierIIAfterUnlock)
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
                new AiMvpMatchRulesShape(false, "draw"));
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
                        new[] { new AiMvpAutoResearchItem("tier2", RemnantUnlockTick) })
                },
                new AiMvpMatchRulesShape(false, "draw"));
        }
    }
}
