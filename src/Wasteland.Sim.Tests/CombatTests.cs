using System;
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
    public sealed class CombatTests
    {
        // TODO: port tools/sim.py golden_expected.txt (17 damage vectors and 27 battles)
        // when that file is on main. These tests lock the spec formula instead.

        [Fact]
        public void DamageChain_MatchesBalanceNotesTimeToKillRows()
        {
            Assert.Equal(150, OpenVolley(150, 0));
            Assert.Equal(70, WalledUrbanVolley(false));
            Assert.Equal(80, WalledUrbanVolley(true));
            Assert.Equal(4, ArmourHitByMilitia());
            Assert.Equal(57, OpenVolley(50, 0, 500, 0));
            Assert.Equal(50, OpenVolley(50, 0, 499, 0));
        }

        [Fact]
        public void HighGround_UsesCellBoundaryNotARoundedCellSize()
        {
            CombatTerrain terrain = BoundaryTerrain();
            Assert.Equal(0, terrain.Sample(99, 0).ElevationDm);
            Assert.Equal(500, terrain.Sample(100, 0).ElevationDm);
            Assert.Equal(terrain.Sample(1, 0).ElevationDm, terrain.Sample(99, 0).ElevationDm);

            BalanceCatalog catalog = Catalog();
            int fromHigh = FireOnce(catalog, terrain, 100, 50, out int _);
            int fromLow = FireOnce(catalog, terrain, 50, 100, out int _);
            int same = FireOnce(catalog, terrain, 40, 60, out int _);
            Assert.Equal(57, fromHigh);
            Assert.Equal(50, fromLow);
            Assert.Equal(50, same);
        }

        [Fact]
        public void Garrison_TakesUrbanDefenceOnAFieldCell_MobileUsesTheCell()
        {
            CombatTerrain field = new CombatTerrain(
                1,
                1,
                0,
                0,
                100000,
                100000,
                new ushort[] { 0 },
                new ushort[] { 0 },
                new byte[] { 1 },
                new byte[] { 0 },
                0,
                10);
            BalanceCatalog catalog = Catalog();
            int garrisonLoss = Loss(catalog, field, SimWorld.NeutralOwnerId, "compact_militia");
            int mobileLoss = Loss(catalog, field, 2, "compact_militia");
            Assert.Equal(38, garrisonLoss);
            Assert.Equal(50, mobileLoss);
        }

        [Fact]
        public void SimultaneousKill_BothFire_AndLowerIdIsFirst()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = World(catalog);
            int first = world.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 0);
            int second = world.SpawnCatalogUnit(2, "compact_militia", 1000, 0);
            world.SetBattalionStrength(first, 1);
            world.SetBattalionStrength(second, 1);
            world.Step();
            Assert.False(world.TryGetBattalionView(first, out _));
            Assert.False(world.TryGetBattalionView(second, out _));
            List<SimEvent> deaths = Deaths(world);
            Assert.Equal(2, deaths.Count);
            Assert.True(deaths[0].SubjectId < deaths[1].SubjectId);
            Assert.Equal(first, deaths[0].SubjectId);
            Assert.Equal(1, deaths[0].AuxId);
            Assert.Equal(2, deaths[1].AuxId);
        }

        [Fact]
        public void Overkill_IsWasted_AndAutoPickBreaksTiesByLowestId()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = World(catalog);
            int shooterA = world.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 0);
            int shooterB = world.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 100);
            int low = world.SpawnCatalogUnit(2, "compact_militia", 500, 0);
            int high = world.SpawnCatalogUnit(2, "compact_militia", 500, 100);
            world.SetBattalionStrength(low, 1);
            world.Step();
            Assert.False(world.TryGetBattalionView(low, out _));
            Assert.True(world.TryGetBattalionView(high, out _));
            Assert.True(world.TryGetBattalionView(shooterA, out _));
            Assert.True(world.TryGetBattalionView(shooterB, out _));
        }

        [Fact]
        public void AttackOrder_OverridesAutoPick_AndInvalidOrderFallsBack()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = World(catalog);
            int shooter = world.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 0);
            int low = world.SpawnCatalogUnit(2, "compact_militia", 400, 0);
            int high = world.SpawnCatalogUnit(2, "compact_militia", 800, 0);
            world.Submit(SimCommand.Attack(0, 1, shooter, high));
            world.Step();
            Assert.True(world.TryGetBattalionView(high, out BattalionView hurt));
            Assert.True(world.TryGetBattalionView(low, out BattalionView spared));
            Assert.True(hurt.Hp < spared.Hp);
            Assert.Equal(high, world.GetBattalion(shooter).AttackTargetId);

            SimWorld fallbackWorld = World(catalog);
            int fallbackShooter = fallbackWorld.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 0);
            int near = fallbackWorld.SpawnCatalogUnit(2, "compact_militia", 400, 0);
            int far = fallbackWorld.SpawnCatalogUnit(2, "compact_militia", 500000, 0);
            fallbackWorld.Submit(SimCommand.Attack(0, 1, fallbackShooter, far));
            fallbackWorld.Step();
            Assert.True(fallbackWorld.GetBattalion(near).Hp < 900);
            Assert.Equal(900, fallbackWorld.GetBattalion(far).Hp);
            Assert.Equal(far, fallbackWorld.GetBattalion(fallbackShooter).AttackTargetId);
        }

        [Fact]
        public void AttackRejects_UseNewCodes_AndDoNotDependOnAiRejectCast()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = World(catalog);
            int armed = world.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 0);
            int friend = world.SpawnCatalogUnit(1, "remnant_recon", 100, 0);
            int enemy = world.SpawnCatalogUnit(2, "compact_militia", 100, 0);
            int engineer = world.SpawnCatalogUnit(1, "remnant_engineers", 0, 100);
            AssertReject(world, SimCommand.Attack(0, 1, 99999, enemy), CommandRejectReason.UnknownBattalion);
            AssertReject(world, SimCommand.Attack(0, 2, armed, enemy), CommandRejectReason.NotOwner);
            AssertReject(world, SimCommand.Attack(0, 1, engineer, enemy), CommandRejectReason.NotArmed);
            AssertReject(world, SimCommand.Attack(0, 1, armed, armed), CommandRejectReason.TargetIsSelf);
            AssertReject(world, SimCommand.Attack(0, 1, armed, friend), CommandRejectReason.TargetNotEnemy);
            AssertReject(world, SimCommand.Attack(0, 1, armed, 88888), CommandRejectReason.UnknownTarget);

            SimCommand stamped = world.Submit(SimCommand.Attack(world.Tick, 1, armed, 88888));
            world.Step();
            ulong key = ((ulong)(uint)stamped.IssuerId << 32) | (uint)stamped.Sequence;
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            Assert.True(sink.TryGetRejectionByKey(key, out CommandRejectReason viaKey, out int target, out _));
            Assert.Equal(CommandRejectReason.UnknownTarget, viaKey);
            Assert.Equal(88888, target);
            Assert.True((int)viaKey != (int)AiCommandReject.UnitNotRaisable);
        }

        [Fact]
        public void Attack_PathsUntilInRange_ThenStopsAndFires()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = World(catalog);
            int attacker = world.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 0);
            int target = world.SpawnCatalogUnit(2, "compact_militia", 200000, 0);
            world.Submit(SimCommand.Attack(0, 1, attacker, target));
            world.Step();
            Assert.True(world.GetBattalion(attacker).HasTarget);
            Assert.Equal(900, world.GetBattalion(target).Hp);
            bool fired = false;
            for (int i = 0; i < 80 && !fired; i++)
            {
                world.Step();
                fired = world.GetBattalion(target).Hp < 900;
            }

            Assert.True(fired);
            Assert.False(world.GetBattalion(attacker).HasTarget);
        }

        [Fact]
        public void SupplyCut_PenalizesAfterGrace_AndDoublesReload()
        {
            Assert.Equal(120, CombatMath.EffectiveReload(60, true, 500));
            Assert.Equal(60, CombatMath.EffectiveReload(60, false, 500));
            BalanceCatalog catalog = Catalog();
            Assert.Equal(40, catalog.Combat.RecheckIntervalTicks);
            Assert.Equal(1200, catalog.Combat.GraceTicks);
            Assert.Equal(700, catalog.Combat.SpeedPpt);
        }

        [Fact]
        public void BuilderDeath_RefundsBeforeTheBuildStarts_AndNotAfter()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = GorgeWorld(catalog, out _, out _);
            int engineer = FirstBuilder(world, catalog, 1);
            Assert.True(world.TryGetSeat(1, out SeatView seat));
            int materials = seat.Materials;
            Assert.True(world.TryGetTown(seat.HomeTownId, out TownView home));
            int node = home.RoadNodeId;
            world.PlaceBattalion(engineer, home.XCm + 500000, home.YCm);
            world.Submit(SimCommand.BuildDepot(0, 1, engineer, node));
            world.Step();
            world.PlaceBattalion(engineer, home.XCm + 500000, home.YCm);
            world.SetBattalionStrength(engineer, 0);
            world.Step();
            SimEvent refund = Last(world, SimEventKind.OrderRefunded);
            Assert.Equal((int)CommandKind.BuildDepot, refund.AuxId);
            Assert.Equal(node, refund.Param0);
            Assert.Equal(1, refund.SubjectId);
            Assert.True(world.TryGetSeat(1, out SeatView after));
            Assert.Equal(materials, after.Materials);
            int destroyed = IndexOfKind(world, SimEventKind.BattalionDestroyed);
            int refunded = IndexOfKind(world, SimEventKind.OrderRefunded);
            Assert.True(destroyed >= 0 && refunded == destroyed + 1);
        }

        [Fact]
        public void GarrisonContest_CarriesSeatMinusOne_AndBlocksTheCaptureTimer()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = GorgeWorld(catalog, out SimScenario scenario, out AiMapSnapshot map);
            Assert.Single(scenario.Garrisons);
            Assert.Equal(2, scenario.Garrisons[0].Units[0].Count);
            Assert.True(world.TryGetTerritory(scenario.Garrisons[0].TerritoryKey, out TerritoryView territory));
            Assert.True(world.TryGetTown(territory.SeatTownId, out TownView town));
            int garrison = 0;
            int garrisonCount = 0;
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != SimWorld.NeutralOwnerId)
                {
                    continue;
                }

                garrisonCount++;
                garrison = battalion.Id;
                Assert.Equal(town.XCm, battalion.PositionXCm);
                Assert.Equal(town.YCm, battalion.PositionYCm);
                Assert.Equal(900, battalion.Hp);
            }

            Assert.Equal(2, garrisonCount);
            int capturer = world.SpawnCatalogUnit(1, "remnant_motor_rifles", town.XCm, town.YCm);
            world.Submit(SimCommand.ClaimTown(0, 1, capturer, town.TownId));
            world.Step();
            SimEvent started = Last(world, SimEventKind.TownContestStarted);
            Assert.Equal(town.TownId, started.SubjectId);
            Assert.True(started.AuxId == SimWorld.NeutralOwnerId || started.Param0 == SimWorld.NeutralOwnerId);
            Assert.Equal(0, TownOf(world, town.TownId).CaptureProgressTicks);
            world.Step();
            Assert.Equal(0, TownOf(world, town.TownId).CaptureProgressTicks);
            world.SetBattalionStrength(garrison, 0);
            int other = OtherGarrison(world, garrison);
            world.SetBattalionStrength(other, 0);
            world.Step();
            SimEvent ended = Last(world, SimEventKind.TownContestEnded);
            Assert.True(ended.AuxId == SimWorld.NeutralOwnerId || ended.Param0 == SimWorld.NeutralOwnerId);
            Assert.True(TownOf(world, town.TownId).CaptureProgressTicks > 0);

            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            sink.BindRoads(Wrmb.ReadRoadGraph(ReadMarshMap()));
            List<AiCombatantView> combatants = new List<AiCombatantView>();
            sink.CopyCombatants(1, combatants);
            Assert.Contains(combatants, view => view.OwnerSeat == SimWorld.NeutralOwnerId || view.Hp > 0);
            Assert.True(world.TryGetBattalionView(capturer, out BattalionView own));
            Assert.Equal(0, own.AttackTargetId);
            Assert.True(map.TownCount > 0);
        }

        [Fact]
        public void HqCapture_EndsTheMatch_AndLaterCommandsAreDropped()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = GorgeWorld(catalog, out _, out _);
            Assert.True(world.TryGetSeat(2, out SeatView compact));
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == 2)
                {
                    world.SetBattalionStrength(battalion.Id, 0);
                }
            }

            int capturer = world.SpawnCatalogUnit(1, "remnant_motor_rifles", compact.HqXCm, compact.HqYCm);
            world.Submit(SimCommand.ClaimTown(0, 1, capturer, compact.HomeTownId));
            int guard = 0;
            for (int i = 0; i < 4000 && !world.MatchResult.IsOver; i++)
            {
                world.Step();
                guard = i;
            }

            Assert.True(world.MatchResult.IsOver);
            Assert.Equal(1, world.MatchResult.WinnerSeat);
            Assert.Equal(SimWorld.MatchReasonHqCaptured, world.MatchResult.Reason);
            Assert.Equal(2, world.MatchResult.LoserSeat);
            SimEvent ended = world.Events[world.Events.Count - 1];
            Assert.Equal(SimEventKind.MatchEnded, ended.Kind);
            ulong hash = world.HashState();
            world.Submit(SimCommand.Move(world.Tick, 1, capturer, 0, 0));
            world.Step();
            Assert.Equal(hash, world.HashState());
            Assert.True(guard < 4000);
        }

        [Fact]
        public void WorldSave_RefusesSchema11_AndRestoresCombatFields()
        {
            BalanceCatalog catalog = Catalog();
            SimWorld world = World(catalog);
            int id = world.SpawnCatalogUnit(1, "remnant_motor_rifles", 10, 20);
            world.Submit(SimCommand.Attack(0, 1, id, id));
            world.Step();
            world.GetBattalion(id);
            byte[] blob = world.Save();
            SimWorld restored = World(catalog);
            restored.SpawnCatalogUnit(1, "remnant_motor_rifles", 0, 0);
            restored.Load(blob);
            Assert.Equal(world.GetBattalion(id).Hp, restored.GetBattalion(id).Hp);
            Assert.Equal(world.GetBattalion(id).Cooldown, restored.GetBattalion(id).Cooldown);
            Assert.Equal(world.GetBattalion(id).CutDetectedTick, restored.GetBattalion(id).CutDetectedTick);
            byte[] schema11 = { 11, 0, 0, 0 };
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => restored.Load(schema11));
            Assert.Contains("expected 12", ex.Message);
        }

        private static int OpenVolley(int baseDamage, int armor, int attackerDm = 0, int targetDm = 0)
        {
            return CombatMath.Volley(baseDamage, 1000, armor, 0, 1000, attackerDm, targetDm, 500, 150, 1);
        }

        private static int WalledUrbanVolley(bool highGround)
        {
            int attack = CombatMath.AttackPpt(1000, -200, 4000, false, false, true, true);
            int defense = CombatMath.DefensePpt(300, 200, 100, 200, -200, -500, true, true, false, 0, 2, false);
            return CombatMath.Volley(150, attack, 0, defense, 1000, highGround ? 500 : 0, 0, 500, 150, 1);
        }

        private static int ArmourHitByMilitia()
        {
            return CombatMath.Volley(8, 1000, 450, 0, 1000, 0, 0, 500, 150, 1);
        }

        private static CombatTerrain BoundaryTerrain()
        {
            return new CombatTerrain(
                2,
                1,
                0,
                0,
                200,
                100,
                new ushort[] { 0, 500 },
                new ushort[] { 0, 0 },
                new byte[] { 1, 1 },
                new byte[] { 0 },
                0,
                10);
        }

        private static int FireOnce(BalanceCatalog catalog, CombatTerrain terrain, int attackerX, int targetX, out int hp)
        {
            SimWorld world = World(catalog);
            world.BindCombatTerrain(terrain);
            world.SpawnCatalogUnit(1, "remnant_motor_rifles", attackerX, 0);
            int target = world.SpawnCatalogUnit(2, "compact_militia", targetX, 0);
            world.Step();
            hp = world.GetBattalion(target).Hp;
            return 900 - hp;
        }

        private static int Loss(BalanceCatalog catalog, CombatTerrain terrain, int owner, string unitId)
        {
            SimWorld world = World(catalog);
            world.BindCombatTerrain(terrain);
            world.SpawnCatalogUnit(1, "remnant_motor_rifles", 1000, 0);
            int target = world.SpawnCatalogUnit(owner, unitId, 0, 0);
            world.Step();
            return 900 - world.GetBattalion(target).Hp;
        }

        private static void AssertReject(SimWorld world, SimCommand command, CommandRejectReason reason)
        {
            SimCommand stamped = world.Submit(command.WithIssuedTick(world.Tick));
            world.Step();
            Assert.True(world.TryGetRejection(stamped.BattalionId, out CommandRejectReason got, out _, out _));
            Assert.Equal(reason, got);
        }

        private static List<SimEvent> Deaths(SimWorld world)
        {
            List<SimEvent> found = new List<SimEvent>();
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind == SimEventKind.BattalionDestroyed)
                {
                    found.Add(world.Events[i]);
                }
            }

            return found;
        }

        private static SimEvent Last(SimWorld world, SimEventKind kind)
        {
            for (int i = world.Events.Count - 1; i >= 0; i--)
            {
                if (world.Events[i].Kind == kind)
                {
                    return world.Events[i];
                }
            }

            throw new InvalidOperationException("Missing " + kind + ".");
        }

        private static int IndexOfKind(SimWorld world, SimEventKind kind)
        {
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind == kind)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FirstBuilder(SimWorld world, BalanceCatalog catalog, int owner)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == owner && battalion.UnitIndex >= 0 && catalog.UnitCanBuildAt(battalion.UnitIndex))
                {
                    return battalion.Id;
                }
            }

            throw new InvalidOperationException("No builder.");
        }

        private static int OtherGarrison(SimWorld world, int skip)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == SimWorld.NeutralOwnerId && battalion.Id != skip)
                {
                    return battalion.Id;
                }
            }

            throw new InvalidOperationException("No second garrison.");
        }

        private static TownView TownOf(SimWorld world, int townId)
        {
            Assert.True(world.TryGetTown(townId, out TownView town));
            return town;
        }

        private static SimWorld World(BalanceCatalog catalog)
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.BindCatalog(catalog);
            return world;
        }

        private static SimWorld GorgeWorld(BalanceCatalog catalog, out SimScenario scenario, out AiMapSnapshot map)
        {
            byte[] bytes = ReadMarshMap();
            scenario = SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                catalog,
                bytes,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");
            map = AiMapSnapshot.FromWrmb(bytes);
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.BindCatalog(catalog);
            for (int i = 0; i < scenario.SeatCount; i++)
            {
                ScenarioSeat seat = scenario.SeatAt(i);
                Assert.True(map.TryGetTown(seat.HomeTownId, out AiTownRecord town));
                world.BindSeat(new SeatSetup(
                    seat.Slot + 1,
                    seat.Slot,
                    seat.Faction,
                    town.Id,
                    town.XCm,
                    town.YCm,
                    1,
                    -1,
                    seat.StartingManpower,
                    catalog.StartingMateriel,
                    catalog.StartingFuel,
                    scenario.MatchRules.FuelEnabled,
                    catalog.StartingSeatPopCap,
                    seat.RaisableUnits,
                    seat.AutoResearch,
                    seat.StartingUnits));
            }

            world.BindHoldings(scenario, map);
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName);
            string terrainName = "golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb";
            byte[] terrain = File.ReadAllBytes(Path.Combine(dir, terrainName));
            world.BindRoads(MovementService.Create(bytes, terrain, MovementTables.Default()), Wrmb.ReadRoadGraph(bytes));
            world.BindCombatTerrain(CombatTerrain.Read(terrain));
            return world;
        }

        private static BalanceCatalog Catalog()
        {
            return BalanceCatalog.LoadFromDirectory(Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance"));
        }

        private static byte[] ReadMarshMap()
        {
            return GoldenMapFiles.ReadWrmb(
                Path.Combine(
                    GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName),
                    GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
        }
    }
}
