using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiRaiseMixTests
    {
        [Fact]
        public void Compact_RaisesAntiArmourAfterSeeingEnemyArmour()
        {
            AiMvpAttackTests.Harness harness = CombatHarness();
            harness.SeatView.HasSeenEnemyArmourOrMech = true;
            harness.SetBattalions(AiMvpAttackTests.PinWithRole(5, 1000, 0, 1, AiConstants.RoleLineInfantry, true));
            string raised = RaiseOnce(harness);
            Assert.True(harness.Catalog.TryGetRole(raised, out string role));
            Assert.Equal(AiConstants.RoleAntiTank, role);
            Assert.True(harness.Catalog.TryGetTargetClass(EnemyArmoredId(harness), out string seenClass));
            Assert.Equal(AiConstants.TargetClassArmored, seenClass);
        }

        [Fact]
        public void Compact_MixFollowsLineUnitsPerAntiArmourBeforeSeeingArmour()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            int ratio = tables.GetPersonality(AiConstants.PersonalitySettler).LineUnitsPerAntiArmour;
            Assert.Equal(2, ratio);
            AiScenarioSeatView seat = CompactSeat();
            IAiUnitCatalog catalog = tables.UnitCatalog;
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Manpower = seat.StartingManpower;
            view.HasSeenEnemyArmourOrMech = false;

            AssertRole(Select(seat, catalog, view, ratio, System.Array.Empty<AiBattalionPin>()), catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(seat, catalog, view, ratio, Pins(1, 0)), catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(seat, catalog, view, ratio, Pins(2, 0)), catalog, AiConstants.RoleAntiTank);
            AssertRole(Select(seat, catalog, view, ratio, Pins(2, 1)), catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(seat, catalog, view, ratio, Pins(3, 1)), catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(seat, catalog, view, ratio, Pins(4, 1)), catalog, AiConstants.RoleAntiTank);
        }

        [Fact]
        public void Militia_OnlyFillsGarrisonWhenManpowerCannotAffordLine()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiScenarioSeatView seat = CompactSeat();
            IAiUnitCatalog catalog = tables.UnitCatalog;
            int ratio = tables.GetPersonality(AiConstants.PersonalitySettler).LineUnitsPerAntiArmour;
            string lineId = AiMvpAttackTests.FirstRoleOrTarget(seat, catalog, AiConstants.RoleLineInfantry, null);
            Assert.True(catalog.TryGetManpowerCost(lineId, out int lineCost));
            FixtureAiSeatView view = new FixtureAiSeatView();

            view.Manpower = lineCost - 1;
            AssertRole(
                AiAttackDefend.SelectRaiseUnit(seat, catalog, view, System.Array.Empty<AiBattalionPin>(), 0, ratio, false, true),
                catalog,
                AiConstants.RoleGarrisonInfantry);

            view.Manpower = lineCost;
            AssertRole(
                AiAttackDefend.SelectRaiseUnit(seat, catalog, view, System.Array.Empty<AiBattalionPin>(), 0, ratio, false, true),
                catalog,
                AiConstants.RoleLineInfantry);

            view.Manpower = lineCost - 1;
            AssertRole(
                AiAttackDefend.SelectRaiseUnit(seat, catalog, view, System.Array.Empty<AiBattalionPin>(), 0, ratio, false, false),
                catalog,
                AiConstants.RoleLineInfantry);
        }

        [Fact]
        public void Remnant_NeverRaisesArmoredBeforeCurrentTierTwo()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiScenarioSeatView seat = RemnantSeat();
            IAiUnitCatalog catalog = tables.UnitCatalog;
            int ratio = tables.GetPersonality(AiConstants.PersonalityWarlord).LineUnitsPerAntiArmour;
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.CurrentTier = 1;
            view.Manpower = seat.StartingManpower;

            string picked = AiAttackDefend.SelectRaiseUnit(
                seat,
                catalog,
                view,
                System.Array.Empty<AiBattalionPin>(),
                0,
                ratio,
                false,
                false);
            Assert.True(catalog.TryGetTier(picked, out int pickedTier));
            Assert.True(pickedTier < 2);
            Assert.True(catalog.TryGetRole(picked, out string pickedRole));
            Assert.Equal(AiConstants.RoleLineInfantry, pickedRole);

            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.LineUnitsPerAntiArmour = ratio;
            harness.MinGarrisonPerTown = 0;
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 2);
            harness.SetBattalions();
            harness.SeatView.CurrentTier = 1;
            harness.Tick(0);
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                {
                    continue;
                }

                Assert.True(catalog.TryGetTier(cmd.UnitId, out int tier));
                Assert.True(tier < 2);
                if (catalog.TryGetTargetClass(cmd.UnitId, out string targetClass))
                {
                    Assert.NotEqual(AiConstants.TargetClassArmored, targetClass);
                }
            }

            view.CurrentTier = 2;
            Assert.True(seat.AllowsUnitForTier(
                FirstArmored(seat, catalog),
                view.CurrentTier,
                catalog));
        }

        [Fact]
        public void Raise_NeverIssuesTierLockedOrUnitNotRaisable()
        {
            RunAndAssertNoIllegalReject(CompactSeat(), false);
            RunAndAssertNoIllegalReject(RemnantSeat(), true);
        }

        [Fact]
        public void Engineer_OnlySelectedForDepot()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiScenarioSeatView seat = CompactSeat();
            IAiUnitCatalog catalog = tables.UnitCatalog;
            int ratio = tables.GetPersonality(AiConstants.PersonalitySettler).LineUnitsPerAntiArmour;
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Manpower = seat.StartingManpower;
            AssertRole(
                AiAttackDefend.SelectRaiseUnit(seat, catalog, view, System.Array.Empty<AiBattalionPin>(), 0, ratio, true, false),
                catalog,
                AiConstants.RoleEngineer);
            AssertRole(
                AiAttackDefend.SelectRaiseUnit(seat, catalog, view, System.Array.Empty<AiBattalionPin>(), 0, ratio, false, false),
                catalog,
                AiConstants.RoleLineInfantry);
        }

        private static void RunAndAssertNoIllegalReject(AiScenarioSeatView seat, bool remnant)
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = seat;
            harness.SupplyReachCm = remnant ? 100000 : 1500;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, remnant ? 1 : 0, remnant ? 2 : 0);
            if (!remnant)
            {
                harness.SetBattalions(
                    AiMvpAttackTests.PinWithRole(5, 0, 0, 1, AiConstants.RoleLineInfantry),
                    AiMvpAttackTests.PinWithRole(7, 0, 0, 1, AiConstants.RoleEngineer));
            }
            else
            {
                harness.SetBattalions();
            }

            int ticks = remnant ? 80 : 40;
            for (int tick = 0; tick < ticks; tick++)
            {
                harness.Tick(tick);
            }

            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                AiCommandReject reject = harness.Sink.RejectOf(cmd.Id);
                Assert.NotEqual(AiCommandReject.TierLocked, reject);
                Assert.NotEqual(AiCommandReject.UnitNotRaisable, reject);
                if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                {
                    continue;
                }

                Assert.True(harness.Seat.AllowsUnit(cmd.UnitId));
                Assert.True(harness.Seat.AllowsUnitForTier(cmd.UnitId, harness.SeatView.CurrentTier, harness.Catalog));
            }
        }

        private static AiMvpAttackTests.Harness CombatHarness()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = CompactSeat();
            harness.SupplyReachCm = 100000;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 4, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.SetBattalions();
            return harness;
        }

        private static string RaiseOnce(AiMvpAttackTests.Harness harness)
        {
            int before = harness.Sink.Log.Count;
            harness.Tick(0);
            for (int i = before; i < harness.Sink.Log.Count; i++)
            {
                if (harness.Sink.Log[i].Kind == AiIssuedKind.RaiseBattalion)
                {
                    return harness.Sink.Log[i].UnitId;
                }
            }

            Assert.Fail("expected a raise");
            return string.Empty;
        }

        private static string Select(
            AiScenarioSeatView seat,
            IAiUnitCatalog catalog,
            IAiSeatView view,
            int ratio,
            AiBattalionPin[] pins)
        {
            return AiAttackDefend.SelectRaiseUnit(seat, catalog, view, pins, pins.Length, ratio, false, false);
        }

        private static AiBattalionPin[] Pins(int line, int at)
        {
            List<AiBattalionPin> pins = new List<AiBattalionPin>();
            int id = 1;
            for (int i = 0; i < line; i++)
            {
                pins.Add(AiMvpAttackTests.PinWithRole(id++, 0, 0, 1, AiConstants.RoleLineInfantry));
            }

            for (int i = 0; i < at; i++)
            {
                pins.Add(AiMvpAttackTests.PinWithRole(id++, 0, 0, 1, AiConstants.RoleAntiTank));
            }

            return pins.ToArray();
        }

        private static void AssertRole(string unitId, IAiUnitCatalog catalog, string role)
        {
            Assert.False(string.IsNullOrEmpty(unitId));
            Assert.True(catalog.TryGetRole(unitId, out string have));
            Assert.Equal(role, have);
        }

        private static string EnemyArmoredId(AiMvpAttackTests.Harness harness)
        {
            return FirstArmored(RemnantSeat(), harness.Catalog);
        }

        private static string FirstArmored(AiScenarioSeatView seat, IAiUnitCatalog catalog)
        {
            return AiMvpAttackTests.FirstRoleOrTarget(seat, catalog, null, AiConstants.TargetClassArmored);
        }

        private static AiScenarioSeatView CompactSeat()
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
                        new[] { "supply_depot" },
                        System.Array.Empty<AiMvpAutoResearchItem>())
                },
                new AiMvpMatchRulesShape(false, "draw")).SeatAt(0);
        }

        private static AiScenarioSeatView RemnantSeat()
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
                        new[] { new AiMvpAutoResearchItem("tier2", 40) })
                },
                new AiMvpMatchRulesShape(false, "draw")).SeatAt(0);
        }
    }
}
