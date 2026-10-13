using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Events;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AutoResearchTests
    {
        private const int Remnant = 1;
        private const int Compact = 2;
        private const string TierTwo = "tier2";
        private const string LineUnit = "remnant_motor_rifles";
        private const string TierTwoUnit = "remnant_mech_battalion";

        [Fact]
        public void TechCatalog_ReadsResearchTicksAndCost()
        {
            BalanceCatalog catalog = Catalog();
            int ticks;
            int manpower;
            int fuel;
            int materiel;
            Assert.True(catalog.TryGetTechResearchTicks(TierTwo, out ticks));
            Assert.Equal(1800, ticks);
            Assert.True(catalog.TryGetTechCost(TierTwo, out manpower, out fuel, out materiel));
            Assert.Equal(0, manpower);
            Assert.Equal(200, fuel);
            Assert.Equal(300, materiel);
            Assert.Equal(400, catalog.StartingFuel);
            Assert.Equal(500, catalog.StartingMateriel);
            Assert.False(catalog.TryGetTechResearchTicks("no_such_tech", out ticks));
            Assert.False(catalog.TryGetTechCost("no_such_tech", out manpower, out fuel, out materiel));
        }

        [Fact]
        public void AutoResearch_StartsAtNotBeforeTick_AndCompletesAfterDuration()
        {
            BalanceCatalog catalog = Catalog();
            int duration = ResearchTicks(catalog);
            const int notBefore = 5;
            SimWorld world = WorldWithResearch(notBefore, manpower: 1000, materials: 1000, fuel: 0, fuelEnabled: false);

            StepUntil(world, notBefore);
            Assert.True(world.TryGetSeat(Remnant, out SeatView waiting));
            Assert.Equal(1, waiting.CurrentTier);
            Assert.Equal(-1, waiting.TierResearchCompleteTick);
            Assert.Equal(1000, waiting.Materials);
            Assert.Equal(0, world.FuelOf(Remnant));

            world.Step();
            SimEvent started = Single(world, SimEventKind.TierResearchStarted, Remnant);
            Assert.Equal(notBefore, started.Tick);
            Assert.Equal(2, started.AuxId);
            Assert.Equal(notBefore, started.Param0);
            Assert.Equal(notBefore + duration, started.Param1);
            Assert.True(world.TryGetSeat(Remnant, out SeatView running));
            Assert.Equal(1, running.CurrentTier);
            Assert.Equal(notBefore + duration, running.TierResearchCompleteTick);
            Assert.Equal(1000 - 300, running.Materials);
            Assert.Equal(1000, running.Manpower);
            Assert.Equal(0, world.FuelOf(Remnant));

            StepUntil(world, notBefore + duration);
            Assert.True(world.TryGetSeat(Remnant, out SeatView beforeComplete));
            Assert.Equal(1, beforeComplete.CurrentTier);
            Assert.Equal(notBefore + duration, beforeComplete.TierResearchCompleteTick);

            world.Step();
            SimEvent reached = Single(world, SimEventKind.TierReached, Remnant);
            Assert.Equal(notBefore + duration, reached.Tick);
            Assert.Equal(2, reached.AuxId);
            Assert.True(world.TryGetSeat(Remnant, out SeatView done));
            Assert.Equal(2, done.CurrentTier);
            Assert.Equal(-1, done.TierResearchCompleteTick);
        }

        [Fact]
        public void TierLockedRaise_RejectedBeforeTierReached_AcceptedAfter()
        {
            BalanceCatalog catalog = Catalog();
            int duration = ResearchTicks(catalog);
            SimWorld world = WorldWithResearch(0, 1000, 1000, 0, false, TierTwoUnit);
            world.SubmitRaiseBattalion(Remnant, 7, TierTwoUnit, 0, 0);
            world.Step();

            Assert.Equal(SimEventKind.TierResearchStarted, world.Events[0].Kind);
            Assert.True(world.TryGetRejection(7, out CommandRejectReason reason, out int required, out int have));
            Assert.Equal(CommandRejectReason.TierLocked, reason);
            Assert.Equal(2, required);
            Assert.Equal(1, have);
            Assert.Equal(0, world.BattalionCount);

            StepUntil(world, duration);
            world.SubmitRaiseBattalion(Remnant, 8, TierTwoUnit, 1, 2);
            world.Step();

            Assert.Equal(duration + 1, world.Tick);
            SimEvent reached = Single(world, SimEventKind.TierReached, Remnant);
            Assert.Equal(duration, reached.Tick);
            Assert.Equal(2, reached.AuxId);
            int reachedAt = IndexOf(world, SimEventKind.TierReached, Remnant);
            int raisedAt = IndexOf(world, SimEventKind.BattalionRaiseStarted, 8);
            Assert.True(reachedAt < raisedAt);
            Assert.Equal(duration, world.Events[raisedAt].Tick);
            Assert.False(world.TryGetRejection(8, out CommandRejectReason later, out int unused0, out int unused1));
            Assert.Equal(CommandRejectReason.None, later);
            Assert.Equal(0, unused0);
            Assert.Equal(0, unused1);
            Assert.True(world.TryGetSeat(Remnant, out SeatView seat));
            Assert.Equal(2, seat.CurrentTier);
        }

        [Fact]
        public void UnaffordableResearch_DefersThenStartsWhenTheFullCostIsOnHand()
        {
            BalanceCatalog catalog = Catalog();
            int duration = ResearchTicks(catalog);
            const int notBefore = 2;
            SimWorld world = WorldWithResearch(notBefore, manpower: 1000, materials: 299, fuel: 0, fuelEnabled: false);

            StepUntil(world, 8);
            Assert.True(world.TryGetSeat(Remnant, out SeatView waiting));
            Assert.Equal(1, waiting.CurrentTier);
            Assert.Equal(-1, waiting.TierResearchCompleteTick);
            Assert.Equal(299, waiting.Materials);
            Assert.Equal(0, Count(world, SimEventKind.TierResearchStarted, Remnant));

            world.AddResources(Remnant, 0, 1, 0);
            world.Step();
            SimEvent started = Single(world, SimEventKind.TierResearchStarted, Remnant);
            Assert.Equal(8, started.Tick);
            Assert.Equal(8 + duration, started.Param1);
            Assert.True(world.TryGetSeat(Remnant, out SeatView running));
            Assert.Equal(0, running.Materials);
            Assert.Equal(1000, running.Manpower);
            Assert.Equal(0, world.FuelOf(Remnant));

            SimWorld fuelShort = WorldWithResearch(0, 1000, 1000, 199, fuelEnabled: true);
            fuelShort.Step();
            Assert.Equal(0, Count(fuelShort, SimEventKind.TierResearchStarted, Remnant));
            Assert.True(fuelShort.TryGetSeat(Remnant, out SeatView held));
            Assert.Equal(1000, held.Materials);
            Assert.Equal(199, fuelShort.FuelOf(Remnant));
            fuelShort.AddResources(Remnant, 0, 0, 1);
            fuelShort.Step();
            SimEvent fuelStarted = Single(fuelShort, SimEventKind.TierResearchStarted, Remnant);
            Assert.Equal(1, fuelStarted.Tick);
            Assert.Equal(1000 - 300, SeatOf(fuelShort).Materials);
            Assert.Equal(0, fuelShort.FuelOf(Remnant));
        }

        [Fact]
        public void TwoSeats_StartInFactionIdOrder()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = NewWorld();
            world.BindCatalog(catalog);
            world.BindSeat(ResearchSeat(Compact, "homestead_compact", 0, 1000, 1000, 0, false, "compact_militia"));
            world.BindSeat(ResearchSeat(Remnant, "remnant_command", 0, 1000, 1000, 0, false, LineUnit));
            world.Step();

            int first = IndexOf(world, SimEventKind.TierResearchStarted, Remnant);
            int second = IndexOf(world, SimEventKind.TierResearchStarted, Compact);
            Assert.True(first >= 0);
            Assert.True(second > first);
            Assert.Equal(0, world.Events[first].Tick);
            Assert.Equal(0, world.Events[second].Tick);
            Assert.Equal(2, world.Events[first].AuxId);
            Assert.Equal(2, world.Events[second].AuxId);
        }

        [Fact]
        public void NextTierPrerequisite_HoldsTheCursor()
        {
            SimWorld world = NewWorld();
            world.BindCatalog(Catalog());
            world.BindSeat(new SeatSetup(
                Remnant,
                Remnant,
                "remnant_command",
                7,
                0,
                0,
                2,
                -1,
                1000,
                1000,
                0,
                false,
                8,
                new[] { LineUnit },
                new[] { new ScenarioAutoResearch(TierTwo, 0) }));
            world.Step();
            world.Step();
            Assert.Equal(0, Count(world, SimEventKind.TierResearchStarted, Remnant));
            Assert.True(world.TryGetSeat(Remnant, out SeatView seat));
            Assert.Equal(2, seat.CurrentTier);
            Assert.Equal(-1, seat.TierResearchCompleteTick);
            Assert.Equal(1000, seat.Materials);
        }

        [Fact]
        public void Replay_HashIdenticalEveryTick()
        {
            List<ulong> first = RunReplay();
            List<ulong> second = RunReplay();
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i], second[i]);
            }

            Assert.True(first.Count > 1800);
            Assert.NotEqual(first[0], first[4]);
            Assert.NotEqual(first[4], first[5]);
        }

        [Fact]
        public void MvpGorge_RemnantReachesTier2_CompactStaysTier1()
        {
            string root = GoldenMapFiles.FindRepoRoot();
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(Path.Combine(root, "data", "balance"));
            byte[] map = GoldenMapFiles.ReadWrmb(
                Path.Combine(
                    GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName),
                    GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
            SimScenario scenario = SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                catalog,
                map,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");

            ScenarioSeat remnant = scenario.SeatAt(0);
            ScenarioSeat compact = scenario.SeatAt(1);
            Assert.Equal("remnant_command", remnant.Faction);
            Assert.Equal("homestead_compact", compact.Faction);
            Assert.Single(remnant.AutoResearch);
            Assert.Equal(TierTwo, remnant.AutoResearch[0].Item);
            Assert.Equal(1200, remnant.AutoResearch[0].NotBeforeTick);
            Assert.Empty(compact.AutoResearch);
            Assert.False(scenario.MatchRules.FuelEnabled);

            int duration;
            int manpowerCost;
            int fuelCost;
            int materielCost;
            Assert.True(catalog.TryGetTechResearchTicks(TierTwo, out duration));
            Assert.Equal(1800, duration);
            Assert.True(catalog.TryGetTechCost(TierTwo, out manpowerCost, out fuelCost, out materielCost));
            Assert.Equal(0, manpowerCost);
            Assert.Equal(200, fuelCost);
            Assert.Equal(300, materielCost);
            Assert.True(catalog.StartingMateriel >= materielCost);

            SimWorld world = NewWorld();
            world.BindCatalog(catalog);
            world.BindSeat(FromScenario(compact, scenario, catalog));
            world.BindSeat(FromScenario(remnant, scenario, catalog));

            int startTick = remnant.AutoResearch[0].NotBeforeTick;
            int reachedTick = startTick + duration;
            Assert.Equal(3000, reachedTick);

            StepUntil(world, startTick);
            Assert.Equal(1, SeatOf(world, remnant.Slot).CurrentTier);
            Assert.Equal(1, SeatOf(world, compact.Slot).CurrentTier);
            // Tick 1200 has not accrued yet: 59 intervals of HQ materiel 50.
            Assert.Equal(catalog.StartingMateriel + ((59 * 50) / 60), SeatOf(world, remnant.Slot).Materials);

            world.Step();
            SimEvent started = Single(world, SimEventKind.TierResearchStarted, remnant.Slot);
            Assert.Equal(startTick, started.Tick);
            Assert.Equal(2, started.AuxId);
            Assert.Equal(startTick, started.Param0);
            Assert.Equal(reachedTick, started.Param1);
            // The 60th interval pays one more materiel, then research charges.
            Assert.Equal(catalog.StartingMateriel + 50 - materielCost, SeatOf(world, remnant.Slot).Materials);
            Assert.Equal(catalog.StartingFuel + 45, world.FuelOf(remnant.Slot));
            Assert.Equal(remnant.StartingManpower + 10, SeatOf(world, remnant.Slot).Manpower);
            Assert.Equal(catalog.StartingMateriel + 50, SeatOf(world, compact.Slot).Materials);
            Assert.Equal(0, Count(world, SimEventKind.TierResearchStarted, compact.Slot));

            StepUntil(world, reachedTick);
            Assert.Equal(1, SeatOf(world, remnant.Slot).CurrentTier);
            world.Step();
            SimEvent reached = Single(world, SimEventKind.TierReached, remnant.Slot);
            Assert.Equal(reachedTick, reached.Tick);
            Assert.Equal(2, reached.AuxId);
            Assert.Equal(2, SeatOf(world, remnant.Slot).CurrentTier);
            Assert.Equal(-1, SeatOf(world, remnant.Slot).TierResearchCompleteTick);
            Assert.Equal(1, SeatOf(world, compact.Slot).CurrentTier);
            Assert.Equal(-1, SeatOf(world, compact.Slot).TierResearchCompleteTick);
            Assert.Equal(0, Count(world, SimEventKind.TierReached, compact.Slot));
        }

        private static List<ulong> RunReplay()
        {
            int duration = ResearchTicks(Catalog());
            const int notBefore = 4;
            SimWorld world = WorldWithResearch(notBefore, 1000, 1000, 0, false);
            List<ulong> hashes = new List<ulong>();
            int horizon = notBefore + duration + 2;
            for (int i = 0; i < horizon; i++)
            {
                hashes.Add(world.Step());
            }

            return hashes;
        }

        private static SimWorld WorldWithResearch(
            int notBefore,
            int manpower,
            int materials,
            int fuel,
            bool fuelEnabled,
            params string[] extraUnits)
        {
            SimWorld world = NewWorld();
            world.BindCatalog(Catalog());
            string[] units = new string[1 + extraUnits.Length];
            units[0] = LineUnit;
            for (int i = 0; i < extraUnits.Length; i++)
            {
                units[i + 1] = extraUnits[i];
            }

            world.BindSeat(ResearchSeat(Remnant, "remnant_command", notBefore, manpower, materials, fuel, fuelEnabled, units));
            return world;
        }

        private static SeatSetup ResearchSeat(
            int factionId,
            string factionKey,
            int notBefore,
            int manpower,
            int materials,
            int fuel,
            bool fuelEnabled,
            params string[] units)
        {
            return new SeatSetup(
                factionId,
                factionId,
                factionKey,
                7,
                0,
                0,
                1,
                -1,
                manpower,
                materials,
                fuel,
                fuelEnabled,
                8,
                units,
                new[] { new ScenarioAutoResearch(TierTwo, notBefore) });
        }

        private static SeatSetup FromScenario(ScenarioSeat seat, SimScenario scenario, BalanceCatalog catalog)
        {
            return new SeatSetup(
                seat.Slot,
                seat.Slot,
                seat.Faction,
                seat.HomeTownId,
                0,
                0,
                1,
                -1,
                seat.StartingManpower,
                catalog.StartingMateriel,
                catalog.StartingFuel,
                scenario.MatchRules.FuelEnabled,
                30,
                seat.RaisableUnits,
                seat.AutoResearch);
        }

        private static int ResearchTicks(BalanceCatalog catalog)
        {
            int ticks;
            Assert.True(catalog.TryGetTechResearchTicks(TierTwo, out ticks));
            return ticks;
        }

        private static SimWorld NewWorld()
        {
            return new SimWorld(new MatchConfig(42UL, 0));
        }

        private static BalanceCatalog Catalog()
        {
            return BalanceCatalog.LoadFromDirectory(Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance"));
        }

        private static void StepUntil(SimWorld world, int tick)
        {
            while (world.Tick < tick)
            {
                world.Step();
            }
        }

        private static SeatView SeatOf(SimWorld world)
        {
            return SeatOf(world, Remnant);
        }

        private static SeatView SeatOf(SimWorld world, int factionId)
        {
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
            return seat;
        }

        private static SimEvent Single(SimWorld world, SimEventKind kind, int subject)
        {
            int index = IndexOf(world, kind, subject);
            Assert.True(index >= 0);
            for (int i = index + 1; i < world.Events.Count; i++)
            {
                SimEvent ev = world.Events[i];
                Assert.False(ev.Kind == kind && ev.SubjectId == subject);
            }

            return world.Events[index];
        }

        private static int Count(SimWorld world, SimEventKind kind, int subject)
        {
            int count = 0;
            for (int i = 0; i < world.Events.Count; i++)
            {
                SimEvent ev = world.Events[i];
                if (ev.Kind == kind && ev.SubjectId == subject)
                {
                    count++;
                }
            }

            return count;
        }

        private static int IndexOf(SimWorld world, SimEventKind kind, int subject)
        {
            for (int i = 0; i < world.Events.Count; i++)
            {
                SimEvent ev = world.Events[i];
                if (ev.Kind == kind && ev.SubjectId == subject)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
