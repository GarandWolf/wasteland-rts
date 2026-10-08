using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class StartingUnitsTests
    {
        private const int Remnant = AiConstants.DefaultAiFactionA;
        private const int Compact = AiConstants.DefaultAiFactionB;

        private static readonly string[] RemnantIds =
        {
            "remnant_engineers",
            "remnant_motor_rifles",
            "remnant_motor_rifles",
            "remnant_recon"
        };

        private static readonly string[] CompactIds =
        {
            "compact_engineers",
            "compact_militia",
            "compact_militia",
            "compact_militia",
            "compact_militia",
            "compact_scouts"
        };

        [Fact]
        public void MvpGorge_BindSeat_SpawnsAuthoredUnitsAtTickZero()
        {
            BalanceCatalog catalog = Catalog();
            byte[] map = ReadMarshMap();
            SimScenario scenario = LoadScenario(catalog, map);
            AiMapSnapshot towns = AiMapSnapshot.FromWrmb(map);

            ScenarioSeat remnant = scenario.SeatAt(0);
            ScenarioSeat compact = scenario.SeatAt(1);
            Assert.Equal(RemnantIds, Expand(remnant.StartingUnits));
            Assert.Equal(CompactIds, Expand(compact.StartingUnits));
            Assert.Equal(2, Count(RemnantIds, "remnant_motor_rifles"));
            Assert.Equal(1, Count(RemnantIds, "remnant_recon"));
            Assert.Equal(1, Count(RemnantIds, "remnant_engineers"));
            Assert.Equal(4, Count(CompactIds, "compact_militia"));
            Assert.Equal(1, Count(CompactIds, "compact_scouts"));
            Assert.Equal(1, Count(CompactIds, "compact_engineers"));

            AiTownRecord remnantTown = Town(towns, remnant.HomeTownId);
            AiTownRecord compactTown = Town(towns, compact.HomeTownId);
            Assert.True(remnantTown.XCm != 0 || remnantTown.YCm != 0);
            Assert.True(compactTown.XCm != 0 || compactTown.YCm != 0);

            SimWorld world = new SimWorld(new MatchConfig(42UL, 0));
            world.BindCatalog(catalog);
            world.BindSeat(Seat(Remnant, remnant, scenario, catalog, remnantTown));
            world.BindSeat(Seat(Compact, compact, scenario, catalog, compactTown));

            Assert.Equal(0, world.Tick);
            Assert.Equal(RemnantIds.Length + CompactIds.Length, world.BattalionCount);
            AssertForce(world, catalog, Remnant, remnant, RemnantIds, remnantTown);
            AssertForce(world, catalog, Compact, compact, CompactIds, compactTown);
            Assert.Equal(MovementClass.Tracked, MoveClassOf(world, catalog, Remnant, "remnant_engineers"));
            Assert.Equal(MovementClass.Wheeled, MoveClassOf(world, catalog, Remnant, "remnant_motor_rifles"));
            Assert.Equal(MovementClass.Foot, MoveClassOf(world, catalog, Compact, "compact_militia"));
        }

        [Fact]
        public void MvpHost_BindsRemnantAsFaction1_AndCompactAsFaction2_AtSeatTown()
        {
            BalanceCatalog catalog = Catalog();
            byte[] map = ReadMarshMap();
            SimScenario scenario = LoadScenario(catalog, map);
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(1UL),
                map,
                new[] { Compact, Remnant },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());

            Assert.Equal(0, host.Tick);
            Assert.Equal(RemnantIds.Length + CompactIds.Length, host.World.BattalionCount);
            AssertHostSeat(host, scenario.SeatAt(0), Remnant, RemnantIds);
            AssertHostSeat(host, scenario.SeatAt(1), Compact, CompactIds);
            host.Shutdown();
        }

        private static void AssertHostSeat(
            AiMatchHost host,
            ScenarioSeat scenarioSeat,
            int factionId,
            string[] expectedIds)
        {
            Assert.True(host.World.TryGetSeat(factionId, out SeatView seat));
            Assert.Equal(scenarioSeat.HomeTownId, seat.HomeTownId);
            Assert.True(host.Ai.Map.TryGetTown(seat.HomeTownId, out AiTownRecord town));
            Assert.Equal(town.XCm, seat.HqXCm);
            Assert.Equal(town.YCm, seat.HqYCm);
            Assert.True(seat.HqXCm != 0 || seat.HqYCm != 0);
            Assert.Equal(expectedIds, UnitIds(host.World, factionId));
            Assert.Equal(PopOf(Catalog(), expectedIds), seat.PopUsed);
            Assert.Equal(scenarioSeat.StartingManpower, seat.Manpower);
        }

        private static void AssertForce(
            SimWorld world,
            BalanceCatalog catalog,
            int factionId,
            ScenarioSeat scenarioSeat,
            string[] expectedIds,
            AiTownRecord town)
        {
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
            Assert.Equal(0, world.Tick);
            Assert.Equal(town.XCm, seat.HqXCm);
            Assert.Equal(town.YCm, seat.HqYCm);
            Assert.Equal(expectedIds, UnitIds(world, factionId));
            Assert.Equal(PopOf(catalog, expectedIds), seat.PopUsed);
            Assert.True(seat.PopUsed <= seat.PopCap);
            Assert.Equal(scenarioSeat.StartingManpower, seat.Manpower);
            Assert.Equal(catalog.StartingMateriel, seat.Materials);
            Assert.Equal(catalog.StartingFuel, world.FuelOf(factionId));
            Assert.True(RaiseCost(catalog, expectedIds) > 0);

            List<ProductionQueueItem> queue = new List<ProductionQueueItem>();
            Assert.True(world.TryCopyProductionQueue(factionId, queue));
            Assert.Empty(queue);

            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                Assert.True(world.TryGetBattalionView(battalion.Id, out BattalionView view));
                Assert.Equal(town.XCm, view.XCm);
                Assert.Equal(town.YCm, battalion.PositionYCm);
                Assert.Equal(SimConfig.PptScale, battalion.StrengthPpt);
                Assert.True(catalog.TryGetUnitIndex(view.UnitId, out int unitIndex));
                Assert.Equal(unitIndex, battalion.UnitIndex);
                Assert.True(battalion.UnitIndex >= 0);
                MovementClass moveClass = catalog.UnitMoveClassAt(unitIndex);
                Assert.Equal(moveClass, battalion.MoveClass);
                Assert.Equal(catalog.SpeedCmPerSecond(moveClass), battalion.SpeedCmPerSecond);
            }
        }

        private static SeatSetup Seat(
            int factionId,
            ScenarioSeat scenarioSeat,
            SimScenario scenario,
            BalanceCatalog catalog,
            AiTownRecord town)
        {
            return new SeatSetup(
                factionId,
                scenarioSeat.Slot,
                scenarioSeat.Faction,
                town.Id,
                town.XCm,
                town.YCm,
                1,
                -1,
                scenarioSeat.StartingManpower,
                catalog.StartingMateriel,
                catalog.StartingFuel,
                scenario.MatchRules.FuelEnabled,
                30,
                scenarioSeat.RaisableUnits,
                scenarioSeat.AutoResearch,
                scenarioSeat.StartingUnits);
        }

        private static MovementClass MoveClassOf(SimWorld world, BalanceCatalog catalog, int factionId, string unitId)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                Assert.True(world.TryGetBattalionView(battalion.Id, out BattalionView view));
                if (view.UnitId == unitId)
                {
                    Assert.True(catalog.TryGetUnitIndex(unitId, out int index));
                    Assert.Equal(catalog.UnitMoveClassAt(index), battalion.MoveClass);
                    return battalion.MoveClass;
                }
            }

            throw new System.InvalidOperationException("missing " + unitId);
        }

        private static string[] UnitIds(SimWorld world, int factionId)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                Assert.True(world.TryGetBattalionView(battalion.Id, out BattalionView view));
                ids.Add(view.UnitId);
            }

            return ids.ToArray();
        }

        private static string[] Expand(ScenarioUnitCount[] units)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < units.Length; i++)
            {
                for (int n = 0; n < units[i].Count; n++)
                {
                    ids.Add(units[i].UnitId);
                }
            }

            return ids.ToArray();
        }

        private static int Count(string[] ids, string unitId)
        {
            int count = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == unitId)
                {
                    count++;
                }
            }

            return count;
        }

        private static int PopOf(BalanceCatalog catalog, string[] ids)
        {
            int pop = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                Assert.True(catalog.TryGetUnitIndex(ids[i], out int index));
                pop += catalog.UnitPopAt(index);
            }

            return pop;
        }

        private static int RaiseCost(BalanceCatalog catalog, string[] ids)
        {
            int manpower = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                Assert.True(catalog.TryGetUnitIndex(ids[i], out int index));
                manpower += catalog.UnitManpowerAt(index);
                Assert.True(catalog.UnitMaterielAt(index) > 0 || catalog.UnitManpowerAt(index) > 0);
            }

            return manpower;
        }

        private static AiTownRecord Town(AiMapSnapshot map, int townId)
        {
            Assert.True(map.TryGetTown(townId, out AiTownRecord town));
            return town;
        }

        private static SimScenario LoadScenario(BalanceCatalog catalog, byte[] map)
        {
            return SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                catalog,
                map,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");
        }

        private static byte[] ReadMarshMap()
        {
            return GoldenMapFiles.ReadWrmb(
                Path.Combine(
                    GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName),
                    GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
        }

        private static BalanceCatalog Catalog()
        {
            return BalanceCatalog.LoadFromDirectory(Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance"));
        }
    }
}
