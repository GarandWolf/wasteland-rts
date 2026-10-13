using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class RaiseBattalionTests
    {
        private const int Faction = 1;
        private const int OtherFaction = 9;
        private const string LineUnit = "remnant_motor_rifles";
        private const string TierTwoUnit = "remnant_mech_battalion";
        private const string UnlistedUnit = "compact_militia";

        [Fact]
        public void ExecuteTick_IsIssuedPlusDelay_AndFactionPrecedesSubmitOrder()
        {
            SimWorld world = NewWorld(delay: 2);
            BindRichSeat(world, Faction, tier: 1, popCap: 4);
            BindRichSeat(world, OtherFaction, tier: 1, popCap: 4);

            world.SubmitRaiseBattalion(OtherFaction, 90, LineUnit, 10, 10);
            world.SubmitRaiseBattalion(Faction, 10, LineUnit, 20, 20);

            world.Step();
            world.Step();
            Assert.Equal(0, world.BattalionCount);
            Assert.Equal(2, world.Tick);

            world.Step();
            Assert.Equal(0, world.BattalionCount);
            Assert.Equal(3, world.Tick);

            int firstSpawn = -1;
            int secondSpawn = -1;
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind != SimEventKind.BattalionRaiseStarted)
                {
                    continue;
                }

                if (firstSpawn < 0)
                {
                    firstSpawn = world.Events[i].SubjectId;
                }
                else
                {
                    secondSpawn = world.Events[i].SubjectId;
                }
            }

            Assert.Equal(10, firstSpawn);
            Assert.Equal(90, secondSpawn);

            SimWorld straight = new SimWorld(new MatchConfig(3UL, commandDelayTicks: 0));
            straight.Submit(SimCommand.SpawnBattalion(0, Faction, 1, 0, 0, 1000));
            straight.Submit(SimCommand.Move(0, Faction, 1, 100, 0));
            straight.Submit(SimCommand.Move(0, Faction, 1, 5000, 0));
            straight.Step();
            Assert.Equal(5000, straight.GetBattalion(1).TargetXCm);

            SimWorld reversed = new SimWorld(new MatchConfig(3UL, commandDelayTicks: 0));
            reversed.Submit(SimCommand.SpawnBattalion(0, Faction, 1, 0, 0, 1000));
            reversed.Submit(SimCommand.Move(0, Faction, 1, 5000, 0));
            reversed.Submit(SimCommand.Move(0, Faction, 1, 100, 0));
            reversed.Step();
            Assert.Equal(100, reversed.GetBattalion(1).TargetXCm);
        }

        [Fact]
        public void SameInputs_ReplayToIdenticalStateHash()
        {
            List<ulong> first = RunReplay();
            List<ulong> second = RunReplay();
            Assert.Equal(first, second);

            SimWorld factionFirst = ReplayWorld();
            factionFirst.SubmitRaiseBattalion(Faction, 10, LineUnit, 10000, 10000);
            factionFirst.SubmitRaiseBattalion(OtherFaction, 90, LineUnit, 10000, 10000);
            SimWorld otherFirst = ReplayWorld();
            otherFirst.SubmitRaiseBattalion(OtherFaction, 90, LineUnit, 10000, 10000);
            otherFirst.SubmitRaiseBattalion(Faction, 10, LineUnit, 10000, 10000);

            Assert.Equal(factionFirst.Step(), otherFirst.Step());
            Assert.Equal(factionFirst.HashState(), otherFirst.HashState());
        }

        [Fact]
        public void RejectionCodes_ExposeIntegerParams()
        {
            AssertReject(TooLate(), CommandRejectReason.TooLate, 7, 0, 0);
            AssertReject(UnknownBattalion(), CommandRejectReason.UnknownBattalion, 99, 0, 0);
            AssertReject(DuplicateBattalion(), CommandRejectReason.DuplicateBattalion, 4, 0, 0);
            AssertReject(NotOwner(), CommandRejectReason.NotOwner, 4, 0, 0);
            AssertReject(NegativeSpeed(), CommandRejectReason.NegativeSpeed, 4, 0, 0);
            AssertReject(UnknownKind(), CommandRejectReason.UnknownKind, 5, 0, 0);
            AssertReject(UnknownSeat(), CommandRejectReason.UnknownSeat, 3, 0, 0);
            AssertReject(UnitNotRaisable(), CommandRejectReason.UnitNotRaisable, 3, -1, 0);

            SimWorld pop = Rich(popCap: 1, manpower: 1000, materials: 1000, fuel: 0, fuelEnabled: false);
            pop.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            pop.SubmitRaiseBattalion(Faction, 2, LineUnit, 6, 6);
            pop.Step();
            AssertParams(pop, 2, CommandRejectReason.PopCapReached, 1, 1);

            SimWorld manpower = Rich(popCap: 4, manpower: 69, materials: 1000, fuel: 0, fuelEnabled: false);
            manpower.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            manpower.Step();
            AssertParams(manpower, 1, CommandRejectReason.InsufficientManpower, 70, 69);

            SimWorld materials = Rich(popCap: 4, manpower: 1000, materials: 39, fuel: 0, fuelEnabled: false);
            materials.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            materials.Step();
            AssertParams(materials, 1, CommandRejectReason.InsufficientMaterials, 40, 39);

            SimWorld fuel = Rich(popCap: 4, manpower: 1000, materials: 1000, fuel: 29, fuelEnabled: true);
            fuel.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            fuel.Step();
            AssertParams(fuel, 1, CommandRejectReason.InsufficientFuel, 30, 29);

            SimWorld noPath = NewWorld(0);
            BindRichSeat(noPath, Faction, tier: 1, popCap: 4);
            BindDisconnected(noPath);
            noPath.Submit(SimCommand.SpawnBattalion(0, Faction, 8, 0, 0, 1000));
            noPath.Submit(SimCommand.Move(0, Faction, 8, 50000, 0));
            noPath.Step();
            AssertParams(noPath, 8, CommandRejectReason.NoPath, 1, 2);
        }

        [Fact]
        public void TierLocked_WinsOverUnitNotRaisable()
        {
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, tier: 1, researchTick: -1, manpower: 1000, materials: 1000, fuel: 0, fuelEnabled: false, popCap: 4, LineUnit));
            world.SubmitRaiseBattalion(Faction, 3, TierTwoUnit, 80, 80);
            world.Step();

            AssertParams(world, 3, CommandRejectReason.TierLocked, 2, 1);
            Assert.Equal(0, world.BattalionCount);
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind == SimEventKind.CommandRejected)
                {
                    Assert.NotEqual(CommandRejectReason.UnitNotRaisable, world.Events[i].RejectReason);
                }
            }
        }

        [Fact]
        public void Raise_SpawnsAtHq_ThenFollowsRoadTowardRally()
        {
            SimWorld world = NewWorld(0);
            BindRichSeat(world, Faction, tier: 1, popCap: 4);
            BindCornerRoads(world);
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 10000, 10000);
            world.Step();
            Assert.Equal(0, world.BattalionCount);
            Assert.True(world.TryGetSeat(Faction, out SeatView charged));
            Assert.Equal(1000 - 70, charged.Manpower);
            Assert.Equal(1, charged.PopUsed);

            int buildTicks = BuildTicks(LineUnit);
            Assert.Equal(600, buildTicks);
            StepUntilTick(world, buildTicks);
            Assert.Equal(0, world.BattalionCount);
            world.Step();

            SimBattalion battalion = world.GetBattalion(1);
            Assert.Equal(0, battalion.PositionXCm);
            Assert.InRange(battalion.PositionYCm, 1, 9999);
            Assert.NotEqual(10000, battalion.PositionXCm);
            Assert.True(battalion.HasTarget);
            Assert.True(battalion.PathCount >= 2);
            Assert.Equal(0, battalion.PathXCm[0]);
            Assert.Equal(10000, battalion.PathYCm[0]);
            Assert.Equal(10000, battalion.PathXCm[battalion.PathCount - 1]);
            Assert.Equal(10000, battalion.PathYCm[battalion.PathCount - 1]);

            Assert.True(world.TryGetBattalionView(1, out BattalionView view));
            Assert.Equal(LineUnit, view.UnitId);
            Assert.True(world.TryGetSeat(Faction, out SeatView seat));
            Assert.Equal(1, seat.CurrentTier);
            Assert.Equal(-1, seat.TierResearchCompleteTick);
            // Tick 601 includes 30 HQ accruals: 5 manpower and 25 materiel.
            Assert.Equal(1000 - 70 + 5, seat.Manpower);
            Assert.Equal(1000 - 40 + 25, seat.Materials);
            Assert.Equal(1, seat.PopUsed);
            Assert.Equal(4, seat.PopCap);
            Assert.Equal(1000, seat.VisibleStrength);
            Assert.Equal(buildTicks, SpawnTick(world, 1));
            Assert.Equal(7, seat.HomeTownId);
        }

        [Fact]
        public void ResearchCompleteTick_UnlocksTierBeforeRaiseExecutes()
        {
            SimWorld world = NewWorld(delay: 2);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(
                Faction,
                tier: 1,
                researchTick: 2,
                manpower: 1000,
                materials: 1000,
                fuel: 0,
                fuelEnabled: false,
                popCap: 4,
                LineUnit,
                TierTwoUnit));
            world.SubmitRaiseBattalion(Faction, 4, TierTwoUnit, 0, 0);

            world.Step();
            world.Step();
            Assert.Equal(2, world.Tick);
            Assert.True(world.TryGetSeat(Faction, out SeatView before));
            Assert.Equal(1, before.CurrentTier);
            Assert.Equal(2, before.TierResearchCompleteTick);
            Assert.Equal(0, world.BattalionCount);

            world.Step();
            Assert.True(world.TryGetSeat(Faction, out SeatView after));
            Assert.Equal(2, after.CurrentTier);
            Assert.Equal(-1, after.TierResearchCompleteTick);
            Assert.Equal(0, world.BattalionCount);
            Assert.Equal(SimEventKind.BattalionRaiseStarted, world.Events[world.Events.Count - 1].Kind);
            Assert.Equal(4, world.Events[world.Events.Count - 1].SubjectId);
        }

        [Fact]
        public void ForwardSink_RaisesAtHq_AndMovesTowardRally()
        {
            SimWorld world = NewWorld(0);
            BindRichSeat(world, Faction, tier: 1, popCap: 4);
            BindCornerRoads(world);
            ForwardAiCommandSink sink = new ForwardAiCommandSink(world);

            int raised = sink.SubmitRaiseBattalion(Faction, LineUnit, 10000, 10000);
            int locked = sink.SubmitRaiseBattalion(Faction, TierTwoUnit, 10000, 10000);
            world.Step();

            Assert.Equal(CommandRejectReason.None, sink.RejectOf(raised));
            Assert.Equal(CommandRejectReason.TierLocked, sink.RejectOf(locked));
            Assert.Equal(0, world.BattalionCount);
            Assert.NotEqual(raised, locked);

            StepUntilTick(world, BuildTicks(LineUnit));
            world.Step();

            SimBattalion battalion = world.GetBattalion(raised);
            Assert.Equal(0, battalion.PositionXCm);
            Assert.InRange(battalion.PositionYCm, 1, 9999);
            Assert.Equal(10000, battalion.PathXCm[battalion.PathCount - 1]);
            Assert.Equal(10000, battalion.PathYCm[battalion.PathCount - 1]);
            Assert.NotEqual(10000, battalion.PositionXCm);

            sink.SubmitMove(Faction, raised, 0, 0);
            world.Step();
            Assert.Equal(0, world.GetBattalion(raised).TargetXCm);
        }

        [Fact]
        public void CatalogGetters_ReadRealBalanceData()
        {
            BalanceCatalog catalog = Catalog();

            Assert.True(catalog.TryGetUnitTier(LineUnit, out int lineTier));
            Assert.Equal(1, lineTier);
            Assert.True(catalog.TryGetUnitTier(TierTwoUnit, out int mechTier));
            Assert.Equal(2, mechTier);
            Assert.True(catalog.TryGetUnitRole(LineUnit, out string lineRole));
            Assert.Equal("line_infantry", lineRole);
            Assert.True(catalog.TryGetUnitRole(UnlistedUnit, out string militiaRole));
            Assert.Equal("garrison_infantry", militiaRole);
            Assert.True(catalog.TryGetUnitTargetClass(TierTwoUnit, out string mechClass));
            Assert.Equal("armored", mechClass);
            Assert.True(catalog.TryGetUnitTargetClass(UnlistedUnit, out string militiaClass));
            Assert.Equal("soft", militiaClass);
            Assert.True(catalog.TryGetManpowerCost(LineUnit, out int manpower));
            Assert.Equal(70, manpower);
            Assert.True(catalog.TryGetBuildingRole("hq", out string hq));
            Assert.Equal("hq", hq);
            Assert.True(catalog.TryGetBuildingRole("supply_depot", out string depot));
            Assert.Equal("supply_depot", depot);
            Assert.True(catalog.TryGetBuildingRole("town_walls_l1", out string wall));
            Assert.Equal("wall", wall);
            Assert.True(catalog.TryGetBuildingRole("bunker", out string fort));
            Assert.Equal("fortification", fort);
            Assert.True(catalog.TryGetBuildingRole("fuel_pump", out string resource));
            Assert.Equal("resource", resource);
            Assert.True(catalog.TryGetBuildingRole("recruitment_office", out string recruitment));
            Assert.Equal("recruitment", recruitment);
            Assert.True(catalog.TryGetBuildingRole("muster_camp", out string production));
            Assert.Equal("production", production);

            int missingTier;
            string missingRole;
            string missingClass;
            int missingCost;
            string missingBuilding;
            Assert.False(catalog.TryGetUnitTier("no_such_unit", out missingTier));
            Assert.False(catalog.TryGetUnitRole("no_such_unit", out missingRole));
            Assert.False(catalog.TryGetUnitTargetClass("no_such_unit", out missingClass));
            Assert.False(catalog.TryGetManpowerCost("no_such_unit", out missingCost));
            Assert.False(catalog.TryGetBuildingRole("no_such_building", out missingBuilding));
        }

        [Fact]
        public void Reject_UnknownSeat_ChargesNothing()
        {
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.SubmitRaiseBattalion(Faction, 3, LineUnit, 1, 1);
            world.Step();
            AssertParams(world, 3, CommandRejectReason.UnknownSeat, 0, 0);
            Assert.False(world.TryCopyProductionQueue(Faction, new List<ProductionQueueItem>()));
            Assert.Equal(0, world.BattalionCount);
        }

        [Fact]
        public void Reject_DuplicateBattalion_ChargesNothing()
        {
            SimWorld world = Rich(popCap: 4, manpower: 1000, materials: 1000, fuel: 50, fuelEnabled: true);
            SeatView before = SeatOf(world);
            world.Submit(SimCommand.SpawnBattalion(0, Faction, 4, 0, 0, 10));
            world.SubmitRaiseBattalion(Faction, 4, LineUnit, 1, 1);
            world.Step();
            AssertParams(world, 4, CommandRejectReason.DuplicateBattalion, 0, 0);
            AssertUntouched(before, SeatOf(world));
            Assert.Equal(50, world.FuelOf(Faction));
            Assert.Empty(Queue(world));
        }

        [Fact]
        public void Reject_UnknownUnit_ChargesNothing()
        {
            SimWorld world = Rich(popCap: 4, manpower: 1000, materials: 1000, fuel: 50, fuelEnabled: true);
            SeatView before = SeatOf(world);
            world.SubmitRaiseBattalion(Faction, 3, "no_such_unit", 1, 1);
            world.Step();
            AssertParams(world, 3, CommandRejectReason.UnitNotRaisable, -1, 0);
            AssertUntouched(before, SeatOf(world));
            Assert.Equal(50, world.FuelOf(Faction));
            Assert.Empty(Queue(world));
        }

        [Fact]
        public void Reject_PopCapReached_ChargesNothingExtra()
        {
            SimWorld world = Rich(popCap: 1, manpower: 1000, materials: 1000, fuel: 0, fuelEnabled: false);
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            world.SubmitRaiseBattalion(Faction, 2, LineUnit, 6, 6);
            world.Step();
            AssertParams(world, 2, CommandRejectReason.PopCapReached, 1, 1);
            SeatView seat = SeatOf(world);
            Assert.Equal(1, seat.PopUsed);
            Assert.Equal(1000 - 70, seat.Manpower);
            Assert.Equal(1000 - 40, seat.Materials);
            Assert.Single(Queue(world));
        }

        [Fact]
        public void Reject_InsufficientManpower_ChargesNothing()
        {
            SimWorld world = Rich(popCap: 4, manpower: 69, materials: 1000, fuel: 0, fuelEnabled: false);
            SeatView before = SeatOf(world);
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            world.Step();
            AssertParams(world, 1, CommandRejectReason.InsufficientManpower, 70, 69);
            AssertUntouched(before, SeatOf(world));
            Assert.Empty(Queue(world));
        }

        [Fact]
        public void Reject_InsufficientMaterials_ChargesNothing()
        {
            SimWorld world = Rich(popCap: 4, manpower: 1000, materials: 39, fuel: 0, fuelEnabled: false);
            SeatView before = SeatOf(world);
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            world.Step();
            AssertParams(world, 1, CommandRejectReason.InsufficientMaterials, 40, 39);
            AssertUntouched(before, SeatOf(world));
            Assert.Empty(Queue(world));
        }

        [Fact]
        public void Reject_InsufficientFuel_ChargesNothing()
        {
            SimWorld world = Rich(popCap: 4, manpower: 1000, materials: 1000, fuel: 29, fuelEnabled: true);
            SeatView before = SeatOf(world);
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 5, 5);
            world.Step();
            AssertParams(world, 1, CommandRejectReason.InsufficientFuel, 30, 29);
            AssertUntouched(before, SeatOf(world));
            Assert.Equal(29, world.FuelOf(Faction));
            Assert.Empty(Queue(world));
        }

        [Fact]
        public void FuelDisabled_SkipsFuelCheck()
        {
            SimWorld world = Rich(popCap: 4, manpower: 1000, materials: 1000, fuel: 0, fuelEnabled: false);
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 0, 0);
            world.Step();
            Assert.Equal(CommandRejectReason.None, RejectionOrNone(world, 1));
            Assert.Equal(0, world.FuelOf(Faction));
            Assert.Equal(1000 - 70, SeatOf(world).Manpower);
            Assert.Equal(1, SeatOf(world).PopUsed);
            Assert.Single(Queue(world));
        }

        [Fact]
        public void Reject_NoPath_ChargesNothing()
        {
            SimWorld world = Rich(popCap: 4, manpower: 1000, materials: 1000, fuel: 40, fuelEnabled: true);
            SeatView before = SeatOf(world);
            BindDisconnected(world);
            world.Submit(SimCommand.SpawnBattalion(0, Faction, 8, 0, 0, 1000));
            world.Submit(SimCommand.Move(0, Faction, 8, 50000, 0));
            world.Step();
            AssertParams(world, 8, CommandRejectReason.NoPath, 1, 2);
            AssertUntouched(before, SeatOf(world));
            Assert.Equal(40, world.FuelOf(Faction));
        }

        [Fact]
        public void CostsAndPop_ChargedOnExecuteTick_NotBefore()
        {
            SimWorld world = NewWorld(delay: 2);
            BindRichSeat(world, Faction, tier: 1, popCap: 4);
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 4, 4);
            world.Step();
            world.Step();
            Assert.Equal(2, world.Tick);
            Assert.Equal(1000, SeatOf(world).Manpower);
            Assert.Equal(1000, SeatOf(world).Materials);
            Assert.Equal(0, SeatOf(world).PopUsed);
            Assert.Empty(Queue(world));
            Assert.Equal(0, world.BattalionCount);

            world.Step();
            Assert.Equal(1000 - 70, SeatOf(world).Manpower);
            Assert.Equal(1000 - 40, SeatOf(world).Materials);
            Assert.Equal(1, SeatOf(world).PopUsed);
            Assert.Equal(0, world.BattalionCount);
            ProductionQueueItem job = Queue(world)[0];
            Assert.Equal(LineUnit, job.UnitId);
            Assert.Equal(2, job.StartTick);
            Assert.Equal(2 + 600, job.CompleteTick);
        }

        [Fact]
        public void ArmourRaise_SpawnsAtHq_AfterBuildTicks()
        {
            const string armour = "remnant_armoured_battalion";
            Assert.Equal(1200, BuildTicks(armour));
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 2, -1, 1000, 1000, 0, false, 4, armour));
            world.SubmitRaiseBattalion(Faction, 11, armour, 10000, 0);
            world.Step();
            Assert.Equal(0, world.BattalionCount);
            ProductionQueueItem job = Queue(world)[0];
            Assert.Equal(0, job.StartTick);
            Assert.Equal(1200, job.CompleteTick);

            StepUntilTick(world, 1200);
            Assert.Equal(0, world.BattalionCount);
            world.Step();

            Assert.Equal(1200, SpawnTick(world, 11));
            SimBattalion battalion = world.GetBattalion(11);
            Assert.Equal(0, battalion.PositionYCm);
            Assert.InRange(battalion.PositionXCm, 1, 2000);
            Assert.NotEqual(10000, battalion.PositionXCm);
            Assert.True(battalion.HasTarget);
            Assert.Equal(10000, battalion.TargetXCm);
            Assert.Equal(0, battalion.TargetYCm);
        }

        [Fact]
        public void HeavyWorksRaises_CompleteSerially()
        {
            const string armour = "remnant_armoured_battalion";
            Assert.Equal(1200, BuildTicks(armour));
            Assert.Equal(900, BuildTicks(TierTwoUnit));
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 2, -1, 1000, 1000, 0, false, 4, armour, TierTwoUnit));
            world.SubmitRaiseBattalion(Faction, 11, armour, 0, 0);
            world.SubmitRaiseBattalion(Faction, 12, TierTwoUnit, 0, 0);
            world.Step();

            List<ProductionQueueItem> queued = Queue(world);
            Assert.Equal(2, queued.Count);
            Assert.Equal(armour, queued[0].UnitId);
            Assert.Equal(0, queued[0].StartTick);
            Assert.Equal(1200, queued[0].CompleteTick);
            Assert.Equal(TierTwoUnit, queued[1].UnitId);
            Assert.Equal(1200, queued[1].StartTick);
            Assert.Equal(2100, queued[1].CompleteTick);
            Assert.Equal(2, SeatOf(world).PopUsed);

            StepUntilTick(world, 1200);
            Assert.Equal(0, world.BattalionCount);
            world.Step();
            Assert.Equal(1200, SpawnTick(world, 11));
            Assert.Equal(0, world.GetBattalion(11).PositionXCm);
            Assert.Equal(0, world.GetBattalion(11).PositionYCm);
            Assert.False(world.TryGetBattalionView(12, out _));

            StepUntilTick(world, 2100);
            Assert.False(world.TryGetBattalionView(12, out _));
            world.Step();
            Assert.Equal(2100, SpawnTick(world, 12));
            Assert.Equal(0, world.GetBattalion(12).PositionXCm);
            Assert.Equal(0, world.GetBattalion(12).PositionYCm);
        }

        [Fact]
        public void DifferentProducedAtBuildings_RunInParallel()
        {
            const string armour = "remnant_armoured_battalion";
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 2, -1, 1000, 1000, 0, false, 4, LineUnit, armour));
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 0, 0);
            world.SubmitRaiseBattalion(Faction, 11, armour, 0, 0);
            world.Step();

            ProductionQueueItem rifles = FindJob(world, LineUnit);
            ProductionQueueItem armourJob = FindJob(world, armour);
            Assert.Equal(0, rifles.StartTick);
            Assert.Equal(600, rifles.CompleteTick);
            Assert.Equal(0, armourJob.StartTick);
            Assert.Equal(1200, armourJob.CompleteTick);

            StepUntilTick(world, 600);
            world.Step();
            Assert.Equal(600, SpawnTick(world, 1));
            Assert.False(world.TryGetBattalionView(11, out _));

            StepUntilTick(world, 1200);
            world.Step();
            Assert.Equal(1200, SpawnTick(world, 11));
        }

        [Fact]
        public void TwoSinks_AllocateDistinctBattalionIds()
        {
            SimWorld world = NewWorld(0);
            BindRichSeat(world, Faction, tier: 1, popCap: 4);
            BindRichSeat(world, OtherFaction, tier: 1, popCap: 4);
            world.Submit(SimCommand.SpawnBattalion(0, Faction, 5, 0, 0, 10));
            world.Submit(SimCommand.SpawnBattalion(3, Faction, 20, 1, 1, 10));

            ForwardAiCommandSink first = new ForwardAiCommandSink(world);
            ForwardAiCommandSink second = new ForwardAiCommandSink(world);
            int left = first.SubmitRaiseBattalion(Faction, LineUnit, 0, 0);
            int right = second.SubmitRaiseBattalion(OtherFaction, LineUnit, 0, 0);
            int again = first.SubmitRaiseBattalion(Faction, LineUnit, 1, 1);

            Assert.Equal(21, left);
            Assert.Equal(22, right);
            Assert.Equal(23, again);
            Assert.NotEqual(left, right);
        }

        [Fact]
        public void ProductionQueue_Replay_MatchesHashEveryTick()
        {
            List<ulong> first = RunProductionReplay();
            List<ulong> second = RunProductionReplay();
            Assert.Equal(first, second);
            Assert.True(first.Count > 1200);
        }

        private static List<ulong> RunReplay()
        {
            SimWorld world = ReplayWorld();
            world.SubmitRaiseBattalion(Faction, 1, LineUnit, 10000, 10000);
            world.Submit(SimCommand.Move(4, Faction, 1, 0, 10000));
            List<ulong> hashes = new List<ulong>();
            for (int i = 0; i < 12; i++)
            {
                hashes.Add(world.Step());
            }

            return hashes;
        }

        private static SimWorld ReplayWorld()
        {
            SimWorld world = NewWorld(delay: 2);
            BindRichSeat(world, Faction, tier: 1, popCap: 4);
            BindRichSeat(world, OtherFaction, tier: 1, popCap: 4);
            BindCornerRoads(world);
            return world;
        }

        private static SimWorld TooLate()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, commandDelayTicks: 0));
            world.Step();
            world.Submit(SimCommand.Move(0, Faction, 7, 1, 1));
            return world;
        }

        private static SimWorld UnknownBattalion()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.Submit(SimCommand.Move(0, Faction, 99, 1, 1));
            world.Step();
            return world;
        }

        private static SimWorld DuplicateBattalion()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.Submit(SimCommand.SpawnBattalion(0, Faction, 4, 0, 0, 10));
            world.Submit(SimCommand.SpawnBattalion(0, Faction, 4, 1, 1, 10));
            world.Step();
            return world;
        }

        private static SimWorld NotOwner()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.Submit(SimCommand.SpawnBattalion(0, Faction, 4, 0, 0, 10));
            world.Submit(SimCommand.Move(0, OtherFaction, 4, 3, 3));
            world.Step();
            return world;
        }

        private static SimWorld NegativeSpeed()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.Submit(SimCommand.SpawnBattalion(0, Faction, 4, 0, 0, -5));
            world.Step();
            return world;
        }

        private static SimWorld UnknownKind()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.Submit(new SimCommand(0, Faction, 5, (CommandKind)255, 0, 0, 0));
            world.Step();
            return world;
        }

        private static SimWorld UnknownSeat()
        {
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.SubmitRaiseBattalion(Faction, 3, LineUnit, 1, 1);
            world.Step();
            return world;
        }

        private static SimWorld UnitNotRaisable()
        {
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 1, -1, 1000, 1000, 0, false, 4, LineUnit));
            world.SubmitRaiseBattalion(Faction, 3, "no_such_unit", 1, 1);
            world.Step();
            return world;
        }

        private static SimWorld Rich(int popCap, int manpower, int materials, int fuel, bool fuelEnabled)
        {
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 1, -1, manpower, materials, fuel, fuelEnabled, popCap, LineUnit));
            return world;
        }

        private static void AssertReject(SimWorld world, CommandRejectReason code, int subject, int param0, int param1)
        {
            AssertParams(world, subject, code, param0, param1);
        }

        private static void AssertParams(SimWorld world, int subject, CommandRejectReason code, int param0, int param1)
        {
            Assert.True(world.TryGetRejection(subject, out CommandRejectReason have, out int p0, out int p1));
            Assert.Equal(code, have);
            Assert.Equal(param0, p0);
            Assert.Equal(param1, p1);
        }

        private static void BindRichSeat(SimWorld world, int factionId, int tier, int popCap)
        {
            if (world.Tick == 0 && factionId == Faction)
            {
                world.BindCatalog(Catalog());
            }

            world.BindSeat(Seat(factionId, tier, -1, 1000, 1000, 0, false, popCap, LineUnit));
        }

        private static SimWorld NewWorld(int delay)
        {
            return new SimWorld(new MatchConfig(42UL, delay));
        }

        private static void StepUntilTick(SimWorld world, int tick)
        {
            while (world.Tick < tick)
            {
                world.Step();
            }
        }

        private static int BuildTicks(string unitId)
        {
            BalanceCatalog catalog = Catalog();
            Assert.True(catalog.TryGetUnitIndex(unitId, out int index));
            return catalog.UnitBuildTicksAt(index);
        }

        private static int SpawnTick(SimWorld world, int battalionId)
        {
            for (int i = 0; i < world.Events.Count; i++)
            {
                SimEvent ev = world.Events[i];
                if (ev.Kind == SimEventKind.BattalionSpawned && ev.SubjectId == battalionId)
                {
                    return ev.Tick;
                }
            }

            Assert.Fail("missing spawn " + battalionId);
            return -1;
        }

        private static SeatView SeatOf(SimWorld world)
        {
            Assert.True(world.TryGetSeat(Faction, out SeatView seat));
            return seat;
        }

        private static List<ProductionQueueItem> Queue(SimWorld world)
        {
            List<ProductionQueueItem> items = new List<ProductionQueueItem>();
            Assert.True(world.TryCopyProductionQueue(Faction, items));
            return items;
        }

        private static ProductionQueueItem FindJob(SimWorld world, string unitId)
        {
            List<ProductionQueueItem> items = Queue(world);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].UnitId == unitId)
                {
                    return items[i];
                }
            }

            Assert.Fail("missing job " + unitId);
            return new ProductionQueueItem(unitId, 0, 0);
        }

        private static void AssertUntouched(SeatView before, SeatView after)
        {
            Assert.Equal(before.Manpower, after.Manpower);
            Assert.Equal(before.Materials, after.Materials);
            Assert.Equal(before.PopUsed, after.PopUsed);
        }

        private static CommandRejectReason RejectionOrNone(SimWorld world, int subject)
        {
            CommandRejectReason code;
            int param0;
            int param1;
            if (world.TryGetRejection(subject, out code, out param0, out param1))
            {
                return code;
            }

            return CommandRejectReason.None;
        }

        private static List<ulong> RunProductionReplay()
        {
            const string armour = "remnant_armoured_battalion";
            SimWorld world = NewWorld(0);
            world.BindCatalog(Catalog());
            world.BindSeat(Seat(Faction, 2, -1, 1000, 1000, 0, false, 4, armour, TierTwoUnit));
            world.SubmitRaiseBattalion(Faction, 11, armour, 3, 4);
            world.SubmitRaiseBattalion(Faction, 12, TierTwoUnit, 5, 6);
            List<ulong> hashes = new List<ulong>();
            for (int i = 0; i < 1211; i++)
            {
                hashes.Add(world.Step());
            }

            return hashes;
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
                factionId == OtherFaction ? "homestead_compact" : "remnant_command",
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

        private static void BindCornerRoads(SimWorld world)
        {
            RoadGraphBuilder roads = new RoadGraphBuilder()
                .AddNode(1, 0, 0)
                .AddNode(2, 0, 10000)
                .AddNode(3, 10000, 10000)
                .AddEdge(10, 1, 2, 10000, RoadTier.Paved, RoadSurface.Asphalt)
                .AddEdge(11, 2, 3, 10000, RoadTier.Paved, RoadSurface.Asphalt);
            Bind(world, roads);
        }

        private static void BindDisconnected(SimWorld world)
        {
            RoadGraphBuilder roads = new RoadGraphBuilder()
                .AddNode(1, 0, 0)
                .AddNode(2, 50000, 0);
            Bind(world, roads);
        }

        private static void Bind(SimWorld world, RoadGraphBuilder roads)
        {
            byte[] blob = roads.ToBlob();
            byte[] terrain = new TerrainGridBuilder(2, 2, 10000).Fill(0, LandCover.Field).ToBlob();
            MovementService movement = MovementService.Create(
                blob,
                terrain,
                MovementTables.Default(),
                MovementServiceConfig.Default);
            world.BindRoads(movement, Wrmb.ReadRoadGraph(blob));
        }
    }
}
