using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiCombatOrderTests
    {
        [Fact]
        public void MapReject_NamesCombatRejects_WithoutCastingEnums()
        {
            Assert.Equal(
                AiCommandReject.UnknownTarget,
                ForwardingAiCommandSink.MapReject(CommandRejectReason.UnknownTarget));
            Assert.Equal(
                AiCommandReject.TargetNotEnemy,
                ForwardingAiCommandSink.MapReject(CommandRejectReason.TargetNotEnemy));
            Assert.Equal(
                AiCommandReject.NotArmed,
                ForwardingAiCommandSink.MapReject(CommandRejectReason.NotArmed));
            Assert.Equal(
                AiCommandReject.TargetIsSelf,
                ForwardingAiCommandSink.MapReject(CommandRejectReason.TargetIsSelf));
            Assert.NotEqual((int)AiCommandReject.UnknownTarget, (int)CommandRejectReason.UnknownTarget);
            Assert.NotEqual((int)AiCommandReject.TargetNotEnemy, (int)CommandRejectReason.TargetNotEnemy);
            Assert.NotEqual((int)AiCommandReject.NotArmed, (int)CommandRejectReason.NotArmed);
            Assert.NotEqual((int)AiCommandReject.TargetIsSelf, (int)CommandRejectReason.TargetIsSelf);
            Assert.Equal(12, AiConstants.StateSchemaVersion);
        }

        [Fact]
        public void AttackFlow_ClearsGarrisonBeforeEnemyDefender()
        {
            Board board = Board.Create();
            int first = AiAttackDefend.PickTownAttackTarget(
                board.Combatants(),
                3,
                board.FarNode,
                board.EnemyX,
                board.EnemyY,
                board.ZoneRadius);
            Assert.Equal(board.GarrisonId, first);

            AiCombatantView[] reversed =
            {
                board.DefenderView(),
                board.ScoutView(),
                board.GarrisonView()
            };
            int same = AiAttackDefend.PickTownAttackTarget(
                reversed,
                reversed.Length,
                board.FarNode,
                board.EnemyX,
                board.EnemyY,
                board.ZoneRadius);
            Assert.Equal(first, same);

            board.RemoveGarrison();
            int second = AiAttackDefend.PickTownAttackTarget(
                board.Combatants(),
                2,
                board.FarNode,
                board.EnemyX,
                board.EnemyY,
                board.ZoneRadius);
            Assert.Equal(board.DefenderId, second);

            board.RestoreGarrison();
            board.Step(0);
            int attacks = 0;
            int attackTarget = 0;
            for (int i = 0; i < board.Sink.Log.Count; i++)
            {
                if (board.Sink.Log[i].Kind != AiIssuedKind.Attack)
                {
                    continue;
                }

                attacks++;
                attackTarget = board.Sink.Log[i].Arg1;
                Assert.NotEqual(board.NearOwnId, board.Sink.Log[i].Arg0);
            }

            Assert.Equal(1, attacks);
            Assert.Equal(board.GarrisonId, attackTarget);
            Assert.Equal(board.EnemyTown, board.Faction.AttackTargetTownId);
        }

        [Fact]
        public void CombatReject_BacksOffThatTarget()
        {
            Board board = Board.Create();
            board.Faction.AttackTargetTownId = board.EnemyTown;
            board.Sink.NextReject = AiCommandReject.UnknownTarget;
            board.Planner.IssueTownCombat(
                0,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.FarOwnId);
            Assert.Equal((int)AiCommandReject.UnknownTarget, board.Faction.LastRejectCode);
            Assert.Equal(AiAttackDefend.RejectBackoffStartTicks, board.Faction.RejectBackoffUntilTick);
            Assert.True(board.Owned.IsOrderBackedOff(
                board.Faction.FactionId,
                board.FarOwnId,
                CommandKind.Attack,
                board.GarrisonId,
                0));

            int attacks = CountKind(board.Sink, AiIssuedKind.Attack);
            board.Faction.RejectBackoffUntilTick = 0;
            board.Faction.LastRejectCode = 0;
            board.Sink.Tick = 1;
            board.Planner.IssueTownCombat(
                1,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.FarOwnId);
            int garrisonAttacks = 0;
            int defenderAttacks = 0;
            for (int i = 0; i < board.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = board.Sink.Log[i];
                if (cmd.Kind != AiIssuedKind.Attack || cmd.Tick != 1)
                {
                    continue;
                }

                if (cmd.Arg1 == board.GarrisonId)
                {
                    garrisonAttacks++;
                }

                if (cmd.Arg1 == board.DefenderId)
                {
                    defenderAttacks++;
                }
            }

            Assert.True(attacks >= 1);
            Assert.Equal(0, garrisonAttacks);
            Assert.Equal(1, defenderAttacks);

            byte[] blob = board.Owned.Save(1UL, 2UL, 3UL);
            Assert.Equal(12, blob[0] | (blob[1] << 8) | (blob[2] << 16) | (blob[3] << 24));
            AiFactionState loadedFaction = Board.MakeFaction(board.Faction.HomeTownId, board.HqNode);
            AiOwnedState loaded = new AiOwnedState(new[] { loadedFaction }, 2);
            loaded.Load(blob, 1UL, 2UL, 3UL);
            Assert.True(loaded.IsOrderBackedOff(
                loadedFaction.FactionId,
                board.FarOwnId,
                CommandKind.Attack,
                board.GarrisonId,
                0));
            Assert.Equal(12, AiConstants.StateSchemaVersion);
        }

        [Fact]
        public void Event15_ClearsOrdersIntentsAndAttackBackoff()
        {
            Board board = Board.Create();
            AiOrder order = default;
            order.Id = 1;
            order.Type = AiGoalType.ClaimTown;
            order.TargetTownId = board.EnemyTown;
            order.BattalionId = board.FarOwnId;
            board.Faction.Orders[board.Faction.OrderCount++] = order;

            AiPendingIntent intent = default;
            intent.Kind = AiIssuedKind.Attack;
            intent.BattalionId = board.FarOwnId;
            intent.TargetId = board.GarrisonId;
            intent.IssuedTick = 1;
            intent.Sequence = 9;
            board.Faction.EnqueueIntent(intent);
            board.Owned.NoteOrderReject(
                board.Faction.FactionId,
                board.FarOwnId,
                CommandKind.Attack,
                board.GarrisonId,
                1);

            AiAttackDefend.ClearDestroyedBattalion(board.Owned, board.GarrisonId);
            Assert.Equal(0, board.Faction.IntentCount);
            Assert.Equal(0, board.Owned.BackoffCount);
            Assert.Equal(1, board.Faction.OrderCount);

            AiAttackDefend.ClearDestroyedBattalion(board.Owned, board.FarOwnId);
            Assert.Equal(0, board.Faction.OrderCount);
        }

        [Fact]
        public void AttackTimeout_DropsTheIntentAndBacksOff()
        {
            Board board = Board.Create();
            AiPendingIntent intent = default;
            intent.Kind = AiIssuedKind.Attack;
            intent.BattalionId = board.FarOwnId;
            intent.TargetId = board.GarrisonId;
            intent.IssuedTick = 4;
            intent.Sequence = 3;
            board.Faction.EnqueueIntent(intent);

            board.Planner.ExpireAttackOrders(4 + AiCombatTable.AttackTimeoutTicks - 1, board.Faction);
            Assert.Equal(1, board.Faction.IntentCount);

            board.Planner.ExpireAttackOrders(4 + AiCombatTable.AttackTimeoutTicks, board.Faction);
            Assert.Equal(0, board.Faction.IntentCount);
            Assert.True(board.Owned.IsOrderBackedOff(
                board.Faction.FactionId,
                board.FarOwnId,
                CommandKind.Attack,
                board.GarrisonId,
                4 + AiCombatTable.AttackTimeoutTicks));

            board.Faction.AttackTargetTownId = board.EnemyTown;
            board.Planner.IssueTownCombat(
                4 + AiCombatTable.AttackTimeoutTicks,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.FarOwnId);
            for (int i = 0; i < board.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = board.Sink.Log[i];
                if (cmd.Kind == AiIssuedKind.Attack)
                {
                    Assert.NotEqual(board.GarrisonId, cmd.Arg1);
                }
            }
        }

        [Fact]
        public void HqRecall_UsesRuntimeNearestNode_AndReleasesWhenThreatIsGone()
        {
            Board board = Board.Create();
            board.Faction.HqNodeId = board.PastNode;
            board.Planner.SyncHqRecall(0, board.Faction, board.Towns, board.Towns.Length, board.Pins(), 3, board.Roads);
            Assert.Equal(1, CountHolds(board.Faction));
            Assert.Equal(board.NearOwnId, HoldBattalion(board.Faction));

            board.Faction.HqNodeId = board.HqNode;
            board.KeepOnlyFarEnemy();
            board.Planner.SyncHqRecall(1, board.Faction, board.Towns, board.Towns.Length, board.Pins(), 3, board.Roads);
            Assert.Equal(0, CountHolds(board.Faction));

            board.RestoreNearScout();
            board.Planner.SyncHqRecall(2, board.Faction, board.Towns, board.Towns.Length, board.Pins(), 3, board.Roads);
            Assert.Equal(1, CountHolds(board.Faction));
            board.RemoveScout();
            board.Planner.SyncHqRecall(3, board.Faction, board.Towns, board.Towns.Length, board.Pins(), 3, board.Roads);
            Assert.Equal(0, CountHolds(board.Faction));
        }

        [Fact]
        public void OneScoutNearHq_RecallsOneBattalion_AndTheOffensiveContinues()
        {
            Board first = Board.Create();
            Board second = Board.Create();
            first.Step(0);
            second.Step(0);
            Assert.Equal(first.Sink.Log.Count, second.Sink.Log.Count);
            for (int i = 0; i < first.Sink.Log.Count; i++)
            {
                AiIssuedCommand a = first.Sink.Log[i];
                AiIssuedCommand b = second.Sink.Log[i];
                Assert.Equal(a.Kind, b.Kind);
                Assert.Equal(a.Arg0, b.Arg0);
                Assert.Equal(a.Arg1, b.Arg1);
                Assert.Equal(a.Arg2, b.Arg2);
            }

            Assert.Equal(first.EnemyTown, first.Faction.AttackTargetTownId);
            Assert.Equal(1, CountHolds(first.Faction));
            Assert.Equal(first.NearOwnId, HoldBattalion(first.Faction));
            int hqMoves = 0;
            int attacks = 0;
            for (int i = 0; i < first.Sink.Log.Count; i++)
            {
                AiIssuedCommand cmd = first.Sink.Log[i];
                if (cmd.Kind == AiIssuedKind.Move && cmd.Arg1 == first.HomeX && cmd.Arg2 == first.HomeY)
                {
                    hqMoves++;
                    Assert.Equal(first.NearOwnId, cmd.Arg0);
                }

                if (cmd.Kind == AiIssuedKind.Attack)
                {
                    attacks++;
                    Assert.Equal(first.GarrisonId, cmd.Arg1);
                    Assert.NotEqual(first.NearOwnId, cmd.Arg0);
                }
            }

            Assert.Equal(1, hqMoves);
            Assert.Equal(1, attacks);
            Assert.Equal(40, AiCombatTable.RecallCap(40));
            Assert.True(AiAttackDefend.StrengthAllowsAttack(100, 40, AiCombatTable.HqRecallMarginPpt));
            Assert.Equal(1, AiCombatTable.CountToCover(new[] { 100 }, 1, 40));
        }

        [Fact]
        public void ArmouredThreat_RecallsMoreThanOneScout()
        {
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(
                Path.Combine(GoldenMapFiles.FindRepoRoot(), "data", "balance"));
            Assert.True(catalog.TryGetUnitTargetClass("remnant_armoured_battalion", out string armourClass));
            Assert.Equal(AiConstants.TargetClassArmored, armourClass);
            Assert.True(catalog.TryGetUnitTargetClass("compact_scouts", out string scoutClass));
            Assert.Equal(AiConstants.TargetClassSoft, scoutClass);
            Assert.True(catalog.TryGetUnitIndex("remnant_armoured_battalion", out int armourIndex));
            Assert.True(catalog.TryGetUnitIndex("compact_scouts", out int scoutIndex));
            int armourHp = catalog.UnitMaxHpAt(armourIndex);
            int scoutHp = catalog.UnitMaxHpAt(scoutIndex);
            Assert.True(armourHp > scoutHp);

            Board board = Board.Create();
            board.SetOwnHitPoints(scoutHp);
            board.PlaceNearThreat(board.ScoutId, scoutHp, scoutHp);
            board.Planner.SyncHqRecall(
                0,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.Roads);
            int scoutHolds = CountHolds(board.Faction);
            Assert.Equal(1, scoutHolds);

            AiCombatantView armour = board.ArmourView(armourHp, armourHp);
            board.PlaceNearThreat(armour);
            board.Planner.SyncHqRecall(
                1,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.Roads);
            int armourHolds = CountHolds(board.Faction);
            Assert.True(armourHolds > scoutHolds);
            Assert.True(armourHolds > 1);
        }

        [Fact]
        public void GarrisonInsideHqRadius_RecallsNothing()
        {
            Board board = Board.Create();
            board.PlaceNearGarrison(board.GarrisonView().MaxHp);
            board.Planner.SyncHqRecall(
                0,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.Roads);
            Assert.Equal(0, CountHolds(board.Faction));
        }

        [Fact]
        public void ReleaseHqHolds_KeepsTownDefendOrder()
        {
            Board board = Board.Create();
            AiOrder townDefend = default;
            townDefend.Id = 4;
            townDefend.Type = AiGoalType.DefendTown;
            townDefend.TargetTownId = board.Faction.HomeTownId;
            townDefend.BattalionId = board.OtherOwnId;
            townDefend.Score = 0;
            board.Faction.Orders[board.Faction.OrderCount++] = townDefend;
            board.Faction.NextOrderId = 5;

            board.Planner.SyncHqRecall(
                0,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.Roads);
            bool kept = false;
            for (int i = 0; i < board.Faction.OrderCount; i++)
            {
                AiOrder order = board.Faction.Orders[i];
                if (order.Id == townDefend.Id && order.Score == 0)
                {
                    kept = true;
                }
            }

            Assert.True(kept);
            Assert.Equal(1, CountHolds(board.Faction));
        }

        [Fact]
        public void HoldAtHq_DoesNotDropOrdersWhenTheListIsFull()
        {
            Board board = Board.Create();
            int cap = board.Faction.Orders.Length;
            for (int i = 0; i < cap; i++)
            {
                AiOrder order = default;
                order.Id = 50 + i;
                order.Type = AiGoalType.ClaimTown;
                order.TargetTownId = board.EnemyTown;
                order.BattalionId = 400 + i;
                board.Faction.Orders[i] = order;
            }

            board.Faction.OrderCount = cap;
            board.Planner.SyncHqRecall(
                0,
                board.Faction,
                board.Towns,
                board.Towns.Length,
                board.Pins(),
                3,
                board.Roads);
            Assert.Equal(cap, board.Faction.OrderCount);
            for (int i = 0; i < cap; i++)
            {
                Assert.Equal(50 + i, board.Faction.Orders[i].Id);
                Assert.Equal(AiGoalType.ClaimTown, board.Faction.Orders[i].Type);
            }

            Assert.Equal(0, CountHolds(board.Faction));
        }

        private static int CountKind(RecordingAiCommandSink sink, AiIssuedKind kind)
        {
            int count = 0;
            for (int i = 0; i < sink.Log.Count; i++)
            {
                if (sink.Log[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountHolds(AiFactionState faction)
        {
            int count = 0;
            for (int i = 0; i < faction.OrderCount; i++)
            {
                AiOrder order = faction.Orders[i];
                if (order.Type == AiGoalType.DefendTown
                    && order.TargetTownId == faction.HomeTownId
                    && order.Score == AiCombatTable.HqHoldScore)
                {
                    count++;
                }
            }

            return count;
        }

        private static int HoldBattalion(AiFactionState faction)
        {
            for (int i = 0; i < faction.OrderCount; i++)
            {
                AiOrder order = faction.Orders[i];
                if (order.Type == AiGoalType.DefendTown
                    && order.TargetTownId == faction.HomeTownId
                    && order.Score == AiCombatTable.HqHoldScore)
                {
                    return order.BattalionId;
                }
            }

            return 0;
        }

        private sealed class Board : IAiTownQuery, ISimWorldView
        {
            public readonly int HqNode;
            public readonly int NearNode;
            public readonly int FarNode;
            public readonly int PastNode;
            public readonly int HomeTown;
            public readonly int EnemyTown;
            public readonly int HomeX;
            public readonly int HomeY;
            public readonly int EnemyX;
            public readonly int EnemyY;
            public readonly int ZoneRadius;
            public readonly int NearOwnId;
            public readonly int FarOwnId;
            public readonly int OtherOwnId;
            public readonly int ScoutId;
            public readonly int ArmourId;
            public readonly int GarrisonId;
            public readonly int DefenderId;
            public readonly RoadGraph Roads;
            public readonly AiAttackTownInfo[] Towns;
            public readonly RecordingAiCommandSink Sink = new RecordingAiCommandSink();
            public readonly AiFactionState Faction;
            public readonly AiOwnedState Owned;
            public readonly AiAttackDefend Planner;
            private readonly Dictionary<int, AiTownReadback> _towns = new Dictionary<int, AiTownReadback>();
            private readonly Dictionary<int, SimBattalion> _own = new Dictionary<int, SimBattalion>();
            private readonly List<AiCombatantView> _combat = new List<AiCombatantView>();
            private readonly string _lineUnit;

            private Board(
                int hqNode,
                int nearNode,
                int farNode,
                int pastNode,
                int homeTown,
                int enemyTown,
                int homeX,
                int homeY,
                int enemyX,
                int enemyY,
                int zoneRadius,
                int nearOwnId,
                int farOwnId,
                int otherOwnId,
                int scoutId,
                int armourId,
                int garrisonId,
                int defenderId,
                RoadGraph roads,
                AiAttackTownInfo[] towns,
                string lineUnit)
            {
                HqNode = hqNode;
                NearNode = nearNode;
                FarNode = farNode;
                PastNode = pastNode;
                HomeTown = homeTown;
                EnemyTown = enemyTown;
                HomeX = homeX;
                HomeY = homeY;
                EnemyX = enemyX;
                EnemyY = enemyY;
                ZoneRadius = zoneRadius;
                NearOwnId = nearOwnId;
                FarOwnId = farOwnId;
                OtherOwnId = otherOwnId;
                ScoutId = scoutId;
                ArmourId = armourId;
                GarrisonId = garrisonId;
                DefenderId = defenderId;
                Roads = roads;
                Towns = towns;
                _lineUnit = lineUnit;
                Faction = MakeFaction(homeTown, hqNode);
                Owned = new AiOwnedState(new[] { Faction }, 2);
                Planner = new AiAttackDefend(Sink, this, Sink, this, Owned);
                _towns[homeTown] = new AiTownReadback(1, false, 1, 2, 0, 0);
                _towns[enemyTown] = new AiTownReadback(2, false, 2, 1, 0, 0);
                AddOwn(nearOwnId, 0, 1000);
                AddOwn(farOwnId, 1000, 8000);
                AddOwn(otherOwnId, 1000, 8000);
                RestoreNearScout();
                _combat.Add(GarrisonView());
                _combat.Add(DefenderView());
            }

            public static Board Create()
            {
                int hqNode = 1;
                int nearNode = 2;
                int farNode = 3;
                int pastNode = 4;
                int homeTown = 5;
                int enemyTown = 6;
                int homeX = 4;
                int homeY = 4;
                int enemyX = 1000;
                int enemyY = 0;
                int zone = 40;
                int longEdge = AiCombatTable.HqThreatRadiusCm + 100000;
                RoadGraph roads = Wrmb.ReadRoadGraph(
                    new RoadGraphBuilder()
                        .AddNode(hqNode, 0, 0)
                        .AddNode(nearNode, 0, 1000)
                        .AddNode(farNode, enemyX, enemyY)
                        .AddNode(pastNode, 1000, 8000)
                        .AddEdge(1, hqNode, nearNode, 2000, RoadTier.Paved, RoadSurface.Asphalt)
                        .AddEdge(2, hqNode, farNode, longEdge, RoadTier.Paved, RoadSurface.Asphalt)
                        .AddEdge(3, farNode, pastNode, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                        .ToBlob());
                string line = AiMvpAttackTests.PinWithRole(1, 0, 0, hqNode, AiConstants.RoleLineInfantry).UnitId;
                AiAttackTownInfo[] towns =
                {
                    new AiAttackTownInfo(homeTown, 1, hqNode, homeX, homeY, true, 1UL, zone),
                    new AiAttackTownInfo(enemyTown, 2, farNode, enemyX, enemyY, true, 2UL, zone)
                };
                return new Board(
                    hqNode,
                    nearNode,
                    farNode,
                    pastNode,
                    homeTown,
                    enemyTown,
                    homeX,
                    homeY,
                    enemyX,
                    enemyY,
                    zone,
                    101,
                    102,
                    103,
                    201,
                    202,
                    301,
                    205,
                    roads,
                    towns,
                    line);
            }

            public static AiFactionState MakeFaction(int homeTown, int hqNode)
            {
                AiFactionState faction = new AiFactionState(
                    1,
                    AiConstants.PersonalitySettler,
                    AiConstants.DifficultyNormal,
                    0,
                    40,
                    8,
                    1,
                    60,
                    8,
                    1,
                    0,
                    50000,
                    4,
                    4,
                    AiPosture.Attack,
                    new SimRandom(1UL),
                    new SimRandom(3UL),
                    new SimRandom(5UL),
                    new SimRandom(7UL));
                faction.HomeTownId = homeTown;
                faction.HomeTerritoryId = 1;
                faction.HqNodeId = hqNode;
                return faction;
            }

            public void Step(int tick)
            {
                Faction.WorkUnitsThisTick = 0;
                Sink.Tick = tick;
                AiMvpAttackTests.Harness donor = AiMvpAttackTests.Harness.Line();
                int[] network = { HqNode };
                AiBattalionPin[] pins = Pins();
                Planner.Tick(
                    tick,
                    Faction,
                    donor.Seat,
                    Towns,
                    Towns.Length,
                    pins,
                    pins.Length,
                    network,
                    1,
                    Roads,
                    1500,
                    AiCombatTable.HqThreatRadiusCm + 200000,
                    0UL,
                    0,
                    donor.SeatView,
                    donor.Catalog,
                    donor.Buildings,
                    donor.LineUnitsPerAntiArmour,
                    donor.MinGarrisonPerTown,
                    1);
            }

            public AiBattalionPin[] Pins()
            {
                return new[]
                {
                    new AiBattalionPin(NearOwnId, 0, 1000, NearNode, _lineUnit, false),
                    new AiBattalionPin(FarOwnId, 1000, 8000, PastNode, _lineUnit, false),
                    new AiBattalionPin(OtherOwnId, 1000, 8000, PastNode, _lineUnit, false)
                };
            }

            public AiCombatantView[] Combatants()
            {
                AiCombatantView[] copy = new AiCombatantView[_combat.Count];
                for (int i = 0; i < _combat.Count; i++)
                {
                    copy[i] = _combat[i];
                }

                return copy;
            }

            public AiCombatantView GarrisonView()
            {
                return new AiCombatantView(GarrisonId, SimWorld.NeutralOwnerId, EnemyX, EnemyY, FarNode, 100, 100);
            }

            public AiCombatantView DefenderView()
            {
                return new AiCombatantView(DefenderId, 2, EnemyX, EnemyY, FarNode, 100, 100);
            }

            public AiCombatantView ScoutView()
            {
                return new AiCombatantView(ScoutId, 2, 0, 1000, NearNode, 40, 40);
            }

            public AiCombatantView ArmourView(int hp, int maxHp)
            {
                return new AiCombatantView(ArmourId, 2, 0, 1000, NearNode, hp, maxHp);
            }

            public void PlaceNearGarrison(int hp)
            {
                DropCombatant(ScoutId);
                DropCombatant(GarrisonId);
                _combat.Add(new AiCombatantView(GarrisonId, SimWorld.NeutralOwnerId, 0, 1000, NearNode, hp, hp));
            }

            public void RemoveGarrison()
            {
                DropCombatant(GarrisonId);
            }

            public void RestoreGarrison()
            {
                DropCombatant(GarrisonId);
                _combat.Add(GarrisonView());
            }

            public void RemoveScout()
            {
                DropCombatant(ScoutId);
            }

            public void KeepOnlyFarEnemy()
            {
                RemoveScout();
            }

            public void RestoreNearScout()
            {
                DropCombatant(ScoutId);
                _combat.Add(ScoutView());
            }

            public void SetOwnHitPoints(int hp)
            {
                int[] ids = { NearOwnId, FarOwnId, OtherOwnId };
                for (int i = 0; i < ids.Length; i++)
                {
                    SimBattalion battalion = _own[ids[i]];
                    battalion.Hp = hp;
                    battalion.MaxHp = hp;
                    _own[ids[i]] = battalion;
                }
            }

            public void PlaceNearThreat(int id, int hp, int maxHp)
            {
                PlaceNearThreat(new AiCombatantView(id, 2, 0, 1000, NearNode, hp, maxHp));
            }

            public void PlaceNearThreat(AiCombatantView threat)
            {
                DropCombatant(ScoutId);
                DropCombatant(ArmourId);
                DropCombatant(threat.Id);
                _combat.Add(threat);
            }

            public bool TryGet(int townId, out AiTownReadback readback)
            {
                return _towns.TryGetValue(townId, out readback);
            }

            public int Tick
            {
                get { return Sink.Tick; }
            }

            public MatchResultView MatchResult
            {
                get { return new MatchResultView(false); }
            }

            public int BattalionCount
            {
                get { return _own.Count; }
            }

            public int TownCount
            {
                get { return Towns.Length; }
            }

            public void CopyOwnBattalionIds(int factionId, List<int> into)
            {
                into.Clear();
            }

            public void CopyVisibleEnemyBattalionIds(int factionId, List<int> into)
            {
                into.Clear();
            }

            public void CopyCombatants(int factionId, List<AiCombatantView> into)
            {
                into.Clear();
                for (int i = 0; i < _combat.Count; i++)
                {
                    into.Add(_combat[i]);
                }
            }

            public int CountOwnBattalions(int factionId)
            {
                return _own.Count;
            }

            public bool TryGetOwnBattalion(int factionId, int battalionId, out SimBattalion battalion)
            {
                return _own.TryGetValue(battalionId, out battalion);
            }

            public bool TryGetSeat(int factionId, out SeatView view)
            {
                view = default;
                return false;
            }

            public int FuelOf(int factionId)
            {
                return 0;
            }

            public bool TryGetBattalionView(int battalionId, out BattalionView view)
            {
                view = default;
                return false;
            }

            public bool TryCopyProductionQueue(int factionId, List<ProductionQueueItem> into)
            {
                if (into != null)
                {
                    into.Clear();
                }

                return false;
            }

            public bool TryGetTown(int townId, out TownView view)
            {
                for (int i = 0; i < Towns.Length; i++)
                {
                    if (Towns[i].Id != townId)
                    {
                        continue;
                    }

                    int owner = townId == HomeTown ? 1 : 2;
                    view = new TownView(
                        townId,
                        Towns[i].TerritoryId,
                        Towns[i].TerritoryStableKey,
                        true,
                        Towns[i].XCm,
                        Towns[i].YCm,
                        0,
                        0,
                        0,
                        Towns[i].ZoneRadiusCm,
                        0,
                        owner,
                        0,
                        0,
                        0,
                        0,
                        false);
                    return true;
                }

                view = default;
                return false;
            }

            public void CopyTowns(List<TownView> into)
            {
                into.Clear();
            }

            public bool TryGetTerritory(ulong key, out TerritoryView view)
            {
                view = default;
                return false;
            }

            public void CopyDepots(List<DepotView> into)
            {
                into.Clear();
            }

            public void CopyEventsOfTick(int tick, List<SimEvent> into)
            {
                into.Clear();
            }

            public void CopyEventsInRange(int fromTick, int toTick, List<SimEvent> into)
            {
                into.Clear();
            }

            public int GetNodeTerritoryId(int nodeId)
            {
                return 0;
            }

            public int GetNearestNodeId(int xCm, int yCm)
            {
                if (Roads.NodeCount < 1)
                {
                    return 0;
                }

                int bestIndex = 0;
                int bestId = Roads.GetNodeId(0);
                long bestDist = long.MaxValue;
                for (int i = 0; i < Roads.NodeCount; i++)
                {
                    long dx = (long)Roads.GetNodeXCm(i) - xCm;
                    long dy = (long)Roads.GetNodeYCm(i) - yCm;
                    long dist = (dx * dx) + (dy * dy);
                    int id = Roads.GetNodeId(i);
                    if (dist < bestDist || (dist == bestDist && id < bestId))
                    {
                        bestDist = dist;
                        bestId = id;
                        bestIndex = i;
                    }
                }

                return Roads.GetNodeId(bestIndex);
            }

            public CommandRejectReason PreviewDepotPlacement(int factionId, int nodeId, out int param0, out int param1)
            {
                param0 = 0;
                param1 = 0;
                return CommandRejectReason.None;
            }

            private void AddOwn(int id, int xCm, int yCm)
            {
                SimBattalion battalion = default;
                battalion.Id = id;
                battalion.OwnerId = 1;
                battalion.PositionXCm = xCm;
                battalion.PositionYCm = yCm;
                battalion.Hp = 100;
                battalion.MaxHp = 100;
                _own[id] = battalion;
            }

            private void DropCombatant(int id)
            {
                for (int i = _combat.Count - 1; i >= 0; i--)
                {
                    if (_combat[i].Id == id)
                    {
                        _combat.RemoveAt(i);
                    }
                }
            }
        }
    }
}
