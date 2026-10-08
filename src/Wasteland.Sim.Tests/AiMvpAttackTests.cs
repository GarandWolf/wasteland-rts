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
                100000);
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
                harness.SupplyReachCm);
            Assert.Equal(20, target);
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
            Assert.Equal(AiConstants.UnitKindEngineer, harness.Sink.Log[0].Arg0);
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
            Assert.Equal((int)AiCommandReject.UnitNotRaisable, harness.Faction.LastRejectCode);
        }

        [Fact]
        public void Raise_BlocksTierIIBeforeUnlockTick()
        {
            AiScenarioSeatView seat = new AiScenarioSeatView(
                "homestead_compact",
                "unknown_raider_v9",
                10,
                300,
                new[]
                {
                    AiConstants.UnitKindInfantry,
                    AiConstants.UnitKindEngineer,
                    AiConstants.UnitKindTierII
                },
                new[] { AiConstants.BuildingTagDepot },
                new AiAutoResearch(50, 2, 10));
            Assert.False(seat.AllowsUnit(AiConstants.UnitKindTierII, 59));
            Assert.True(seat.AllowsUnit(AiConstants.UnitKindTierII, 60));
            Assert.Equal(1, seat.CurrentTierAt(59));
            Assert.Equal(2, seat.CurrentTierAt(60));
            Assert.True(seat.AllowsUnit(AiConstants.UnitKindInfantry, 0));
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

        private static AiBattalionPin Idle(int id, int nodeId)
        {
            return new AiBattalionPin(id, nodeId * 1000, 0, nodeId, AiConstants.UnitKindInfantry, false);
        }

        private static AiBattalionPin Engineer(int id, int nodeId)
        {
            return new AiBattalionPin(id, nodeId * 1000, 0, nodeId, AiConstants.UnitKindEngineer, false);
        }

        private static AiScenarioSeatView InfantryOnlySeat()
        {
            return FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                "homestead_compact",
                string.Empty,
                10,
                300,
                new[] { AiConstants.UnitKindInfantry },
                new[] { AiConstants.BuildingTagDepot },
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
            public int SupplyReachCm = 100000;
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
                Seat = FixtureAiScenarioSeats.LaunchSeat("homestead_compact", 10);
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
                        new AiAttackTownInfo(10, 1, 1, 0, 0, true),
                        new AiAttackTownInfo(20, 2, 2, 1000, 0, false),
                        new AiAttackTownInfo(30, 3, 3, 2000, 0, true)
                    },
                    workUnitsPerTick);
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
                    SeatView);
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

                    if (cmd.Arg0 == AiConstants.UnitKindTierII)
                    {
                        Assert.True(SeatView.CurrentTier >= 2);
                    }

                    if (cmd.Arg0 == AiConstants.UnitKindTierIII)
                    {
                        Assert.True(SeatView.CurrentTier >= 3);
                    }

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
