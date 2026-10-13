using System;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// Seat income is paid by the sim on the accrual interval, from HQ rates
    /// plus holding tiers in derived territory places.
    /// </summary>
    public sealed class SeatIncomeTests
    {
        private const int Remnant = AiConstants.DefaultAiFactionA;
        private const int Compact = AiConstants.DefaultAiFactionB;

        [Fact]
        public void Income_PaysOnTheAccrualInterval_AndNotBetween()
        {
            Ensure();
            SimWorld world = World();
            int manpowerPerMin = ManpowerPerMinute(world, Remnant);
            Assert.True(manpowerPerMin > 0);
            Assert.True(manpowerPerMin < _catalog.Capture.AccrualIntervalsPerMinute);

            SeatView start = Seat(world, Remnant);
            for (int i = 0; i < _catalog.Capture.AccrualIntervalTicks; i++)
            {
                world.Step();
            }

            Assert.Equal(_catalog.Capture.AccrualIntervalTicks, world.Tick);
            Assert.Equal(start.Manpower, Seat(world, Remnant).Manpower);
            Assert.Equal(start.Materials, Seat(world, Remnant).Materials);
            Assert.Equal(0, Carry(world, Remnant).Manpower);

            world.Step();
            Assert.Equal(_catalog.Capture.AccrualIntervalTicks + 1, world.Tick);
            Assert.Equal(start.Manpower, Seat(world, Remnant).Manpower);
            Assert.Equal(manpowerPerMin, Carry(world, Remnant).Manpower);

            int carry = Carry(world, Remnant).Manpower;
            world.Step();
            Assert.Equal(carry, Carry(world, Remnant).Manpower);
            Assert.Equal(start.Manpower, Seat(world, Remnant).Manpower);
        }

        [Fact]
        public void Income_SumsHqAndPlaceTiers_ForBothSeats()
        {
            Ensure();
            SimWorld world = World();
            CaptureBalance rules = _catalog.Capture;
            Assert.Equal(20, rules.AccrualIntervalTicks);
            Assert.Equal(60, rules.AccrualIntervalsPerMinute);
            Assert.False(rules.ContestedHoldingPaysIncome);

            TownView hood = Town(world, _snapshot.HoodRiverTownId);
            Assert.True(hood.IsSeatTown);
            Assert.True(_scenario.TryGetTerritory(hood.TerritoryKey, out ScenarioDerivedTerritory hoodRow));
            Assert.Equal("town", hoodRow.SeatTier);
            Assert.Equal(rules.TierIndex(hoodRow.SeatTier), hood.Tier);

            TownView dalles = Town(world, _snapshot.TheDallesTownId);
            Assert.True(_scenario.TryGetTerritory(dalles.TerritoryKey, out ScenarioDerivedTerritory dallesRow));
            int placeManpower = PlaceManpower(rules, dallesRow.Places);
            int classManpower = PopulationClassManpower(world, rules, dalles.TerritoryId);
            Assert.Equal(placeManpower, TierManpower(world, rules, dalles.TerritoryId));
            Assert.NotEqual(classManpower, placeManpower);
            Assert.Equal(10, PlacePopCap(rules, dallesRow.Places));

            int remnantManpower = ManpowerPerMinute(world, Remnant);
            int compactManpower = ManpowerPerMinute(world, Compact);
            int remnantMateriel = MaterielPerMinute(world, Remnant);
            int remnantFuel = FuelPerMinute(world, Remnant);
            int compactFuel = FuelPerMinute(world, Compact);
            Assert.Equal(32, remnantManpower);
            Assert.Equal(48, compactManpower);
            Assert.Equal(50, remnantMateriel);
            Assert.Equal(45, remnantFuel);
            Assert.Equal(20, compactFuel);

            SeatView remnantStart = Seat(world, Remnant);
            SeatView compactStart = Seat(world, Compact);
            int remnantFuelStart = world.FuelOf(Remnant);
            int compactFuelStart = world.FuelOf(Compact);
            int intervals = rules.AccrualIntervalsPerMinute;
            for (int i = 0; i < (rules.AccrualIntervalTicks * intervals) + 1; i++)
            {
                world.Step();
            }

            Assert.Equal(remnantStart.Manpower + remnantManpower, Seat(world, Remnant).Manpower);
            Assert.Equal(remnantStart.Materials + remnantMateriel, Seat(world, Remnant).Materials);
            Assert.Equal(remnantFuelStart + remnantFuel, world.FuelOf(Remnant));
            Assert.Equal(compactStart.Manpower + compactManpower, Seat(world, Compact).Manpower);
            Assert.Equal(compactStart.Materials + remnantMateriel, Seat(world, Compact).Materials);
            Assert.Equal(compactFuelStart + compactFuel, world.FuelOf(Compact));
            Assert.Equal(0, Carry(world, Remnant).Manpower);
            Assert.Equal(0, Carry(world, Compact).Materiel);
            Assert.False(world.FuelEnabledOf(Remnant));
            Assert.False(world.FuelEnabledOf(Compact));
        }

        [Fact]
        public void Income_SaveLoad_RestoresStockCarryAndLaterTicks()
        {
            Ensure();
            SimWorld paid = World();
            SimWorld twin = World();
            for (int i = 0; i < 41; i++)
            {
                paid.Step();
                twin.Step();
            }

            byte[] blob = paid.Save();
            Assert.Equal(SimWorld.WorldStateSchema, BitConverter.ToInt32(blob, 0));
            twin.AddResources(Remnant, 7, 9, 11);
            twin.Load(blob);
            Assert.Equal(paid.HashState(), twin.HashState());
            Assert.Equal(paid.Save(), twin.Save());
            Assert.Equal(Seat(paid, Remnant).Manpower, Seat(twin, Remnant).Manpower);
            Assert.Equal(Carry(paid, Remnant).Manpower, Carry(twin, Remnant).Manpower);
            Assert.Equal(Carry(paid, Compact).Fuel, Carry(twin, Compact).Fuel);

            SimWorld fresh = World();
            fresh.Load(blob);
            Assert.Equal(paid.Tick, fresh.Tick);
            Assert.Equal(Seat(paid, Remnant).Manpower, Seat(fresh, Remnant).Manpower);
            Assert.Equal(Seat(paid, Remnant).Materials, Seat(fresh, Remnant).Materials);
            Assert.Equal(paid.FuelOf(Remnant), fresh.FuelOf(Remnant));
            Assert.Equal(Carry(paid, Remnant).Manpower, Carry(fresh, Remnant).Manpower);
            Assert.Equal(Carry(paid, Compact).Materiel, Carry(fresh, Compact).Materiel);
            Assert.Equal(Carry(paid, Compact).Fuel, Carry(fresh, Compact).Fuel);
            Assert.False(fresh.FuelEnabledOf(Remnant));

            for (int i = 0; i < 40; i++)
            {
                Assert.Equal(paid.Step(), twin.Step());
            }
        }

        [Fact]
        public void CanAfford_RejectsMaterielAndFuelShortfalls()
        {
            IAiUnitCatalog catalog = AiBalanceTablesTests.LoadBalance().UnitCatalog;
            const string unitId = "remnant_motor_rifles";
            Assert.True(catalog.TryGetManpowerCost(unitId, out int manpower));
            Assert.True(catalog.TryGetMaterielCost(unitId, out int materiel));
            Assert.True(catalog.TryGetFuelCost(unitId, out int fuel));
            Assert.True(manpower > 0);
            Assert.True(materiel > 0);
            Assert.True(fuel > 0);

            FixtureAiSeatView shortMateriel = new FixtureAiSeatView();
            shortMateriel.Manpower = manpower;
            shortMateriel.Materials = materiel - 1;
            shortMateriel.Fuel = fuel;
            shortMateriel.FuelEnabled = true;
            Assert.False(AiAttackDefend.CanAfford(shortMateriel, catalog, unitId));

            FixtureAiSeatView shortFuel = new FixtureAiSeatView();
            shortFuel.Manpower = manpower;
            shortFuel.Materials = materiel;
            shortFuel.Fuel = fuel - 1;
            shortFuel.FuelEnabled = true;
            Assert.False(AiAttackDefend.CanAfford(shortFuel, catalog, unitId));

            shortFuel.Fuel = fuel;
            Assert.True(AiAttackDefend.CanAfford(shortFuel, catalog, unitId));

            shortFuel.FuelEnabled = false;
            shortFuel.Fuel = 0;
            Assert.True(AiAttackDefend.CanAfford(shortFuel, catalog, unitId));
        }

        private readonly struct CarryView
        {
            public readonly int Manpower;
            public readonly int Materiel;
            public readonly int Fuel;

            public CarryView(int manpower, int materiel, int fuel)
            {
                Manpower = manpower;
                Materiel = materiel;
                Fuel = fuel;
            }
        }

        private static CarryView Carry(SimWorld world, int factionId)
        {
            world.CopyIncomeCarry(factionId, out int manpower, out int materiel, out int fuel);
            return new CarryView(manpower, materiel, fuel);
        }

        private static int ManpowerPerMinute(SimWorld world, int factionId)
        {
            return ResourcePerMinute(world, factionId, 0);
        }

        private static int MaterielPerMinute(SimWorld world, int factionId)
        {
            return ResourcePerMinute(world, factionId, 1);
        }

        private static int FuelPerMinute(SimWorld world, int factionId)
        {
            return ResourcePerMinute(world, factionId, 2);
        }

        private static int ResourcePerMinute(SimWorld world, int factionId, int resource)
        {
            CaptureBalance rules = _catalog.Capture;
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
            Assert.True(world.TryGetTown(seat.HomeTownId, out TownView home));
            Assert.True(_scenario.TryGetTerritory(home.TerritoryKey, out ScenarioDerivedTerritory row));
            string factionKey = factionId == Compact ? "homestead_compact" : "remnant_command";
            Assert.True(rules.TryHqIncome(
                factionKey,
                out int hqManpower,
                out int hqMateriel,
                out int hqFuel,
                out int manpowerPpt,
                out int materielPpt,
                out int fuelPpt));
            int amount;
            int ppt;
            if (resource == 1)
            {
                amount = hqMateriel + PlaceAmount(rules, row.Places, 1);
                ppt = materielPpt;
            }
            else if (resource == 2)
            {
                amount = hqFuel + PlaceAmount(rules, row.Places, 2);
                ppt = fuelPpt;
            }
            else
            {
                amount = hqManpower + PlaceAmount(rules, row.Places, 0);
                ppt = manpowerPpt;
            }

            if (ppt == SimConfig.PptScale)
            {
                return amount;
            }

            return (int)(((long)amount * ppt) / SimConfig.PptScale);
        }

        private static int PlaceManpower(CaptureBalance rules, ScenarioPlaceCounts places)
        {
            return PlaceAmount(rules, places, 0);
        }

        private static int PlaceAmount(CaptureBalance rules, ScenarioPlaceCounts places, int resource)
        {
            int hamlet = Rate(rules, 0, resource);
            int village = Rate(rules, 1, resource);
            int town = Rate(rules, 2, resource);
            int city = Rate(rules, 3, resource);
            return (places.Hamlet * hamlet)
                + (places.Village * village)
                + (places.Town * town)
                + (places.City * city);
        }

        private static int Rate(CaptureBalance rules, int tier, int resource)
        {
            if (resource == 1)
            {
                return rules.HoldingMaterielPerMin(tier);
            }

            if (resource == 2)
            {
                return rules.HoldingFuelPerMin(tier);
            }

            return rules.HoldingManpowerPerMin(tier);
        }

        private static int TierManpower(SimWorld world, CaptureBalance rules, int territoryId)
        {
            int sum = 0;
            for (int i = 0; i < _snapshot.TownCount; i++)
            {
                AiTownRecord record = _snapshot.TownAt(i);
                if (!world.TryGetTown(record.Id, out TownView town) || town.TerritoryId != territoryId || town.Tier < 0)
                {
                    continue;
                }

                sum += rules.HoldingManpowerPerMin(town.Tier);
            }

            return sum;
        }

        private static int PlacePopCap(CaptureBalance rules, ScenarioPlaceCounts places)
        {
            return (places.Hamlet * rules.PopCapBonus(0))
                + (places.Village * rules.PopCapBonus(1))
                + (places.Town * rules.PopCapBonus(2))
                + (places.City * rules.PopCapBonus(3));
        }

        private static int PopulationClassManpower(SimWorld world, CaptureBalance rules, int territoryId)
        {
            int sum = 0;
            for (int i = 0; i < _snapshot.TownCount; i++)
            {
                AiTownRecord record = _snapshot.TownAt(i);
                if (!world.TryGetTown(record.Id, out TownView town) || town.TerritoryId != territoryId)
                {
                    continue;
                }

                if (town.PopulationClass < 0 || town.PopulationClass >= CaptureBalance.TierCount)
                {
                    continue;
                }

                sum += rules.HoldingManpowerPerMin(town.PopulationClass);
            }

            return sum;
        }

        private static SimWorld World()
        {
            Ensure();
            SimWorld world = new SimWorld(new MatchConfig(7UL, 0));
            world.BindCatalog(_catalog);
            BindSeat(world, Remnant, _scenario.SeatAt(0));
            BindSeat(world, Compact, _scenario.SeatAt(1));
            world.BindHoldings(_scenario, _snapshot);
            return world;
        }

        private static void BindSeat(SimWorld world, int factionId, ScenarioSeat seat)
        {
            Assert.True(_snapshot.TryGetTown(seat.HomeTownId, out AiTownRecord town));
            world.BindSeat(new SeatSetup(
                factionId,
                seat.Slot,
                seat.Faction,
                town.Id,
                town.XCm,
                town.YCm,
                1,
                -1,
                seat.StartingManpower,
                _catalog.StartingMateriel,
                _catalog.StartingFuel,
                false,
                _catalog.StartingSeatPopCap,
                seat.RaisableUnits,
                Array.Empty<ScenarioAutoResearch>(),
                Array.Empty<ScenarioUnitCount>()));
        }

        private static SeatView Seat(SimWorld world, int factionId)
        {
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
            return seat;
        }

        private static TownView Town(SimWorld world, int townId)
        {
            Assert.True(world.TryGetTown(townId, out TownView town));
            return town;
        }

        private static void Ensure()
        {
            if (_ready)
            {
                return;
            }

            _catalog = BalanceCatalog.LoadFromDirectory(BalanceLoad.DirectoryPath());
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName);
            byte[] mapBytes = GoldenMapFiles.ReadWrmb(
                Path.Combine(dir, GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
            _snapshot = AiMapSnapshot.FromWrmb(mapBytes);
            _scenario = SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                _catalog,
                mapBytes,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");
            _ready = true;
        }

        private static bool _ready;
        private static BalanceCatalog _catalog = null!;
        private static SimScenario _scenario = null!;
        private static AiMapSnapshot _snapshot = null!;
    }
}
