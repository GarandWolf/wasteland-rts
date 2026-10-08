using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiRaiseMixTests
    {
        [Fact]
        public void Compact_StartingMilitiaScoutsEngineers_DoNotCountAsMixLine()
        {
            MixContext ctx = CompactUnseen();
            List<AiBattalionPin> pins = new List<AiBattalionPin>
            {
                new AiBattalionPin(1, 0, 0, 1, "compact_militia", false),
                new AiBattalionPin(2, 0, 0, 1, "compact_militia", false),
                new AiBattalionPin(3, 0, 0, 1, "compact_militia", false),
                new AiBattalionPin(4, 0, 0, 1, "compact_militia", false),
                new AiBattalionPin(5, 0, 0, 1, "compact_scouts", false),
                new AiBattalionPin(6, 0, 0, 1, "compact_engineers", false)
            };
            Assert.False(AiAttackDefend.CountsAsMixRifle(ctx.Catalog, "compact_militia"));
            Assert.False(AiAttackDefend.CountsAsMixRifle(ctx.Catalog, "compact_scouts"));
            Assert.False(AiAttackDefend.CountsAsMixRifle(ctx.Catalog, "compact_engineers"));
            AssertRole(Select(ctx, pins.ToArray()), ctx.Catalog, AiConstants.RoleLineInfantry);
            pins.Add(PinFor(FirstRole(ctx, AiConstants.RoleLineInfantry), 7));
            AssertRole(Select(ctx, pins.ToArray()), ctx.Catalog, AiConstants.RoleLineInfantry);
            pins.Add(PinFor(FirstRole(ctx, AiConstants.RoleLineInfantry), 8));
            AssertRole(Select(ctx, pins.ToArray()), ctx.Catalog, AiConstants.RoleAntiTank);
            pins.Add(PinFor(FirstRole(ctx, AiConstants.RoleAntiTank), 9));
            AssertRole(Select(ctx, pins.ToArray()), ctx.Catalog, AiConstants.RoleLineInfantry);
            pins.Add(PinFor(FirstRole(ctx, AiConstants.RoleLineInfantry), 10));
            AssertRole(Select(ctx, pins.ToArray()), ctx.Catalog, AiConstants.RoleLineInfantry);
            pins.Add(PinFor(FirstRole(ctx, AiConstants.RoleLineInfantry), 11));
            AssertRole(Select(ctx, pins.ToArray()), ctx.Catalog, AiConstants.RoleAntiTank);
        }

        [Fact]
        public void Compact_FirstThreeRaises_IncludeAntiTankWhenArmourUnseen()
        {
            MixContext ctx = CompactUnseen();
            int at = 0;
            List<AiBattalionPin> pins = new List<AiBattalionPin>();
            for (int i = 0; i < 3; i++)
            {
                string raised = Select(ctx, pins.ToArray());
                Assert.True(ctx.Catalog.TryGetRole(raised, out string role));
                if (role == AiConstants.RoleAntiTank)
                {
                    at++;
                }

                pins.Add(PinFor(raised, pins.Count + 1));
            }

            Assert.True(at >= 1);
        }

        [Fact]
        public void Compact_MixRatioHoldsOverRaises_WhenArmourUnseen()
        {
            MixContext ctx = CompactUnseen();
            Assert.Equal(2, ctx.Ratio);
            const int n = 12;
            int line = 0;
            int at = 0;
            for (int i = 0; i < n; i++)
            {
                string raised = Select(ctx, Pins(line, at));
                Assert.True(ctx.Catalog.TryGetRole(raised, out string role));
                if (role == AiConstants.RoleAntiTank)
                {
                    at++;
                }
                else
                {
                    Assert.Equal(AiConstants.RoleLineInfantry, role);
                    line++;
                }

                Assert.Equal((i + 1) / (ctx.Ratio + 1), at);
                Assert.True(line <= ctx.Ratio * (at + 1));
                Assert.True(at * ctx.Ratio <= line);
            }

            Assert.Equal(n / (ctx.Ratio + 1), at);
            Assert.Equal(n - at, line);
        }

        [Fact]
        public void Compact_FrontsDueAntiTankOnly_WhenArmourSeen()
        {
            MixContext unseen = CompactUnseen();
            MixContext seen = CompactSeen();
            AssertRole(Select(unseen, Pins(1, 0)), unseen.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(seen, Pins(1, 0)), seen.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(unseen, Pins(2, 0)), unseen.Catalog, AiConstants.RoleAntiTank);
            AssertRole(Select(seen, Pins(2, 0)), seen.Catalog, AiConstants.RoleAntiTank);
            AssertRole(Select(seen, Pins(4, 2)), seen.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(seen, Pins(5, 2)), seen.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(seen, Pins(4, 1)), seen.Catalog, AiConstants.RoleAntiTank);
            Assert.True(seen.Catalog.TryGetTargetClass(FirstArmored(RemnantSeat(), seen.Catalog), out string seenClass));
            Assert.Equal(AiConstants.TargetClassArmored, seenClass);
        }

        [Fact]
        public void Compact_FromFourLineTwoAt_RaisesLineLineAntiTank()
        {
            MixContext ctx = CompactUnseen();
            AssertRole(Select(ctx, Pins(4, 2)), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(5, 2)), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(6, 2)), ctx.Catalog, AiConstants.RoleAntiTank);
        }

        [Fact]
        public void Compact_LostAntiTank_MakesAntiTankDueAgain()
        {
            MixContext ctx = CompactUnseen();
            AssertRole(Select(ctx, Pins(4, 2)), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(4, 1)), ctx.Catalog, AiConstants.RoleAntiTank);
        }

        [Fact]
        public void Compact_DoesNotExceedRatio_WhenArmourSeenAndRatioMet()
        {
            MixContext ctx = CompactSeen();
            AssertRole(Select(ctx, System.Array.Empty<AiBattalionPin>()), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(2, 1)), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(4, 2)), ctx.Catalog, AiConstants.RoleLineInfantry);
        }

        [Fact]
        public void Compact_MixFollowsLineUnitsPerAntiArmourBeforeSeeingArmour()
        {
            MixContext ctx = CompactUnseen();
            Assert.Equal(2, ctx.Ratio);
            AssertRole(Select(ctx, System.Array.Empty<AiBattalionPin>()), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(1, 0)), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(2, 0)), ctx.Catalog, AiConstants.RoleAntiTank);
            AssertRole(Select(ctx, Pins(2, 1)), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(3, 1)), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(4, 1)), ctx.Catalog, AiConstants.RoleAntiTank);
        }

        [Fact]
        public void Compact_QueuedAntiTank_DoesNotRaiseAnotherUntilLineEarnsIt()
        {
            MixContext ctx = CompactUnseen();
            string atId = FirstRole(ctx, AiConstants.RoleAntiTank);
            AssertRole(Select(ctx, Pins(2, 0), new[] { atId }), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(3, 0), new[] { atId }), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(4, 0), new[] { atId }), ctx.Catalog, AiConstants.RoleAntiTank);
        }

        [Fact]
        public void Compact_QueuedLine_MakesAntiTankDueAtRatio()
        {
            MixContext ctx = CompactUnseen();
            string lineId = FirstRole(ctx, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, System.Array.Empty<AiBattalionPin>(), new[] { lineId }), ctx.Catalog, AiConstants.RoleLineInfantry);
            AssertRole(Select(ctx, Pins(1, 0), new[] { lineId }), ctx.Catalog, AiConstants.RoleAntiTank);
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

        private static MixContext CompactUnseen()
        {
            return CompactMix(false);
        }

        private static MixContext CompactSeen()
        {
            return CompactMix(true);
        }

        private static MixContext CompactMix(bool seenArmour)
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiScenarioSeatView seat = CompactSeat();
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Manpower = seat.StartingManpower;
            view.HasSeenEnemyArmourOrMech = seenArmour;
            return new MixContext(
                seat,
                tables.UnitCatalog,
                view,
                tables.GetPersonality(AiConstants.PersonalitySettler).LineUnitsPerAntiArmour);
        }

        private static string Select(MixContext ctx, AiBattalionPin[] pins)
        {
            return Select(ctx, pins, null);
        }

        private static string Select(MixContext ctx, AiBattalionPin[] pins, string[]? queued)
        {
            int queuedCount = queued == null ? 0 : queued.Length;
            return AiAttackDefend.SelectRaiseUnit(
                ctx.Seat,
                ctx.Catalog,
                ctx.View,
                pins,
                pins.Length,
                ctx.Ratio,
                false,
                false,
                queued,
                queuedCount,
                null,
                0);
        }

        private static string FirstRole(MixContext ctx, string role)
        {
            return AiMvpAttackTests.FirstRoleOrTarget(ctx.Seat, ctx.Catalog, role, null);
        }

        private static AiBattalionPin PinFor(string unitId, int id)
        {
            return new AiBattalionPin(id, 0, 0, 1, unitId, false);
        }

        private readonly struct MixContext
        {
            public readonly AiScenarioSeatView Seat;
            public readonly IAiUnitCatalog Catalog;
            public readonly FixtureAiSeatView View;
            public readonly int Ratio;

            public MixContext(
                AiScenarioSeatView seat,
                IAiUnitCatalog catalog,
                FixtureAiSeatView view,
                int ratio)
            {
                Seat = seat;
                Catalog = catalog;
                View = view;
                Ratio = ratio;
            }
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
