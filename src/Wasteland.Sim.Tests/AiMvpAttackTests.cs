using System;
using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiMvpAttackTests
    {
        [Fact]
        public void StrengthRatio_GatesCommit()
        {
            Assert.False(AiAttackDefend.StrengthAllowsAttack(2, 2, 1500));
            Assert.True(AiAttackDefend.StrengthAllowsAttack(3, 2, 1500));
            Assert.True(AiAttackDefend.StrengthAllowsAttack(2, 1, 1500));
            Assert.False(AiAttackDefend.StrengthAllowsAttack(0, 1, 1500));
        }

        [Fact]
        public void PickNearestEnemyTown_PrefersCloserThenLowerId()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[20] = Enemy(2, 1);
            harness.Towns[30] = Enemy(2, 1);
            int farFirst = harness.Planner.PickNearestEnemyTown(
                harness.Faction, harness.TownRows, 3, harness.Roads, 1);
            Assert.Equal(20, farFirst);
            harness.Towns[20] = Enemy(2, 1);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            int stillNear = harness.Planner.PickNearestEnemyTown(
                harness.Faction, harness.TownRows, 3, harness.Roads, 1);
            Assert.Equal(20, stillNear);
        }

        [Fact]
        public void PickAttackTarget_PrefersEnemyHqWhenInSupply()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[20] = Enemy(2, 1);
            harness.Towns[30] = Enemy(2, 1);
            int target = harness.Planner.PickAttackTarget(
                harness.Faction,
                harness.TownRows,
                3,
                harness.Roads,
                1,
                harness.Network,
                1,
                100000,
                0UL,
                0);
            Assert.Equal(30, target);
        }

        [Fact]
        public void PickAttackTarget_UsesNearestTownWhenHqOutOfSupply()
        {
            Harness harness = Harness.Line();
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = Own(1);
            harness.Towns[20] = Enemy(2, 1);
            harness.Towns[30] = Enemy(2, 1);
            int target = harness.Planner.PickAttackTarget(
                harness.Faction,
                harness.TownRows,
                3,
                harness.Roads,
                1,
                harness.Network,
                1,
                harness.SupplyReachCm,
                0UL,
                0);
            Assert.Equal(20, target);
        }

        [Fact]
        public void PickAttackTarget_PrizeAtBoundaryTieGoesToPrize()
        {
            Assert.Equal(30, PickPrize(Harness.Fork(1000, 1300), 3UL, 300));
        }

        [Fact]
        public void PickAttackTarget_PrizeJustBeyondBonusLoses()
        {
            Assert.Equal(20, PickPrize(Harness.Fork(1000, 1301), 3UL, 300));
        }

        [Fact]
        public void PickAttackTarget_PrizeJustInsideBonusWins()
        {
            Assert.Equal(30, PickPrize(Harness.Fork(1000, 1299), 3UL, 300));
        }

        [Fact]
        public void PickAttackTarget_BonusZeroUsesNearestThenLowerId()
        {
            Assert.Equal(20, PickPrize(Harness.Fork(1000, 1300), 3UL, 0));
            Assert.Equal(20, PickPrize(Harness.Fork(), 3UL, 0));
        }

        [Fact]
        public void PickAttackTarget_UnreachablePrizeLosesToReachable()
        {
            Assert.Equal(20, PickPrize(Harness.Fork(1000, 0, true, false), 3UL, 300));
        }

        [Fact]
        public void PickAttackTarget_ReachablePrizeBeatsUnreachableWithoutOverflow()
        {
            Assert.Equal(30, PickPrize(Harness.Fork(0, 1000, false, true), 3UL, 300));
        }

        [Fact]
        public void PickAttackTarget_UnownedPrizeIsEligible()
        {
            Harness harness = Harness.Fork(1000, 1300);
            harness.Towns[10] = Own(1);
            harness.Towns[20] = Enemy(2, 1);
            harness.Towns[30] = Unowned();
            int target = harness.Planner.PickAttackTarget(
                harness.Faction,
                harness.TownRows,
                3,
                harness.Roads,
                1,
                harness.Network,
                1,
                100000,
                3UL,
                300);
            Assert.Equal(30, target);
        }

        [Fact]
        public void PickAttackTarget_UnownedNonPrizeIsSkipped()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[20] = Unowned();
            harness.Towns[30] = Enemy(2, 1);
            int target = harness.Planner.PickAttackTarget(
                harness.Faction,
                harness.TownRows,
                3,
                harness.Roads,
                1,
                harness.Network,
                1,
                100000,
                0UL,
                300);
            Assert.Equal(30, target);
        }

        [Fact]
        public void PickAttackTarget_UnownedPrizeBeatsCloserUnownedNonPrize()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[20] = Unowned();
            harness.Towns[30] = Unowned();
            int target = harness.Planner.PickAttackTarget(
                harness.Faction,
                harness.TownRows,
                3,
                harness.Roads,
                1,
                harness.Network,
                1,
                100000,
                3UL,
                300);
            Assert.Equal(30, target);
        }

        [Fact]
        public void Attack_IssuesMoveAndClaimWhenRatioMetAndInSupply()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(5, 1), Idle(6, 1));
            harness.Tick();
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(AiIssuedKind.ClaimTown, harness.Sink.Log[1].Kind);
            Assert.Equal(30, harness.Sink.Log[1].Arg1);
            Assert.Equal(30, harness.Faction.AttackTargetTownId);
        }

        [Fact]
        public void Attack_IssuesMoveAndClaimOnUnownedPrize()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Unowned();
            harness.PrizeTerritoryKey = 3UL;
            harness.PrizeTargetBonusPpt = 300;
            harness.SetBattalions(Idle(5, 1));
            harness.Tick();
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(AiIssuedKind.ClaimTown, harness.Sink.Log[1].Kind);
            Assert.Equal(30, harness.Sink.Log[1].Arg1);
            Assert.Equal(30, harness.Faction.AttackTargetTownId);
        }

        [Fact]
        public void CompactAttackGroup_ExcludesFootUnits()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Unowned();
            harness.PrizeTerritoryKey = 3UL;
            harness.PrizeTargetBonusPpt = 300;
            AiBattalionPin rifle = Idle(5, 1);
            AiBattalionPin at = Pin(6, 2, AiConstants.RoleAntiTank);
            Assert.True(harness.Catalog.TryGetMovementClass(rifle.UnitId, out MovementClass rifleClass));
            Assert.Equal(MovementClass.Wheeled, rifleClass);
            Assert.True(harness.Catalog.TryGetMovementClass(at.UnitId, out MovementClass atClass));
            Assert.Equal(MovementClass.Foot, atClass);
            harness.SetBattalions(rifle, at);
            harness.Tick();
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(5, harness.Sink.Log[0].Arg0);
            Assert.Equal(2000, harness.Sink.Log[0].Arg1);
            Assert.Equal(0, harness.Sink.Log[0].Arg2);
            Assert.Equal(AiIssuedKind.ClaimTown, harness.Sink.Log[1].Kind);
            Assert.Equal(5, harness.Sink.Log[1].Arg0);
            Assert.Equal(30, harness.Sink.Log[1].Arg1);
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                if (cmd.Kind == AiIssuedKind.Move || cmd.Kind == AiIssuedKind.ClaimTown)
                {
                    Assert.NotEqual(6, cmd.Arg0);
                }
            }
        }

        [Fact]
        public void CompactFootAntiTank_StaysHomeOnDefend()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = new AiTownReadback(1, true, 1, 1, 2, 3);
            harness.Towns[30] = Unowned();
            harness.PrizeTerritoryKey = 3UL;
            harness.PrizeTargetBonusPpt = 300;
            AiBattalionPin at = Pin(6, 1, AiConstants.RoleAntiTank);
            harness.SetBattalions(at);
            harness.Tick();
            Assert.Equal(10, harness.Faction.DefendTownId);
            Assert.Equal(0, harness.Faction.AttackTargetTownId);
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(6, harness.Sink.Log[0].Arg0);
            Assert.Equal(0, harness.Sink.Log[0].Arg1);
            Assert.Equal(0, harness.Sink.Log[0].Arg2);
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                Assert.NotEqual(AiIssuedKind.ClaimTown, cmd.Kind);
                if (cmd.Kind == AiIssuedKind.Move)
                {
                    Assert.False(cmd.Arg1 == 2000 && cmd.Arg2 == 0);
                }
            }
        }

        [Fact]
        public void CompactPrizeGroup_WaitsForTaskForceRiflesThenLeavesTogether()
        {
            Harness harness = Harness.Line();
            harness.TaskForceTargetSize = 2;
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Unowned();
            harness.PrizeTerritoryKey = 3UL;
            harness.PrizeTargetBonusPpt = 300;
            AiBattalionPin scout = new AiBattalionPin(4, 0, 0, 1, "compact_scouts", false);
            harness.SetBattalions(Idle(5, 1), scout);
            harness.Tick();
            Assert.Equal(30, harness.Faction.AttackTargetTownId);
            AssertNoPrizeMarch(harness, 0);

            harness.SetBattalions(Idle(5, 1), Idle(7, 1), scout);
            int from = harness.Sink.Log.Count;
            harness.Tick(1);
            AssertPrizeMarchMembers(harness, from, new[] { 4, 5, 7 });
            Assert.Equal(3, harness.Faction.OrderCount);
        }

        [Fact]
        public void Attack_SkipsWhenBelowRatio()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Enemy(2, 4);
            harness.SetBattalions(Idle(5, 1));
            harness.Tick();
            Assert.Empty(harness.Sink.Log);
        }

        [Fact]
        public void ForwardDepot_MovesEngineerThenBuildsBeforeAttack()
        {
            Harness harness = Harness.Line();
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(5, 1), Engineer(7, 1));
            harness.Tick();
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(7, harness.Sink.Log[0].Arg0);
            Assert.Equal(AiIssuedKind.BuildDepot, harness.Sink.Log[1].Kind);
            Assert.Equal(7, harness.Sink.Log[1].Arg0);
            Assert.Equal(2, harness.Sink.Log[1].Arg1);
            Assert.Equal(0, harness.Faction.DepotSiteTick);
            Assert.Equal(1, harness.Faction.DepotActiveTick);
            harness.Tick(2);
            Assert.True(harness.Sink.Log.Count >= 4);
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[2].Kind);
            Assert.Equal(AiIssuedKind.ClaimTown, harness.Sink.Log[3].Kind);
        }

        [Fact]
        public void SaveLoadContinue_ForwardDepot_RestoresDepotTicksAndReplaysCommands()
        {
            Harness live = Harness.Line();
            live.SupplyReachCm = 1500;
            live.Towns[10] = Own(1);
            live.Towns[30] = Enemy(2, 1);
            live.SetBattalions(Idle(5, 1), Engineer(7, 1));
            live.Tick(5);
            Assert.True(live.Faction.DepotSiteTick > 0);
            Assert.True(live.Faction.DepotActiveTick > live.Faction.DepotSiteTick);
            int siteTick = live.Faction.DepotSiteTick;
            int activeTick = live.Faction.DepotActiveTick;
            int depotNode = 0;
            for (int i = 0; i < live.Sink.Log.Count; i++)
            {
                if (live.Sink.Log[i].Kind == AiIssuedKind.BuildDepot)
                {
                    depotNode = live.Sink.Log[i].Arg1;
                    break;
                }
            }

            Assert.True(depotNode > 0);
            Assert.True(live.Sink.TryGet(live.Faction.FactionId, depotNode, out AiDepotReadback placed));
            Assert.Equal(siteTick, placed.SiteTick);
            Assert.Equal(activeTick, placed.ActiveTick);
            int logAtSave = live.Sink.Log.Count;
            ulong hashAtSave = HashOf(live.Faction);
            AiOwnedState saved = new AiOwnedState(new[] { live.Faction }, 1);
            byte[] blob = saved.Save(0xA11UL, 0xB22UL, 0xC33UL);

            Harness restored = Harness.Line();
            restored.SupplyReachCm = 1500;
            restored.Towns[10] = Own(1);
            restored.Towns[30] = Enemy(2, 1);
            restored.SetBattalions(Idle(5, 1), Engineer(7, 1));
            AiOwnedState loaded = new AiOwnedState(new[] { restored.Faction }, 1);
            loaded.Load(blob, 0xA11UL, 0xB22UL, 0xC33UL);
            Assert.Equal(siteTick, restored.Faction.DepotSiteTick);
            Assert.Equal(activeTick, restored.Faction.DepotActiveTick);
            Assert.Equal(hashAtSave, HashOf(restored.Faction));
            restored.Sink.RecordSite(restored.Faction.FactionId, depotNode, siteTick, activeTick);

            live.Tick(6);
            live.Tick(7);
            restored.Tick(6);
            restored.Tick(7);
            Assert.Equal(HashOf(live.Faction), HashOf(restored.Faction));
            Assert.Equal(siteTick, restored.Faction.DepotSiteTick);
            Assert.Equal(activeTick, restored.Faction.DepotActiveTick);
            Assert.Equal(live.Sink.Log.Count - logAtSave, restored.Sink.Log.Count);
            for (int i = 0; i < restored.Sink.Log.Count; i++)
            {
                AiIssuedCommand expected = live.Sink.Log[logAtSave + i];
                AiIssuedCommand actual = restored.Sink.Log[i];
                Assert.Equal(expected.Tick, actual.Tick);
                Assert.Equal(expected.FactionId, actual.FactionId);
                Assert.Equal(expected.Kind, actual.Kind);
                Assert.Equal(expected.Arg0, actual.Arg0);
                Assert.Equal(expected.Arg1, actual.Arg1);
                Assert.Equal(expected.Arg2, actual.Arg2);
                Assert.Equal(expected.Arg3, actual.Arg3);
                Assert.Equal(expected.UnitId, actual.UnitId);
            }
        }

        [Fact]
        public void ForwardDepot_RaisesEngineerWhenNoneIdle()
        {
            Harness harness = Harness.Line();
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(5, 1), Idle(6, 1));
            harness.Tick();
            Assert.Single(harness.Sink.Log);
            Assert.Equal(AiIssuedKind.RaiseBattalion, harness.Sink.Log[0].Kind);
            Assert.True(harness.Catalog.TryGetRole(harness.Sink.Log[0].UnitId, out string raiseRole));
            Assert.Equal(AiConstants.RoleEngineer, raiseRole);
        }

        [Fact]
        public void ForwardDepot_SendsEscortWhenPathNearEnemy()
        {
            Harness harness = Harness.Line();
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = Own(1);
            harness.Towns[20] = new AiTownReadback(0, false, 2, 2, 0, 0);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(5, 1), Engineer(7, 1));
            harness.Tick();
            Assert.True(harness.Sink.Log.Count >= 3);
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(5, harness.Sink.Log[0].Arg0);
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[1].Kind);
            Assert.Equal(7, harness.Sink.Log[1].Arg0);
            Assert.Equal(AiIssuedKind.BuildDepot, harness.Sink.Log[2].Kind);
        }

        [Fact]
        public void Defend_PreemptsAttackWhenOwnTownContestedAndOutnumbered()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = new AiTownReadback(1, true, 1, 1, 2, 3);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(8, 2), Idle(9, 3));
            harness.Tick();
            Assert.Equal(10, harness.Faction.DefendTownId);
            Assert.Equal(0, harness.Faction.AttackTargetTownId);
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(8, harness.Sink.Log[0].Arg0);
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                Assert.NotEqual(AiIssuedKind.ClaimTown, harness.Sink.Log[i].Kind);
            }
        }

        [Fact]
        public void Defend_OwnHqOutranksOtherContestedTown()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = new AiTownReadback(1, true, 1, 1, 2, 2);
            harness.Towns[20] = new AiTownReadback(1, true, 1, 1, 2, 9);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(8, 1));
            harness.Tick();
            Assert.Equal(10, harness.Faction.DefendTownId);
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(0, harness.Sink.Log[0].Arg1);
        }

        [Fact]
        public void Reject_BacksOffThenRetries()
        {
            Harness harness = Harness.Line();
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(5, 1), Idle(6, 1));
            harness.Sink.NextReject = AiCommandReject.Rejected;
            harness.Tick(0);
            Assert.Single(harness.Sink.Log);
            Assert.Equal((int)AiCommandReject.Rejected, harness.Faction.LastRejectCode);
            int until = harness.Faction.RejectBackoffUntilTick;
            Assert.Equal(AiAttackDefend.RejectBackoffStartTicks, until);
            harness.Tick(1);
            Assert.Single(harness.Sink.Log);
            harness.Tick(until);
            Assert.True(harness.Sink.Log.Count > 1);
        }

        [Fact]
        public void DepotOutOfReach_BacksOffThenRetries()
        {
            Harness harness = Harness.Line();
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(5, 1), Engineer(7, 2));
            harness.Sink.NextReject = AiCommandReject.DepotOutOfReach;
            harness.Tick(0);
            Assert.Equal((int)AiCommandReject.DepotOutOfReach, harness.Faction.LastRejectCode);
            int until = harness.Faction.RejectBackoffUntilTick;
            Assert.Equal(AiAttackDefend.RejectBackoffStartTicks, until);
            harness.Tick(1);
            int count = harness.Sink.Log.Count;
            harness.Tick(until);
            Assert.True(harness.Sink.Log.Count > count);
        }

        [Fact]
        public void Raise_SkipsUnitNotInSeatCatalog()
        {
            Harness harness = Harness.Line();
            harness.Seat = InfantryOnlySeat();
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = Own(1);
            harness.Towns[30] = Enemy(2, 1);
            harness.SetBattalions(Idle(5, 1), Idle(6, 1));
            harness.Tick();
            Assert.Empty(harness.Sink.Log);
        }

        [Fact]
        public void Raise_BlocksTierIIUntilFixtureResearchCompletes()
        {
            IAiUnitCatalog catalog = AiBalanceTablesTests.LoadBalance().UnitCatalog;
            AiScenarioSeatView seat = new AiScenarioSeatView(
                0,
                "remnant_command",
                null,
                1UL,
                300,
                FixtureAiScenarioSeats.RemnantRaisableUnits,
                FixtureAiScenarioSeats.DefaultBuildableBuildings,
                new[] { new AiAutoResearch("tier2", 50) });
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Tier2ResearchTicks = 10;
            view.Refresh(seat, 59);
            Assert.Equal(1, view.CurrentTier);
            string tierTwoId = FirstRoleOrTarget(
                seat,
                catalog,
                null,
                AiConstants.TargetClassArmored);
            Assert.False(string.IsNullOrEmpty(tierTwoId));
            Assert.False(seat.AllowsUnitForTier(tierTwoId, view.CurrentTier, catalog));
            view.Refresh(seat, 60);
            Assert.Equal(2, view.CurrentTier);
            Assert.True(seat.AllowsUnitForTier(tierTwoId, view.CurrentTier, catalog));
            string lineId = FirstRoleOrTarget(seat, catalog, AiConstants.RoleLineInfantry, null);
            Assert.True(seat.AllowsUnit(lineId));
            Assert.True(seat.AllowsUnitForTier(lineId, 1, catalog));
        }

        [Fact]
        public void HashInto_IncludesAttackDefendState_AndIsDeterministic()
        {
            Harness a = Harness.Line();
            Harness b = Harness.Line();
            a.Towns[10] = Own(1);
            b.Towns[10] = Own(1);
            a.Towns[30] = Enemy(2, 1);
            b.Towns[30] = Enemy(2, 1);
            a.SetBattalions(Idle(5, 1), Idle(6, 1));
            b.SetBattalions(Idle(5, 1), Idle(6, 1));
            a.Tick();
            b.Tick();
            Assert.Equal(HashOf(a.Faction), HashOf(b.Faction));
            a.Faction.AttackTargetTownId = 99;
            Assert.NotEqual(HashOf(a.Faction), HashOf(b.Faction));
        }

        [Fact]
        public void NormalDifficulty_RequiresAttackStrengthRatioPpt()
        {
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace(
                    "      \"battalionManpowerCost\": 20,\n      \"attackStrengthRatioPpt\": 1500",
                    "      \"battalionManpowerCost\": 20");
            FormatException ex = Assert.Throws<FormatException>(() => AiBalanceTables.Parse(
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json"),
                difficulty));
            Assert.Equal(AiBalanceTables.RuleIdAttackStrengthRatioPpt, ex.Message);
        }

        private static ulong HashOf(AiFactionState faction)
        {
            AiOwnedState owned = new AiOwnedState(new[] { faction }, 1);
            XxHash64Builder hash = new XxHash64Builder();
            owned.HashInto(hash);
            return hash.Digest();
        }

        private static AiTownReadback Own(int factionId)
        {
            return new AiTownReadback(factionId, false, factionId, 1, 0, 0);
        }

        private static AiTownReadback Enemy(int factionId, int strength)
        {
            return new AiTownReadback(factionId, false, factionId, strength, 0, 0);
        }

        private static AiTownReadback Unowned()
        {
            return new AiTownReadback(0, false, 0, 0, 0, 0);
        }

        internal static string FirstRoleOrTarget(
            AiScenarioSeatView seat,
            IAiUnitCatalog catalog,
            string? role,
            string? targetClass)
        {
            string best = string.Empty;
            string[] ids = seat.RaisableUnits;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (role != null)
                {
                    if (!catalog.TryGetRole(id, out string have) || have != role)
                    {
                        continue;
                    }
                }

                if (targetClass != null)
                {
                    if (!catalog.TryGetTargetClass(id, out string haveClass) || haveClass != targetClass)
                    {
                        continue;
                    }
                }

                if (best.Length < 1)
                {
                    best = id;
                }
            }

            return best;
        }

        private static void AssertNoPrizeMarch(Harness harness, int from)
        {
            for (int i = from; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                Assert.NotEqual(AiIssuedKind.ClaimTown, cmd.Kind);
                if (cmd.Kind == AiIssuedKind.Move)
                {
                    Assert.False(cmd.Arg1 == 2000 && cmd.Arg2 == 0);
                }
            }
        }

        private static void AssertPrizeMarchMembers(Harness harness, int from, int[] ids)
        {
            bool[] moved = new bool[ids.Length];
            bool[] claimed = new bool[ids.Length];
            for (int i = from; i < harness.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = harness.Sink.Log[i];
                if (cmd.Kind != AiIssuedKind.Move && cmd.Kind != AiIssuedKind.ClaimTown)
                {
                    continue;
                }

                int member = IndexOf(ids, cmd.Arg0);
                Assert.True(member >= 0, "prize march issued for a battalion outside the group");
                if (cmd.Kind == AiIssuedKind.Move)
                {
                    Assert.Equal(2000, cmd.Arg1);
                    Assert.Equal(0, cmd.Arg2);
                    moved[member] = true;
                }
                else
                {
                    Assert.Equal(30, cmd.Arg1);
                    claimed[member] = true;
                }
            }

            for (int i = 0; i < ids.Length; i++)
            {
                Assert.True(moved[i], "prize member " + ids[i] + " did not Move");
                Assert.True(claimed[i], "prize member " + ids[i] + " did not ClaimTown");
            }
        }

        private static int IndexOf(int[] ids, int id)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private static AiBattalionPin Idle(int id, int nodeId)
        {
            return Pin(id, nodeId, AiConstants.RoleLineInfantry);
        }

        private static AiBattalionPin Engineer(int id, int nodeId)
        {
            return Pin(id, nodeId, AiConstants.RoleEngineer);
        }

        internal static AiBattalionPin PinWithRole(int id, int xCm, int yCm, int nodeId, string role)
        {
            return PinWithRole(id, xCm, yCm, nodeId, role, false);
        }

        internal static AiBattalionPin PinWithRole(
            int id,
            int xCm,
            int yCm,
            int nodeId,
            string role,
            bool assigned)
        {
            AiScenarioSeatView seat = FixtureAiScenarioSeats.LaunchSeat("homestead_compact", 1UL);
            IAiUnitCatalog catalog = AiBalanceTablesTests.LoadBalance().UnitCatalog;
            string unitId = FirstRoleOrTarget(seat, catalog, role, null);
            return new AiBattalionPin(id, xCm, yCm, nodeId, unitId, assigned);
        }

        private static AiBattalionPin Pin(int id, int nodeId, string role)
        {
            return PinWithRole(id, nodeId * 1000, 0, nodeId, role);
        }

        private static int PickPrize(Harness harness, ulong prizeKey, int bonusPpt)
        {
            harness.Towns[10] = Own(1);
            harness.Towns[20] = Enemy(2, 1);
            harness.Towns[30] = Enemy(2, 1);
            return harness.Planner.PickAttackTarget(
                harness.Faction,
                harness.TownRows,
                3,
                harness.Roads,
                1,
                harness.Network,
                1,
                100000,
                prizeKey,
                bonusPpt);
        }

        private static AiScenarioSeatView InfantryOnlySeat()
        {
            IAiUnitCatalog catalog = AiBalanceTablesTests.LoadBalance().UnitCatalog;
            AiScenarioSeatView full = FixtureAiScenarioSeats.LaunchSeat("homestead_compact", 1UL);
            string lineId = FirstRoleOrTarget(full, catalog, AiConstants.RoleLineInfantry, null);
            return FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                0,
                "homestead_compact",
                string.Empty,
                1UL,
                300,
                new[] { lineId },
                FixtureAiScenarioSeats.DefaultBuildableBuildings,
                FixtureAiScenarioSeats.DefaultResearch)).SeatAt(0);
        }

        internal sealed class Harness : IAiTownQuery
        {
            public readonly RecordingAiCommandSink Sink = new RecordingAiCommandSink();
            public readonly AiAttackDefend Planner;
            public readonly AiFactionState Faction;
            public readonly RoadGraph Roads;
            public readonly AiAttackTownInfo[] TownRows;
            public readonly Dictionary<int, AiTownReadback> Towns = new Dictionary<int, AiTownReadback>();
            public readonly int[] Network = { 1 };
            public readonly FixtureAiSeatView SeatView = new FixtureAiSeatView();
            public readonly IAiUnitCatalog Catalog;
            public IAiBuildingCatalog Buildings;
            public int SupplyReachCm = 100000;
            public ulong PrizeTerritoryKey;
            public int PrizeTargetBonusPpt;
            public int LineUnitsPerAntiArmour;
            public int MinGarrisonPerTown;
            public int TaskForceTargetSize = 1;
            public AiScenarioSeatView Seat;
            private AiBattalionPin[] _battalions = System.Array.Empty<AiBattalionPin>();

            private Harness(RoadGraph roads, AiAttackTownInfo[] towns, int workUnitsPerTick)
            {
                Roads = roads;
                TownRows = towns;
                Planner = new AiAttackDefend(Sink, this, Sink);
                Faction = new AiFactionState(
                    1,
                    AiConstants.PersonalitySettler,
                    AiConstants.DifficultyNormal,
                    0,
                    40,
                    4,
                    1,
                    60,
                    8,
                    1,
                    0,
                    workUnitsPerTick,
                    4,
                    4,
                    AiPosture.Attack,
                    new SimRandom(1UL),
                    new SimRandom(3UL),
                    new SimRandom(5UL),
                    new SimRandom(7UL));
                Faction.HqNodeId = 1;
                Faction.HomeTownId = 10;
                Faction.HomeTerritoryId = 1;
                Seat = FixtureAiScenarioSeats.LaunchSeat("homestead_compact", 1UL);
                AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
                Catalog = tables.UnitCatalog;
                Buildings = AiBuildingCatalog.Parse(
                    "{\"buildings\":{\"supply_depot\":{\"category\":\"economy\",\"role\":\"supply_depot\"}}}");
                AiPersonalityProfile settler = tables.GetPersonality(AiConstants.PersonalitySettler);
                LineUnitsPerAntiArmour = settler.LineUnitsPerAntiArmour;
                MinGarrisonPerTown = settler.MinGarrisonPerTown;
                SeatView.Tier2ResearchTicks = tables.Tier2ResearchTicks;
                SeatView.Manpower = Seat.StartingManpower;
                PrizeTerritoryKey = 0UL;
                PrizeTargetBonusPpt = 0;
            }

            public static Harness Line()
            {
                return Line(50000);
            }

            public static Harness Line(int workUnitsPerTick)
            {
                RoadGraph roads = Wrmb.ReadRoadGraph(
                    new RoadGraphBuilder()
                        .AddNode(1, 0, 0)
                        .AddNode(2, 1000, 0)
                        .AddNode(3, 2000, 0)
                        .AddEdge(1, 1, 2, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                        .AddEdge(2, 2, 3, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                        .ToBlob());
                return new Harness(
                    roads,
                    new[]
                    {
                        new AiAttackTownInfo(10, 1, 1, 0, 0, true, 1UL),
                        new AiAttackTownInfo(20, 2, 2, 1000, 0, false, 2UL),
                        new AiAttackTownInfo(30, 3, 3, 2000, 0, true, 3UL)
                    },
                    workUnitsPerTick);
            }

            public static Harness Fork()
            {
                return Fork(1000, 1000, true, true);
            }

            public static Harness Fork(int toward20Cm, int toward30Cm)
            {
                return Fork(toward20Cm, toward30Cm, true, true);
            }

            public static Harness Fork(int toward20Cm, int toward30Cm, bool connect20, bool connect30)
            {
                RoadGraphBuilder builder = new RoadGraphBuilder()
                    .AddNode(1, 0, 0)
                    .AddNode(2, 1000, 1000)
                    .AddNode(3, 1000, -1000);
                if (connect20)
                {
                    builder.AddEdge(1, 1, 2, toward20Cm, RoadTier.Paved, RoadSurface.Asphalt);
                }

                if (connect30)
                {
                    builder.AddEdge(2, 1, 3, toward30Cm, RoadTier.Paved, RoadSurface.Asphalt);
                }

                return new Harness(
                    Wrmb.ReadRoadGraph(builder.ToBlob()),
                    new[]
                    {
                        new AiAttackTownInfo(10, 1, 1, 0, 0, true, 1UL),
                        new AiAttackTownInfo(20, 2, 2, 1000, 1000, false, 2UL),
                        new AiAttackTownInfo(30, 3, 3, 1000, -1000, false, 3UL)
                    },
                    50000);
            }

            public void SetBattalions(params AiBattalionPin[] pins)
            {
                _battalions = pins;
            }

            public void Tick()
            {
                Tick(Sink.Tick);
            }

            public void Tick(int tick)
            {
                Faction.WorkUnitsThisTick = 0;
                Sink.Tick = tick;
                int[] network = Network;
                int networkCount = 1;
                if (Sink.TryGet(Faction.FactionId, 2, out AiDepotReadback depot) && depot.IsActive(tick))
                {
                    network = new[] { 1, 2 };
                    networkCount = 2;
                }

                AiCommandReject injected = Sink.NextReject;
                int from = Sink.Log.Count;
                SeatView.Refresh(Seat, tick);
                Planner.Tick(
                    tick,
                    Faction,
                    Seat,
                    TownRows,
                    TownRows.Length,
                    _battalions,
                    _battalions.Length,
                    network,
                    networkCount,
                    Roads,
                    1500,
                    SupplyReachCm,
                    PrizeTerritoryKey,
                    PrizeTargetBonusPpt,
                    SeatView,
                    Catalog,
                    Buildings,
                    LineUnitsPerAntiArmour,
                    MinGarrisonPerTown,
                    TaskForceTargetSize);
                AssertNoAiCausedTierOrPopRequest(from, injected);
            }

            private void AssertNoAiCausedTierOrPopRequest(int from, AiCommandReject injected)
            {
                for (int i = from; i < Sink.Log.Count; i++)
                {
                    AiIssuedCommand cmd = Sink.Log[i];
                    AiCommandReject reject = Sink.RejectOf(cmd.Id);
                    if (reject == AiCommandReject.TierLocked || reject == AiCommandReject.PopCapReached)
                    {
                        Assert.Equal(injected, reject);
                    }

                    if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                    {
                        continue;
                    }

                    Assert.True(Seat.AllowsUnitForTier(cmd.UnitId, SeatView.CurrentTier, Catalog));
                    Assert.True(Catalog.TryGetTier(cmd.UnitId, out int tier));
                    Assert.True(tier <= SeatView.CurrentTier);

                    if (SeatView.PopCap > 0)
                    {
                        Assert.True(SeatView.PopUsed < SeatView.PopCap);
                    }
                }
            }

            public bool TryGet(int townId, out AiTownReadback readback)
            {
                return Towns.TryGetValue(townId, out readback);
            }
        }
    }
}
