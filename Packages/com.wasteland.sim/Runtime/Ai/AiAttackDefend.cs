using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    internal readonly struct AiAttackTownInfo
    {
        public readonly int Id;
        public readonly int TerritoryId;
        public readonly int NodeId;
        public readonly int XCm;
        public readonly int YCm;
        public readonly bool IsHq;
        public readonly ulong TerritoryStableKey;
        public readonly int ZoneRadiusCm;

        public AiAttackTownInfo(
            int id,
            int territoryId,
            int nodeId,
            int xCm,
            int yCm,
            bool isHq,
            ulong territoryStableKey)
            : this(id, territoryId, nodeId, xCm, yCm, isHq, territoryStableKey, 0)
        {
        }

        public AiAttackTownInfo(
            int id,
            int territoryId,
            int nodeId,
            int xCm,
            int yCm,
            bool isHq,
            ulong territoryStableKey,
            int zoneRadiusCm)
        {
            Id = id;
            TerritoryId = territoryId;
            NodeId = nodeId;
            XCm = xCm;
            YCm = yCm;
            IsHq = isHq;
            TerritoryStableKey = territoryStableKey;
            ZoneRadiusCm = zoneRadiusCm;
        }
    }

    /// <summary>
    /// Attack the nearest enemy town, preferring the enemy HQ once it is in
    /// supply. Place a forward depot with an engineer first when the target is
    /// beyond supply reach. When that depot cannot be paid for, switch to an
    /// in-supply town or march to the supply edge. A seat that is too weak
    /// keeps raising combat units while it can pay; idle engineers do not
    /// block that. Defend own HQ first; defence outranks attack.
    /// </summary>
    internal sealed class AiAttackDefend
    {
        public const int RejectBackoffStartTicks = 8;
        public const int RejectBackoffMaxTicks = 128;

        private readonly IAiCommandSink _commands;
        private readonly IAiTownQuery _towns;
        private readonly IAiDepotQuery _depots;
        private readonly ISimWorldView? _world;
        private readonly AiOwnedState? _owned;
        private readonly AiRoadSupply _roads = new AiRoadSupply();
        private IAiSeatView _seatView = new FixtureAiSeatView();
        private IAiUnitCatalog _catalog = AiUnitCatalog.Empty;
        private IAiBuildingCatalog _buildings = AiBuildingCatalog.Empty;
        private int _lineUnitsPerAntiArmour = 2;
        private int _minGarrisonPerTown;
        private int _taskForceTargetSize = 1;
        private bool _budgetDeferred;
        private string[]? _queuedUnitIds;
        private int _queuedCount;
        private readonly string[] _pendingUnitIdScratch = new string[AiConstants.PendingCommandCapacity];
        private string[]? _pendingUnitIds;
        private int _pendingCount;
        private readonly List<DepotView> _depotScratch = new List<DepotView>();
        private readonly List<AiCombatantView> _combatScratch = new List<AiCombatantView>();
        private readonly List<int> _marchedScratch = new List<int>();
        private readonly int[] _waypointTown = new int[8];
        private readonly int[] _waypointSites = new int[8];
        private readonly int[] _waypointCached = new int[8];
        private readonly int[] _hqOutSites = new int[8];
        private readonly int[] _hqOutCached = new int[8];
        private readonly int[] _hqOutValue = new int[8];
        private readonly int[] _edgeCached = new int[8];
        private readonly int[] _edgeSites = new int[8];
        private readonly int[] _edgeNode = new int[8];
        private readonly int[] _edgeX = new int[8];
        private readonly int[] _edgeY = new int[8];
        private bool _forceHqMarch;
        private int[] _rankId = Array.Empty<int>();
        private int[] _rankDist = Array.Empty<int>();
        private int[] _rankCommit = Array.Empty<int>();
        private int[] _rankStrength = Array.Empty<int>();
        private int _rankCount;

        public AiAttackDefend(IAiCommandSink commands, IAiTownQuery towns, IAiDepotQuery depots)
            : this(commands, towns, depots, null, null)
        {
        }

        public AiAttackDefend(
            IAiCommandSink commands,
            IAiTownQuery towns,
            IAiDepotQuery depots,
            ISimWorldView? world)
            : this(commands, towns, depots, world, null)
        {
        }

        public AiAttackDefend(
            IAiCommandSink commands,
            IAiTownQuery towns,
            IAiDepotQuery depots,
            ISimWorldView? world,
            AiOwnedState? owned)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _towns = towns ?? throw new ArgumentNullException(nameof(towns));
            _depots = depots ?? throw new ArgumentNullException(nameof(depots));
            _world = world;
            _owned = owned;
        }

        public static bool StrengthAllowsAttack(int ownStrength, int defenderStrength, int attackStrengthRatioPpt)
        {
            if (attackStrengthRatioPpt < 1 || ownStrength < 1)
            {
                return false;
            }

            return (long)ownStrength * SimConfig.PptScale
                >= (long)attackStrengthRatioPpt * defenderStrength;
        }

        public void Tick(
            int tick,
            AiFactionState faction,
            AiScenarioSeatView seat,
            AiAttackTownInfo[] towns,
            int townCount,
            AiBattalionPin[] battalions,
            int battalionCount,
            int[] networkNodeIds,
            int networkCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int attackStrengthRatioPpt,
            int supplyReachCm,
            ulong prizeTerritoryKey,
            int prizeTargetBonusPpt,
            IAiSeatView seatView,
            IAiUnitCatalog catalog,
            IAiBuildingCatalog buildings,
            int lineUnitsPerAntiArmour,
            int minGarrisonPerTown,
            int taskForceTargetSize = 1,
            string[]? queuedUnitIds = null,
            int queuedCount = 0)
        {
            if (faction == null)
            {
                throw new ArgumentNullException(nameof(faction));
            }

            if (seatView == null)
            {
                throw new ArgumentNullException(nameof(seatView));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (buildings == null)
            {
                throw new ArgumentNullException(nameof(buildings));
            }

            if (lineUnitsPerAntiArmour < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(lineUnitsPerAntiArmour));
            }

            _catalog = catalog;
            _buildings = buildings;
            if (taskForceTargetSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(taskForceTargetSize));
            }

            _lineUnitsPerAntiArmour = lineUnitsPerAntiArmour;
            _minGarrisonPerTown = minGarrisonPerTown;
            _taskForceTargetSize = taskForceTargetSize;

            if (towns == null)
            {
                throw new ArgumentNullException(nameof(towns));
            }

            if (_commands is RecordingAiCommandSink recorded)
            {
                recorded.Tick = tick;
            }

            ExpireAttackOrders(tick, faction);
            PollLateReject(tick, faction);
            if (faction.RejectBackoffUntilTick > 0 && tick < faction.RejectBackoffUntilTick)
            {
                return;
            }

            int reservedManpower;
            int reservedPop;
            int reservedMateriel;
            int reservedFuel;
            SumPendingRaises(
                _commands,
                tick,
                faction.FactionId,
                catalog,
                out reservedManpower,
                out reservedPop,
                out reservedMateriel,
                out reservedFuel);
            if (!seatView.FuelEnabled)
            {
                reservedFuel = 0;
            }

            ReservedSeatView reserved = new ReservedSeatView(seatView);
            reserved.Add(reservedManpower, reservedPop, reservedMateriel, reservedFuel);
            _seatView = reserved;
            _queuedUnitIds = queuedUnitIds;
            _queuedCount = 0;
            if (queuedUnitIds != null)
            {
                _queuedCount = queuedCount;
                if (_queuedCount > queuedUnitIds.Length)
                {
                    _queuedCount = queuedUnitIds.Length;
                }
            }

            _pendingUnitIds = _pendingUnitIdScratch;
            _pendingCount = CopyPendingRaiseUnitIds(
                _commands,
                tick,
                faction.FactionId,
                _pendingUnitIdScratch);

            SortTowns(towns, townCount);
            SortBattalions(battalions, battalionCount);
            _budgetDeferred = false;
            _marchedScratch.Clear();

            if (TryDefend(tick, faction, towns, townCount, battalions, battalionCount, graph))
            {
                SyncHqRecall(tick, faction, towns, townCount, battalions, battalionCount, graph);
                if (_budgetDeferred)
                {
                    ClearDecision(faction);
                }

                return;
            }

            TryAttack(
                tick,
                faction,
                seat,
                towns,
                townCount,
                battalions,
                battalionCount,
                networkNodeIds,
                networkCount,
                graph,
                attackStrengthRatioPpt,
                supplyReachCm,
                prizeTerritoryKey,
                prizeTargetBonusPpt,
                seatView);
            SyncHqRecall(tick, faction, towns, townCount, battalions, battalionCount, graph);
            if (faction.AttackTargetTownId != 0)
            {
                IssueTownCombat(tick, faction, towns, townCount, battalions, battalionCount, 0);
            }

            if (_budgetDeferred)
            {
                ClearDecision(faction);
            }
        }

        private static void ClearDecision(AiFactionState faction)
        {
            faction.AttackTargetTownId = 0;
            faction.DefendTownId = 0;
        }

        private bool DeferIfExhausted(AiSupplyResult result)
        {
            if (!result.IsBudgetExhausted)
            {
                return false;
            }

            _budgetDeferred = true;
            return true;
        }

        public int PickAttackTarget(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int fromNodeId,
            int[] networkNodeIds,
            int networkCount,
            int supplyReachCm,
            ulong prizeTerritoryKey,
            int prizeTargetBonusPpt,
            bool inSupplyOnly = false)
        {
            if (inSupplyOnly)
            {
                AiSupplyResult filled = _roads.FillFromSources(graph, networkNodeIds, networkCount, faction);
                if (DeferIfExhausted(filled))
                {
                    return 0;
                }
            }

            int nearestId = 0;
            int nearestDist = AiConstants.UnreachableCm;
            bool nearestPrize = false;
            int hqId = 0;
            int hqDist = AiConstants.UnreachableCm;
            bool hqPrize = false;
            int hqNodeId = 0;
            bool seatZonesPresent = TownListHasSeatZones(towns, townCount);
            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                if (readback.OwnerFactionId == faction.FactionId)
                {
                    continue;
                }

                bool isPrize = prizeTerritoryKey != 0UL && town.TerritoryStableKey == prizeTerritoryKey;
                if (!IsAttackableTown(town, isPrize, seatZonesPresent))
                {
                    continue;
                }

                if (readback.OwnerFactionId == 0 && !isPrize)
                {
                    continue;
                }

                if (inSupplyOnly)
                {
                    if (!graph.TryGetNodeIndex(town.NodeId, out int townIndex))
                    {
                        continue;
                    }

                    if (_roads.NetworkDistanceAt(townIndex) > supplyReachCm)
                    {
                        continue;
                    }
                }

                AiSupplyResult dist = _roads.DistanceCm(graph, fromNodeId, town.NodeId, faction);
                if (DeferIfExhausted(dist))
                {
                    return 0;
                }

                int d = dist.IsReached ? dist.DistanceCm : AiConstants.UnreachableCm;
                if (BetterTarget(d, isPrize, town.Id, nearestDist, nearestPrize, nearestId, prizeTargetBonusPpt))
                {
                    nearestDist = d;
                    nearestPrize = isPrize;
                    nearestId = town.Id;
                }

                if (town.IsHq && BetterTarget(d, isPrize, town.Id, hqDist, hqPrize, hqId, prizeTargetBonusPpt))
                {
                    hqDist = d;
                    hqPrize = isPrize;
                    hqId = town.Id;
                    hqNodeId = town.NodeId;
                }
            }

            if (hqId != 0)
            {
                AiSupplyResult supply = _roads.DistanceToNearestCm(
                    graph,
                    hqNodeId,
                    networkNodeIds,
                    networkCount,
                    faction);
                if (DeferIfExhausted(supply))
                {
                    return 0;
                }

                if (supply.IsReached && supply.DistanceCm <= supplyReachCm)
                {
                    return hqId;
                }
            }

            return nearestId;
        }

        public int PickNearestEnemyTown(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int fromNodeId)
        {
            int[] none = Array.Empty<int>();
            return PickAttackTarget(faction, towns, townCount, graph, fromNodeId, none, 0, 0, 0UL, 0);
        }

        private static bool IsUnreachableDist(int dist)
        {
            return dist >= AiConstants.UnreachableCm;
        }

        private static int ComparePrizeToOther(int prizeDist, int otherDist, int prizeTargetBonusPpt)
        {
            long left = (long)prizeDist * SimConfig.PptScale;
            long right = (long)otherDist * ((long)SimConfig.PptScale + prizeTargetBonusPpt);
            return left.CompareTo(right);
        }

        private static bool TieBreakPrizeThenId(
            bool isPrize,
            int id,
            bool bestIsPrize,
            int bestId,
            int prizeTargetBonusPpt)
        {
            if (prizeTargetBonusPpt > 0)
            {
                if (isPrize && !bestIsPrize)
                {
                    return true;
                }

                if (!isPrize && bestIsPrize)
                {
                    return false;
                }
            }

            return id < bestId;
        }

        private static bool BetterTarget(
            int dist,
            bool isPrize,
            int id,
            int bestDist,
            bool bestIsPrize,
            int bestId,
            int prizeTargetBonusPpt)
        {
            if (bestId == 0)
            {
                return true;
            }

            bool unreachable = IsUnreachableDist(dist);
            bool bestUnreachable = IsUnreachableDist(bestDist);
            if (unreachable != bestUnreachable)
            {
                return bestUnreachable;
            }

            if (unreachable)
            {
                return TieBreakPrizeThenId(isPrize, id, bestIsPrize, bestId, prizeTargetBonusPpt);
            }

            if (isPrize != bestIsPrize)
            {
                int prizeDist = isPrize ? dist : bestDist;
                int otherDist = isPrize ? bestDist : dist;
                int cmp = ComparePrizeToOther(prizeDist, otherDist, prizeTargetBonusPpt);
                if (cmp < 0)
                {
                    return isPrize;
                }

                if (cmp > 0)
                {
                    return !isPrize;
                }

                return TieBreakPrizeThenId(isPrize, id, bestIsPrize, bestId, prizeTargetBonusPpt);
            }

            int raw = dist.CompareTo(bestDist);
            if (raw < 0)
            {
                return true;
            }

            if (raw > 0)
            {
                return false;
            }

            return TieBreakPrizeThenId(isPrize, id, bestIsPrize, bestId, prizeTargetBonusPpt);
        }

        private bool TryDefend(
            int tick,
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            AiBattalionPin[] battalions,
            int battalionCount,
            Wasteland.Sim.Movement.RoadGraph graph)
        {
            int defendTownId = 0;
            int defendNodeId = 0;
            int defendX = 0;
            int defendY = 0;
            int worstGap = 0;
            bool defendHq = false;
            bool seatZonesPresent = TownListHasSeatZones(towns, townCount);
            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                bool isOwnHq = town.Id == faction.HomeTownId;
                if (!IsAttackableTown(town, false, seatZonesPresent) && !isOwnHq)
                {
                    continue;
                }

                if (readback.OwnerFactionId != faction.FactionId && !isOwnHq)
                {
                    continue;
                }

                if (!readback.Contested)
                {
                    continue;
                }

                int garrison = readback.StrengthOf(faction.FactionId);
                int enemy = readback.EnemyStrength(faction.FactionId);
                int gap = enemy - garrison;
                if (gap <= 0)
                {
                    continue;
                }

                if (defendTownId != 0)
                {
                    if (defendHq && !isOwnHq)
                    {
                        continue;
                    }

                    if (defendHq == isOwnHq)
                    {
                        int cmp = gap.CompareTo(worstGap);
                        if (cmp < 0)
                        {
                            continue;
                        }

                        if (cmp == 0 && town.Id >= defendTownId)
                        {
                            continue;
                        }
                    }
                }

                worstGap = gap;
                defendTownId = town.Id;
                defendNodeId = town.NodeId;
                defendX = town.XCm;
                defendY = town.YCm;
                defendHq = isOwnHq;
            }

            if (defendTownId == 0)
            {
                faction.DefendTownId = 0;
                return false;
            }

            faction.DefendTownId = defendTownId;
            faction.AttackTargetTownId = 0;
            int idle = CountIdle(battalions, battalionCount);
            if (idle < 1)
            {
                return true;
            }

            int battalionId = NearestIdle(faction, graph, defendNodeId, battalions, battalionCount, string.Empty);
            if (_budgetDeferred || battalionId == 0)
            {
                return true;
            }

            if (MoveBlocked(faction, battalionId, CommandKind.Move, defendNodeId, tick))
            {
                return true;
            }

            if (!AlreadyOnTheWay(faction.FactionId, battalionId, defendX, defendY))
            {
                Issue(tick, faction, _commands.SubmitMove(faction.FactionId, battalionId, defendX, defendY));
            }

            return true;
        }

        private void TryAttack(
            int tick,
            AiFactionState faction,
            AiScenarioSeatView seat,
            AiAttackTownInfo[] towns,
            int townCount,
            AiBattalionPin[] battalions,
            int battalionCount,
            int[] networkNodeIds,
            int networkCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int attackStrengthRatioPpt,
            int supplyReachCm,
            ulong prizeTerritoryKey,
            int prizeTargetBonusPpt,
            IAiSeatView seatView)
        {
            _forceHqMarch = false;
            faction.DefendTownId = 0;
            int fromNode = faction.HqNodeId;
            if (fromNode == 0 && networkCount > 0)
            {
                fromNode = networkNodeIds[0];
            }

            if (fromNode == 0)
            {
                return;
            }

            int targetId = PickAttackTarget(
                faction,
                towns,
                townCount,
                graph,
                fromNode,
                networkNodeIds,
                networkCount,
                supplyReachCm,
                prizeTerritoryKey,
                prizeTargetBonusPpt);
            if (targetId == 0)
            {
                faction.AttackTargetTownId = 0;
                return;
            }

            faction.AttackTargetTownId = targetId;
            AiAttackTownInfo target = default;
            bool found = false;
            for (int i = 0; i < townCount; i++)
            {
                if (towns[i].Id == targetId)
                {
                    target = towns[i];
                    found = true;
                    break;
                }
            }

            if (!found || !_towns.TryGet(targetId, out AiTownReadback readback))
            {
                return;
            }

            bool prizeMarch = prizeTerritoryKey != 0UL && target.TerritoryStableKey == prizeTerritoryKey;
            bool prizeGroupReady = !prizeMarch
                || PrizeGroupIsReady(seat, battalions, battalionCount);
            int own = CountIdle(battalions, battalionCount) + readback.StrengthOf(faction.FactionId);
            int defender = readback.StrengthOf(readback.OwnerFactionId);
            if (prizeMarch)
            {
                if (!prizeGroupReady)
                {
                    string waitRaiseId = SelectRaiseUnit(
                        seat,
                        _catalog,
                        _seatView,
                        battalions,
                        battalionCount,
                        _lineUnitsPerAntiArmour,
                        false,
                        GarrisonShort(faction, towns, townCount),
                        _queuedUnitIds,
                        _queuedCount,
                        _pendingUnitIds,
                        _pendingCount);
                    int waitX;
                    int waitY;
                    RallyForRaise(
                        true,
                        false,
                        waitRaiseId,
                        towns,
                        townCount,
                        faction.HomeTownId,
                        target.XCm,
                        target.YCm,
                        out waitX,
                        out waitY);
                    TryRaise(tick, faction, seat, waitRaiseId, waitX, waitY);
                    if (TargetOutOfSupply(
                        graph,
                        target.NodeId,
                        networkNodeIds,
                        networkCount,
                        faction,
                        supplyReachCm))
                    {
                        TryPlaceForwardDepot(
                            tick,
                            faction,
                            seat,
                            towns,
                            townCount,
                            battalions,
                            battalionCount,
                            graph,
                            networkNodeIds,
                            networkCount,
                            target.NodeId,
                            supplyReachCm,
                            false);
                    }

                    return;
                }

                DispatchPrizeGroup(
                    tick,
                    faction,
                    battalions,
                    battalionCount,
                    target);
                if (TargetOutOfSupply(
                    graph,
                    target.NodeId,
                    networkNodeIds,
                    networkCount,
                    faction,
                    supplyReachCm))
                {
                    TryPlaceForwardDepot(
                        tick,
                        faction,
                        seat,
                        towns,
                        townCount,
                        battalions,
                        battalionCount,
                        graph,
                        networkNodeIds,
                        networkCount,
                        target.NodeId,
                        supplyReachCm,
                        false);
                }

                return;
            }

            AiAttackTownInfo enemyHq = default;
            bool haveEnemyHq = TryFindEnemyHq(
                faction,
                towns,
                townCount,
                prizeTerritoryKey,
                out enemyHq);
            bool canExtendSupply = SupplyExtensionPendingOrAffordable(tick, faction, seat);
            if (haveEnemyHq)
            {
                faction.HqCommit = canExtendSupply ? 1 : 0;
            }
            else
            {
                faction.HqCommit = 0;
            }

            bool committedHq = haveEnemyHq && faction.HqCommit != 0;
            bool hqOutOfSupply = haveEnemyHq
                && !committedHq
                && HqOutOfSupply(
                    faction,
                    graph,
                    enemyHq.NodeId,
                    networkNodeIds,
                    networkCount,
                    supplyReachCm);
            if (committedHq)
            {
                if (!AdoptAttackTown(
                    faction,
                    enemyHq,
                    battalions,
                    battalionCount,
                    out target,
                    out own,
                    out defender))
                {
                    return;
                }
            }
            else if (hqOutOfSupply)
            {
                ReserveDepotSavings();
                int stepId = CachedRouteWaypoint(
                    faction,
                    towns,
                    townCount,
                    graph,
                    fromNode,
                    enemyHq,
                    networkNodeIds,
                    networkCount,
                    supplyReachCm,
                    prizeTerritoryKey,
                    prizeTargetBonusPpt);
                if (_budgetDeferred)
                {
                    return;
                }

                if (stepId != 0
                    && TryFindAttackTown(towns, townCount, stepId, out AiAttackTownInfo step))
                {
                    if (!AdoptAttackTown(
                        faction,
                        step,
                        battalions,
                        battalionCount,
                        out target,
                        out own,
                        out defender))
                    {
                        return;
                    }
                }
                else
                {
                    MarchToSupplyEdge(
                        tick,
                        faction,
                        battalions,
                        battalionCount,
                        graph,
                        networkNodeIds,
                        networkCount,
                        enemyHq.NodeId,
                        supplyReachCm);
                    RaiseWhileOutmatched(
                        tick,
                        faction,
                        seat,
                        towns,
                        townCount,
                        battalions,
                        battalionCount,
                        enemyHq);
                    return;
                }
            }
            else if (!canExtendSupply
                && TargetOutOfSupply(
                    graph,
                    target.NodeId,
                    networkNodeIds,
                    networkCount,
                    faction,
                    supplyReachCm))
            {
                int suppliedId = PickAttackTarget(
                    faction,
                    towns,
                    townCount,
                    graph,
                    fromNode,
                    networkNodeIds,
                    networkCount,
                    supplyReachCm,
                    prizeTerritoryKey,
                    prizeTargetBonusPpt,
                    true);
                if (_budgetDeferred)
                {
                    return;
                }

                if (suppliedId != 0 && suppliedId != target.Id)
                {
                    if (!TryFindAttackTown(towns, townCount, suppliedId, out AiAttackTownInfo supplied))
                    {
                        return;
                    }

                    if (!AdoptAttackTown(
                        faction,
                        supplied,
                        battalions,
                        battalionCount,
                        out target,
                        out own,
                        out defender))
                    {
                        return;
                    }
                }
                else if (suppliedId == 0)
                {
                    MarchToSupplyEdge(
                        tick,
                        faction,
                        battalions,
                        battalionCount,
                        graph,
                        networkNodeIds,
                        networkCount,
                        target.NodeId,
                        supplyReachCm);
                    RaiseWhileOutmatched(
                        tick,
                        faction,
                        seat,
                        towns,
                        townCount,
                        battalions,
                        battalionCount,
                        target);
                    return;
                }
            }

            bool hqMarch = committedHq && target.Id == enemyHq.Id;
            if (!StrengthAllowsAttack(own, defender, attackStrengthRatioPpt))
            {
                if (hqMarch)
                {
                    ReserveDepotSavings();
                }

                RaiseWhileOutmatched(
                    tick,
                    faction,
                    seat,
                    towns,
                    townCount,
                    battalions,
                    battalionCount,
                    target);
                return;
            }

            if (TargetOutOfSupply(
                graph,
                target.NodeId,
                networkNodeIds,
                networkCount,
                faction,
                supplyReachCm))
            {
                int rejectBefore = faction.LastRejectCode;
                TryPlaceForwardDepot(
                    tick,
                    faction,
                    seat,
                    towns,
                    townCount,
                    battalions,
                    battalionCount,
                    graph,
                    networkNodeIds,
                    networkCount,
                    target.NodeId,
                    supplyReachCm);
                if (!hqMarch || faction.LastRejectCode != rejectBefore)
                {
                    return;
                }
            }

            if (hqMarch)
            {
                int ordered = MarchHqGroup(tick, faction, battalions, battalionCount, target);
                if (ordered == 0 && faction.LastRejectCode == 0)
                {
                    string raiseId = AffordableCombatUnit(
                        seat,
                        battalions,
                        battalionCount,
                        GarrisonShort(faction, towns, townCount));
                    int raiseX;
                    int raiseY;
                    RallyForRaise(
                        prizeMarch,
                        prizeGroupReady,
                        raiseId,
                        towns,
                        townCount,
                        faction.HomeTownId,
                        target.XCm,
                        target.YCm,
                        out raiseX,
                        out raiseY);
                    TryRaise(tick, faction, seat, raiseId, raiseX, raiseY);
                }

                return;
            }

            int battalionId = NearestIdle(
                faction,
                graph,
                target.NodeId,
                battalions,
                battalionCount,
                string.Empty,
                false,
                true,
                target.Id);
            if (_budgetDeferred)
            {
                return;
            }

            if (battalionId == 0)
            {
                string raiseId = AffordableCombatUnit(
                    seat,
                    battalions,
                    battalionCount,
                    GarrisonShort(faction, towns, townCount));
                int raiseX;
                int raiseY;
                RallyForRaise(
                    prizeMarch,
                    prizeGroupReady,
                    raiseId,
                    towns,
                    townCount,
                    faction.HomeTownId,
                    target.XCm,
                    target.YCm,
                    out raiseX,
                    out raiseY);
                TryRaise(tick, faction, seat, raiseId, raiseX, raiseY);
                return;
            }

            if (IssueClaimMarch(tick, faction, battalionId, FindPin(battalions, battalionCount, battalionId).UnitId, target))
            {
                NoteMarched(battalionId);
                RaiseWhileOutmatched(
                    tick,
                    faction,
                    seat,
                    towns,
                    townCount,
                    battalions,
                    battalionCount,
                    target);
            }
        }

        private bool AdoptAttackTown(
            AiFactionState faction,
            AiAttackTownInfo town,
            AiBattalionPin[] battalions,
            int battalionCount,
            out AiAttackTownInfo target,
            out int own,
            out int defender)
        {
            target = town;
            faction.AttackTargetTownId = town.Id;
            if (!_towns.TryGet(town.Id, out AiTownReadback readback))
            {
                own = 0;
                defender = 0;
                return false;
            }

            own = CountIdle(battalions, battalionCount) + readback.StrengthOf(faction.FactionId);
            defender = readback.StrengthOf(readback.OwnerFactionId);
            return true;
        }

        private void ReserveDepotSavings()
        {
            ReservedSeatView? reserved = _seatView as ReservedSeatView;
            if (reserved == null)
            {
                return;
            }

            int manpower;
            int materiel;
            int fuel;
            if (!_buildings.TryGetBuildCost(
                AiConstants.BuildingIdSupplyDepot,
                out manpower,
                out materiel,
                out fuel))
            {
                return;
            }

            int fuelReserve = reserved.FuelEnabled ? fuel : 0;
            int keepMateriel = 0;
            int keepFuel = 0;
            _catalog.TryGetMaterielCost("remnant_motor_rifles", out keepMateriel);
            if (reserved.FuelEnabled)
            {
                _catalog.TryGetFuelCost("remnant_motor_rifles", out keepFuel);
            }

            int materielRoom = reserved.Materials - keepMateriel;
            int fuelRoom = reserved.Fuel - keepFuel;
            if (materielRoom < 0)
            {
                materielRoom = 0;
            }

            if (fuelRoom < 0)
            {
                fuelRoom = 0;
            }

            if (materiel > materielRoom)
            {
                materiel = materielRoom;
            }

            if (fuelReserve > fuelRoom)
            {
                fuelReserve = fuelRoom;
            }

            reserved.Add(0, 0, materiel, fuelReserve);
        }

        public void ClearPlanningCaches()
        {
            Array.Clear(_waypointTown, 0, _waypointTown.Length);
            Array.Clear(_waypointSites, 0, _waypointSites.Length);
            Array.Clear(_waypointCached, 0, _waypointCached.Length);
            Array.Clear(_hqOutSites, 0, _hqOutSites.Length);
            Array.Clear(_hqOutCached, 0, _hqOutCached.Length);
            Array.Clear(_hqOutValue, 0, _hqOutValue.Length);
            Array.Clear(_edgeCached, 0, _edgeCached.Length);
            Array.Clear(_edgeSites, 0, _edgeSites.Length);
            Array.Clear(_edgeNode, 0, _edgeNode.Length);
            Array.Clear(_edgeX, 0, _edgeX.Length);
            Array.Clear(_edgeY, 0, _edgeY.Length);
            _forceHqMarch = false;
        }

        private int MarchHqGroup(
            int tick,
            AiFactionState faction,
            AiBattalionPin[] battalions,
            int battalionCount,
            AiAttackTownInfo target)
        {
            _forceHqMarch = true;
            int ordered = 0;
            for (int i = 0; i < battalionCount; i++)
            {
                AiBattalionPin pin = battalions[i];
                if (pin.Assigned || IsHeld(faction, pin.Id) || IsEngineerOrSupply(pin.UnitId))
                {
                    continue;
                }

                if (!UnitCanCapture(pin.UnitId, pin.Id))
                {
                    continue;
                }

                if (AlreadyOnTheWay(faction.FactionId, pin.Id, target.XCm, target.YCm))
                {
                    continue;
                }

                if (IssueClaimMarch(tick, faction, pin.Id, pin.UnitId, target))
                {
                    NoteMarched(pin.Id);
                    ordered++;
                    continue;
                }

                break;
            }

            _forceHqMarch = false;
            return ordered;
        }

        private bool HqOutOfSupply(
            AiFactionState faction,
            Wasteland.Sim.Movement.RoadGraph graph,
            int hqNodeId,
            int[] networkNodeIds,
            int networkCount,
            int supplyReachCm)
        {
            int factionId = faction.FactionId;
            int sites = _depots.CountSites(factionId);
            if (factionId > 0 && factionId < _hqOutCached.Length && _hqOutCached[factionId] != 0
                && _hqOutSites[factionId] == sites)
            {
                return _hqOutValue[factionId] != 0;
            }

            bool outOfSupply = TargetOutOfSupply(
                graph,
                hqNodeId,
                networkNodeIds,
                networkCount,
                faction,
                supplyReachCm);
            if (factionId > 0 && factionId < _hqOutCached.Length && !_budgetDeferred)
            {
                _hqOutCached[factionId] = 1;
                _hqOutSites[factionId] = sites;
                _hqOutValue[factionId] = outOfSupply ? 1 : 0;
            }

            return outOfSupply;
        }

        private bool TryFindEnemyHq(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            ulong prizeTerritoryKey,
            out AiAttackTownInfo hq)
        {
            hq = default;
            int bestId = 0;
            bool seatZonesPresent = TownListHasSeatZones(towns, townCount);
            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (!town.IsHq)
                {
                    continue;
                }

                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                if (readback.OwnerFactionId == faction.FactionId)
                {
                    continue;
                }

                bool isPrize = prizeTerritoryKey != 0UL && town.TerritoryStableKey == prizeTerritoryKey;
                if (!IsAttackableTown(town, isPrize, seatZonesPresent))
                {
                    continue;
                }

                if (readback.OwnerFactionId == 0 && !isPrize)
                {
                    continue;
                }

                if (bestId != 0 && town.Id >= bestId)
                {
                    continue;
                }

                bestId = town.Id;
                hq = town;
            }

            return bestId != 0;
        }

        private int CachedRouteWaypoint(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int fromNodeId,
            AiAttackTownInfo hq,
            int[] networkNodeIds,
            int networkCount,
            int supplyReachCm,
            ulong prizeTerritoryKey,
            int prizeTargetBonusPpt)
        {
            int factionId = faction.FactionId;
            int sites = _depots.CountSites(factionId);
            if (factionId > 0 && factionId < _waypointCached.Length && _waypointCached[factionId] != 0
                && _waypointSites[factionId] == sites)
            {
                int cached = _waypointTown[factionId];
                if (cached == 0 || WaypointStillHeldByEnemy(faction, towns, townCount, cached))
                {
                    return cached;
                }
            }

            int stepId = 0;
            if (!_budgetDeferred)
            {
                stepId = PickRouteWaypoint(
                    faction,
                    towns,
                    townCount,
                    graph,
                    fromNodeId,
                    hq,
                    networkNodeIds,
                    networkCount,
                    supplyReachCm,
                    prizeTerritoryKey);
            }

            if (factionId > 0 && factionId < _waypointCached.Length)
            {
                _waypointCached[factionId] = 1;
                _waypointSites[factionId] = sites;
                _waypointTown[factionId] = _budgetDeferred ? 0 : stepId;
            }

            return _budgetDeferred ? 0 : stepId;
        }

        private bool WaypointStillHeldByEnemy(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            int townId)
        {
            if (!TryFindAttackTown(towns, townCount, townId, out _))
            {
                return false;
            }

            if (!_towns.TryGet(townId, out AiTownReadback readback))
            {
                return false;
            }

            return readback.OwnerFactionId != faction.FactionId && readback.OwnerFactionId != 0;
        }

        private int PickRouteWaypoint(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int fromNodeId,
            AiAttackTownInfo hq,
            int[] networkNodeIds,
            int networkCount,
            int supplyReachCm,
            ulong prizeTerritoryKey)
        {
            AiSupplyResult filled = _roads.FillFromSources(graph, networkNodeIds, networkCount, faction);
            if (DeferIfExhausted(filled))
            {
                return 0;
            }

            AiSupplyResult fromSeat = _roads.FillFromNode(graph, fromNodeId, faction);
            if (DeferIfExhausted(fromSeat))
            {
                return 0;
            }

            int nodeCount = graph.NodeCount;
            int[] fromSeatDist = new int[nodeCount];
            for (int n = 0; n < nodeCount; n++)
            {
                fromSeatDist[n] = _roads.TargetDistanceAt(n);
            }

            if (!graph.TryGetNodeIndex(hq.NodeId, out int hqIndex))
            {
                return 0;
            }

            int fullDist = fromSeatDist[hqIndex];
            if (fullDist >= AiConstants.UnreachableCm)
            {
                return 0;
            }

            AiSupplyResult fromHq = _roads.FillFromNode(graph, hq.NodeId, faction);
            if (DeferIfExhausted(fromHq))
            {
                return 0;
            }

            int bestId = 0;
            int bestAlong = -1;
            bool seatZonesPresent = TownListHasSeatZones(towns, townCount);
            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (town.Id == hq.Id)
                {
                    continue;
                }

                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                if (readback.OwnerFactionId == faction.FactionId)
                {
                    continue;
                }

                bool isPrize = prizeTerritoryKey != 0UL && town.TerritoryStableKey == prizeTerritoryKey;
                if (!IsAttackableTown(town, isPrize, seatZonesPresent))
                {
                    continue;
                }

                if (readback.OwnerFactionId == 0 && !isPrize)
                {
                    continue;
                }

                if (!graph.TryGetNodeIndex(town.NodeId, out int townIndex))
                {
                    continue;
                }

                if (_roads.NetworkDistanceAt(townIndex) > supplyReachCm)
                {
                    continue;
                }

                int toward = fromSeatDist[townIndex];
                int rest = _roads.TargetDistanceAt(townIndex);
                if (toward >= AiConstants.UnreachableCm || rest >= AiConstants.UnreachableCm)
                {
                    continue;
                }

                if ((long)toward + rest != fullDist)
                {
                    continue;
                }

                if (toward <= bestAlong)
                {
                    continue;
                }

                bestAlong = toward;
                bestId = town.Id;
            }

            return bestId;
        }

        private string AffordableCombatUnit(
            AiScenarioSeatView seat,
            AiBattalionPin[] battalions,
            int battalionCount,
            bool garrisonShort)
        {
            string preferred = SelectRaiseUnit(
                seat,
                _catalog,
                _seatView,
                battalions,
                battalionCount,
                _lineUnitsPerAntiArmour,
                false,
                garrisonShort,
                _queuedUnitIds,
                _queuedCount,
                _pendingUnitIds,
                _pendingCount);
            if (CanAfford(_seatView, _catalog, preferred))
            {
                return preferred;
            }

            string[] ids = seat.RaisableUnits;
            if (ids == null)
            {
                return string.Empty;
            }

            int tier = _seatView.CurrentTier;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (!seat.AllowsUnitForTier(id, tier, _catalog))
                {
                    continue;
                }

                if (!_catalog.TryGetRole(id, out string role))
                {
                    continue;
                }

                if (role == AiConstants.RoleEngineer || role == "supply")
                {
                    continue;
                }

                if (!CanAfford(_seatView, _catalog, id))
                {
                    continue;
                }

                return id;
            }

            return string.Empty;
        }

        private void RaiseWhileOutmatched(
            int tick,
            AiFactionState faction,
            AiScenarioSeatView seat,
            AiAttackTownInfo[] towns,
            int townCount,
            AiBattalionPin[] battalions,
            int battalionCount,
            AiAttackTownInfo target)
        {
            string raiseId = AffordableCombatUnit(
                seat,
                battalions,
                battalionCount,
                GarrisonShort(faction, towns, townCount));
            int raiseX;
            int raiseY;
            RallyForRaise(
                false,
                true,
                raiseId,
                towns,
                townCount,
                faction.HomeTownId,
                target.XCm,
                target.YCm,
                out raiseX,
                out raiseY);
            TryRaise(tick, faction, seat, raiseId, raiseX, raiseY);
        }

        private bool SupplyExtensionPendingOrAffordable(
            int tick,
            AiFactionState faction,
            AiScenarioSeatView seat)
        {
            if (!CanBuildDepot(seat, _buildings))
            {
                return false;
            }

            if (HasPendingDepotIntent(faction))
            {
                return true;
            }

            int inFlightNode = _depots.InFlightNodeId(faction.FactionId, tick);
            if (inFlightNode != 0 && !IsNodeBlacklisted(faction, inFlightNode))
            {
                return true;
            }

            int cap = faction.Depots.Length;
            if (cap > 0 && _depots.CountSites(faction.FactionId) >= cap)
            {
                return true;
            }

            return CanAffordDepot(_seatView, _buildings);
        }

        private static bool TryFindAttackTown(
            AiAttackTownInfo[] towns,
            int townCount,
            int townId,
            out AiAttackTownInfo town)
        {
            for (int i = 0; i < townCount; i++)
            {
                if (towns[i].Id == townId)
                {
                    town = towns[i];
                    return true;
                }
            }

            town = default;
            return false;
        }

        private void MarchToSupplyEdge(
            int tick,
            AiFactionState faction,
            AiBattalionPin[] battalions,
            int battalionCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int[] networkNodeIds,
            int networkCount,
            int targetNodeId,
            int supplyReachCm)
        {
            int edgeX;
            int edgeY;
            int edgeNode;
            int factionId = faction.FactionId;
            int sites = _depots.CountSites(factionId);
            bool cachedEdge = factionId > 0 && factionId < _edgeCached.Length && _edgeCached[factionId] != 0
                && _edgeSites[factionId] == sites;
            if (cachedEdge)
            {
                edgeNode = _edgeNode[factionId];
                edgeX = _edgeX[factionId];
                edgeY = _edgeY[factionId];
                if (edgeNode == 0)
                {
                    return;
                }
            }
            else if (!TryFindSupplyEdge(
                graph,
                networkNodeIds,
                networkCount,
                faction,
                targetNodeId,
                supplyReachCm,
                out edgeNode,
                out edgeX,
                out edgeY))
            {
                if (factionId > 0 && factionId < _edgeCached.Length)
                {
                    _edgeCached[factionId] = 1;
                    _edgeSites[factionId] = sites;
                    _edgeNode[factionId] = 0;
                }

                return;
            }
            else if (factionId > 0 && factionId < _edgeCached.Length && !_budgetDeferred)
            {
                _edgeCached[factionId] = 1;
                _edgeSites[factionId] = sites;
                _edgeNode[factionId] = edgeNode;
                _edgeX[factionId] = edgeX;
                _edgeY[factionId] = edgeY;
            }

            int battalionId = NearestIdle(
                faction,
                graph,
                edgeNode,
                battalions,
                battalionCount,
                string.Empty,
                false,
                true,
                0);
            if (_budgetDeferred || battalionId == 0)
            {
                return;
            }

            if (AlreadyOnTheWay(faction.FactionId, battalionId, edgeX, edgeY)
                || AlreadyMarching(faction.FactionId, battalionId))
            {
                return;
            }

            if (MoveBlocked(faction, battalionId, CommandKind.Move, edgeNode, tick))
            {
                return;
            }

            Issue(tick, faction, _commands.SubmitMove(faction.FactionId, battalionId, edgeX, edgeY));
        }

        private bool TryFindSupplyEdge(
            Wasteland.Sim.Movement.RoadGraph graph,
            int[] networkNodeIds,
            int networkCount,
            AiFactionState faction,
            int targetNodeId,
            int supplyReachCm,
            out int nodeId,
            out int xCm,
            out int yCm)
        {
            nodeId = 0;
            xCm = 0;
            yCm = 0;
            AiSupplyResult fromNet = _roads.FillFromSources(graph, networkNodeIds, networkCount, faction);
            if (DeferIfExhausted(fromNet))
            {
                return false;
            }

            if (!graph.TryGetNodeIndex(targetNodeId, out _))
            {
                return false;
            }

            AiSupplyResult fromTarget = _roads.FillFromNode(graph, targetNodeId, faction);
            if (DeferIfExhausted(fromTarget))
            {
                return false;
            }

            int bestNode = 0;
            int bestX = 0;
            int bestY = 0;
            int bestRemain = int.MaxValue;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                int candidate = graph.GetNodeId(i);
                int netDist = _roads.NetworkDistanceAt(i);
                if (netDist > supplyReachCm)
                {
                    continue;
                }

                int remain = _roads.TargetDistanceAt(i);
                if (remain >= AiConstants.UnreachableCm)
                {
                    continue;
                }

                if (bestNode != 0)
                {
                    int cmp = remain.CompareTo(bestRemain);
                    if (cmp > 0)
                    {
                        continue;
                    }

                    if (cmp == 0 && candidate >= bestNode)
                    {
                        continue;
                    }
                }

                bestRemain = remain;
                bestNode = candidate;
                bestX = graph.GetNodeXCm(i);
                bestY = graph.GetNodeYCm(i);
            }

            if (bestNode == 0)
            {
                return false;
            }

            nodeId = bestNode;
            xCm = bestX;
            yCm = bestY;
            return true;
        }

        private void TryPlaceForwardDepot(
            int tick,
            AiFactionState faction,
            AiScenarioSeatView seat,
            AiAttackTownInfo[] towns,
            int townCount,
            AiBattalionPin[] battalions,
            int battalionCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int[] networkNodeIds,
            int networkCount,
            int targetNodeId,
            int supplyReachCm,
            bool raiseIfMissing = true)
        {
            if (!CanBuildDepot(seat, _buildings))
            {
                return;
            }

            if (HasPendingDepotIntent(faction))
            {
                return;
            }

            int cap = faction.Depots.Length;
            if (cap > 0 && _depots.CountSites(faction.FactionId) >= cap)
            {
                faction.DepotCapSkips++;
                return;
            }

            int inFlightNode = _depots.InFlightNodeId(faction.FactionId, tick);
            if (inFlightNode != 0 && !IsNodeBlacklisted(faction, inFlightNode))
            {
                EnsureBuilderMarchingTo(
                    tick,
                    faction,
                    battalions,
                    battalionCount,
                    graph,
                    inFlightNode);
                return;
            }

            if (!CanAffordDepot(_seatView, _buildings))
            {
                return;
            }

            AiSupplyResult fromNet = _roads.FillFromSources(graph, networkNodeIds, networkCount, faction);
            if (DeferIfExhausted(fromNet))
            {
                return;
            }

            if (!graph.TryGetNodeIndex(targetNodeId, out int targetIndex))
            {
                return;
            }

            AiSupplyResult fromTarget = _roads.FillFromNode(graph, targetNodeId, faction);
            if (DeferIfExhausted(fromTarget))
            {
                return;
            }

            int bestNode = 0;
            int bestX = 0;
            int bestY = 0;
            int bestRemain = int.MaxValue;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                int nodeId = graph.GetNodeId(i);
                if (IsNodeBlacklisted(faction, nodeId))
                {
                    continue;
                }

                int netDist = _roads.NetworkDistanceAt(i);
                if (netDist > supplyReachCm)
                {
                    continue;
                }

                if (_depots.TryGet(faction.FactionId, nodeId, out _))
                {
                    continue;
                }

                int nodeX = graph.GetNodeXCm(i);
                int nodeY = graph.GetNodeYCm(i);
                if (InUnintendedSeatZone(faction, towns, townCount, nodeX, nodeY))
                {
                    continue;
                }

                if (!DepotSiteAllowed(faction, towns, townCount, graph, i, nodeId))
                {
                    continue;
                }

                int remain = _roads.TargetDistanceAt(i);
                if (bestNode != 0)
                {
                    int cmp = remain.CompareTo(bestRemain);
                    if (cmp > 0)
                    {
                        continue;
                    }

                    if (cmp == 0 && nodeId >= bestNode)
                    {
                        continue;
                    }
                }

                bestRemain = remain;
                bestNode = nodeId;
                bestX = nodeX;
                bestY = nodeY;
            }

            if (bestNode == 0)
            {
                return;
            }

            int engineerId = NearestIdle(
                faction,
                graph,
                bestNode,
                battalions,
                battalionCount,
                AiConstants.RoleEngineer);
            if (_budgetDeferred)
            {
                return;
            }

            if (engineerId == 0)
            {
                if (!raiseIfMissing)
                {
                    return;
                }

                string engineerIdToRaise = SelectRaiseUnit(
                    seat,
                    _catalog,
                    _seatView,
                    battalions,
                    battalionCount,
                    _lineUnitsPerAntiArmour,
                    true,
                    false,
                    _queuedUnitIds,
                    _queuedCount,
                    _pendingUnitIds,
                    _pendingCount);
                TryRaise(tick, faction, seat, engineerIdToRaise, bestX, bestY);
                return;
            }

            AiBattalionPin engineer = FindPin(battalions, battalionCount, engineerId);
            bool nearEnemy = PathNearEnemy(faction, towns, townCount, graph, engineer.RoadNodeId, bestNode);
            if (_budgetDeferred)
            {
                return;
            }

            if (nearEnemy)
            {
                int escortId = NearestIdle(faction, graph, bestNode, battalions, battalionCount, AiConstants.RoleLineInfantry);
                if (_budgetDeferred)
                {
                    return;
                }

                if (escortId != 0 && escortId != engineerId)
                {
                    AiBattalionPin escort = FindPin(battalions, battalionCount, escortId);
                    if (escort.RoadNodeId != bestNode
                        && !AlreadyOnTheWay(faction.FactionId, escortId, bestX, bestY))
                    {
                        int escortCmd = _commands.SubmitMove(faction.FactionId, escortId, bestX, bestY);
                        Issue(tick, faction, escortCmd);
                        if (_commands.RejectOf(escortCmd) != AiCommandReject.None)
                        {
                            return;
                        }
                    }
                }
            }

            if (MoveBlocked(faction, engineerId, CommandKind.BuildDepot, bestNode, bestNode, tick))
            {
                return;
            }

            if (engineer.RoadNodeId != bestNode
                && !AlreadyOnTheWay(faction.FactionId, engineerId, bestX, bestY))
            {
                int moveId = _commands.SubmitMove(faction.FactionId, engineerId, bestX, bestY);
                Issue(tick, faction, moveId);
                if (_commands.RejectOf(moveId) != AiCommandReject.None)
                {
                    return;
                }
            }

            if (inFlightNode != 0 && inFlightNode != bestNode && !IsNodeBlacklisted(faction, inFlightNode))
            {
                return;
            }

            int buildId = _commands.SubmitBuildDepot(faction.FactionId, engineerId, bestNode);
            Issue(tick, faction, buildId);
            if (_commands.RejectOf(buildId) != AiCommandReject.None)
            {
                return;
            }

            if (_depots.TryGet(faction.FactionId, bestNode, out AiDepotReadback built))
            {
                faction.DepotSiteTick = built.SiteTick;
                faction.DepotActiveTick = built.ActiveTick;
            }
        }

        private bool TargetOutOfSupply(
            Wasteland.Sim.Movement.RoadGraph graph,
            int targetNodeId,
            int[] networkNodeIds,
            int networkCount,
            AiFactionState faction,
            int supplyReachCm)
        {
            AiSupplyResult supply = _roads.DistanceToNearestCm(
                graph,
                targetNodeId,
                networkNodeIds,
                networkCount,
                faction);
            if (DeferIfExhausted(supply))
            {
                return false;
            }

            return supply.IsUnreachable || supply.DistanceCm > supplyReachCm;
        }

        private static bool HasPendingDepotIntent(AiFactionState faction)
        {
            for (int i = 0; i < faction.IntentCount; i++)
            {
                if (faction.Intents[i].Kind == AiIssuedKind.BuildDepot)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsNodeBlacklisted(AiFactionState faction, int nodeId)
        {
            if (_owned == null)
            {
                return false;
            }

            return _owned.IsDepotNodeBlacklisted(faction.FactionId, nodeId);
        }

        private bool DepotSiteAllowed(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int nodeIndex,
            int nodeId)
        {
            if (_world == null)
            {
                return true;
            }

            int territoryId = _world.GetNodeTerritoryId(nodeId);
            if (territoryId == 0)
            {
                territoryId = graph.GetNodeTerritoryId(nodeIndex);
            }

            if (!NodeTerritoryOwned(faction, towns, townCount, territoryId))
            {
                return false;
            }

            return _world.PreviewDepotPlacement(faction.FactionId, nodeId, out _, out _)
                == CommandRejectReason.None;
        }

        private bool NodeTerritoryOwned(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            int territoryId)
        {
            ulong key = 0UL;
            for (int i = 0; i < townCount; i++)
            {
                if (towns[i].TerritoryId == territoryId)
                {
                    key = towns[i].TerritoryStableKey;
                    break;
                }
            }

            if (key != 0UL && _world != null && _world.TryGetTerritory(key, out TerritoryView view))
            {
                return view.OwnerFactionId == faction.FactionId;
            }

            return faction.HomeTerritoryId == territoryId;
        }

        public static string SelectRaiseUnit(
            AiScenarioSeatView seat,
            IAiUnitCatalog catalog,
            IAiSeatView seatView,
            AiBattalionPin[] battalions,
            int battalionCount,
            int lineUnitsPerAntiArmour,
            bool forDepot,
            bool garrisonShort)
        {
            return SelectRaiseUnit(
                seat,
                catalog,
                seatView,
                battalions,
                battalionCount,
                lineUnitsPerAntiArmour,
                forDepot,
                garrisonShort,
                null,
                0,
                null,
                0);
        }

        public static string SelectRaiseUnit(
            AiScenarioSeatView seat,
            IAiUnitCatalog catalog,
            IAiSeatView seatView,
            AiBattalionPin[] battalions,
            int battalionCount,
            int lineUnitsPerAntiArmour,
            bool forDepot,
            bool garrisonShort,
            string[]? queuedUnitIds,
            int queuedCount,
            string[]? pendingUnitIds,
            int pendingCount)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (seatView == null)
            {
                throw new ArgumentNullException(nameof(seatView));
            }

            if (lineUnitsPerAntiArmour < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(lineUnitsPerAntiArmour));
            }

            int currentTier = seatView.CurrentTier;
            string atId = FirstRaisableRole(seat, catalog, currentTier, AiConstants.RoleAntiTank);
            int line = CountMixRiflesAcross(
                battalions,
                battalionCount,
                queuedUnitIds,
                queuedCount,
                pendingUnitIds,
                pendingCount,
                catalog);
            int at = CountRoleAcross(
                battalions,
                battalionCount,
                queuedUnitIds,
                queuedCount,
                pendingUnitIds,
                pendingCount,
                catalog,
                AiConstants.RoleAntiTank);
            bool atDue = !string.IsNullOrEmpty(atId)
                && line >= (long)lineUnitsPerAntiArmour * ((long)at + 1L);
            bool frontAt = atDue && seatView.HasSeenEnemyArmourOrMech;

            if (forDepot)
            {
                if (frontAt)
                {
                    return atId;
                }

                return FirstRaisableRole(seat, catalog, currentTier, AiConstants.RoleEngineer);
            }

            if (frontAt)
            {
                return atId;
            }

            string lineId = FirstRaisableRole(seat, catalog, currentTier, AiConstants.RoleLineInfantry);
            if (garrisonShort && !CanAfford(seatView, catalog, lineId))
            {
                string garrisonId = FirstRaisableRole(
                    seat,
                    catalog,
                    currentTier,
                    AiConstants.RoleGarrisonInfantry);
                if (!string.IsNullOrEmpty(garrisonId))
                {
                    return garrisonId;
                }
            }

            if (atDue)
            {
                return atId;
            }

            if (!string.IsNullOrEmpty(lineId))
            {
                return lineId;
            }

            return atId;
        }

        private void TryRaise(
            int tick,
            AiFactionState faction,
            AiScenarioSeatView seat,
            string unitId,
            int xCm,
            int yCm)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                return;
            }

            int currentTier = _seatView.CurrentTier;
            if (!seat.AllowsUnit(unitId) || !seat.AllowsUnitForTier(unitId, currentTier, _catalog))
            {
                return;
            }

            if (_seatView.PopCap > 0 && _seatView.PopUsed >= _seatView.PopCap)
            {
                NoteReject(tick, faction, AiCommandReject.PopCapReached);
                return;
            }

            if (!CanAfford(_seatView, _catalog, unitId))
            {
                return;
            }

            int popCost = 1;
            if (_catalog.TryGetPopCost(unitId, out int havePop))
            {
                popCost = havePop;
            }

            if (_seatView.PopCap > 0 && _seatView.PopUsed + popCost > _seatView.PopCap)
            {
                return;
            }

            // xCm/yCm is the rally or march target. Spawn is the seat HQ.
            int commandId = _commands.SubmitRaiseBattalion(faction.FactionId, unitId, xCm, yCm);
            Issue(tick, faction, commandId);
            if (_commands.RejectOf(commandId) != AiCommandReject.None)
            {
                return;
            }

            int cost = 0;
            int materielCost = 0;
            int fuelCost = 0;
            _catalog.TryGetManpowerCost(unitId, out cost);
            _catalog.TryGetMaterielCost(unitId, out materielCost);
            if (_seatView.FuelEnabled)
            {
                _catalog.TryGetFuelCost(unitId, out fuelCost);
            }

            ReservedSeatView? reserved = _seatView as ReservedSeatView;
            if (reserved != null)
            {
                reserved.Add(cost, popCost, materielCost, fuelCost);
            }
        }

        private bool GarrisonShort(AiFactionState faction, AiAttackTownInfo[] towns, int townCount)
        {
            if (_minGarrisonPerTown < 1)
            {
                return false;
            }

            bool seatZonesPresent = TownListHasSeatZones(towns, townCount);
            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                if (readback.OwnerFactionId != faction.FactionId)
                {
                    continue;
                }

                if (!IsAttackableTown(town, false, seatZonesPresent) && town.Id != faction.HomeTownId)
                {
                    continue;
                }

                if (readback.StrengthOf(faction.FactionId) < _minGarrisonPerTown)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Seat stock vs the catalog depot cost. False when the catalog has a
        /// cost and manpower, materiel, or (when fuel is on) fuel is short.
        /// True when the catalog has no cost so harness fixtures still place.
        /// </summary>
        internal static bool CanAffordDepot(IAiSeatView seatView, IAiBuildingCatalog catalog)
        {
            if (seatView == null || catalog == null)
            {
                return false;
            }

            int manpower;
            int materiel;
            int fuel;
            if (!catalog.TryGetBuildCost(AiConstants.BuildingIdSupplyDepot, out manpower, out materiel, out fuel))
            {
                return true;
            }

            if (seatView.Manpower < manpower || seatView.Materials < materiel)
            {
                return false;
            }

            if (seatView.FuelEnabled && seatView.Fuel < fuel)
            {
                return false;
            }

            return true;
        }

        internal static bool CanBuildDepot(AiScenarioSeatView seat, IAiBuildingCatalog catalog)
        {
            if (catalog == null)
            {
                return false;
            }

            string[] ids = seat.BuildableBuildings;
            if (ids == null)
            {
                return false;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (!seat.AllowsBuilding(id))
                {
                    continue;
                }

                if (catalog.TryGetBuildingRole(id, out string role)
                    && role == AiConstants.BuildingRoleSupplyDepot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Seat stock vs manpower, materiel, and fuel. Fuel is checked only
        /// when the seat has fuel enabled. An unknown unit stays affordable
        /// so the sim can reject it.
        /// </summary>
        internal static bool CanAfford(IAiSeatView seatView, IAiUnitCatalog catalog, string unitId)
        {
            if (seatView == null || catalog == null || string.IsNullOrEmpty(unitId))
            {
                return true;
            }

            if (!catalog.TryGetManpowerCost(unitId, out int manpower))
            {
                return true;
            }

            int materiel = 0;
            int fuel = 0;
            catalog.TryGetMaterielCost(unitId, out materiel);
            catalog.TryGetFuelCost(unitId, out fuel);
            if (seatView.Manpower < manpower || seatView.Materials < materiel)
            {
                return false;
            }

            if (seatView.FuelEnabled && seatView.Fuel < fuel)
            {
                return false;
            }

            return true;
        }

        private static string FirstRaisableRole(
            AiScenarioSeatView seat,
            IAiUnitCatalog catalog,
            int currentTier,
            string role)
        {
            string[] ids = seat.RaisableUnits;
            if (ids == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (!seat.AllowsUnitForTier(id, currentTier, catalog))
                {
                    continue;
                }

                if (!catalog.TryGetRole(id, out string have) || have != role)
                {
                    continue;
                }

                return id;
            }

            return string.Empty;
        }

        private static bool TownListHasSeatZones(AiAttackTownInfo[] towns, int townCount)
        {
            for (int i = 0; i < townCount; i++)
            {
                if (towns[i].ZoneRadiusCm > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAttackableTown(AiAttackTownInfo town, bool isPrize, bool seatZonesPresent)
        {
            if (!seatZonesPresent || isPrize || town.IsHq)
            {
                return true;
            }

            return town.ZoneRadiusCm > 0;
        }

        /// <summary>
        /// True when (x,y) sits in another seat town's capture zone and this
        /// faction is not attacking, defending, or home-holding that town.
        /// </summary>
        internal static bool InUnintendedSeatZone(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            int xCm,
            int yCm)
        {
            if (faction == null || towns == null)
            {
                return false;
            }

            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (town.ZoneRadiusCm <= 0)
                {
                    continue;
                }

                if (town.Id == faction.HomeTownId
                    || town.Id == faction.AttackTargetTownId
                    || town.Id == faction.DefendTownId)
                {
                    continue;
                }

                long dx = (long)xCm - town.XCm;
                long dy = (long)yCm - town.YCm;
                long radius = town.ZoneRadiusCm;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    return true;
                }
            }

            return false;
        }

        private bool PathNearEnemy(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int fromNodeId,
            int toNodeId)
        {
            AiSupplyResult path = _roads.DistanceCm(graph, fromNodeId, toNodeId, faction);
            if (DeferIfExhausted(path) || !path.IsReached)
            {
                return false;
            }

            int pathDist = path.DistanceCm;

            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                if (readback.EnemyStrength(faction.FactionId) < 1)
                {
                    continue;
                }

                AiSupplyResult viaResult = _roads.DistanceCm(graph, fromNodeId, town.NodeId, faction);
                if (DeferIfExhausted(viaResult))
                {
                    return false;
                }

                if (!viaResult.IsReached)
                {
                    continue;
                }

                AiSupplyResult restResult = _roads.DistanceCm(graph, town.NodeId, toNodeId, faction);
                if (DeferIfExhausted(restResult))
                {
                    return false;
                }

                if (!restResult.IsReached)
                {
                    continue;
                }

                if (viaResult.DistanceCm + restResult.DistanceCm == pathDist)
                {
                    return true;
                }
            }

            return false;
        }

        private void Issue(int tick, AiFactionState faction, int commandId)
        {
            faction.LastIssuedCommandId = commandId;
            faction.LastIssuedSequence = _commands.LastSequence;
            AiCommandReject reject = _commands.RejectOf(commandId);
            if (reject == AiCommandReject.None)
            {
                faction.LastRejectCode = 0;
                faction.RejectStreak = 0;
                faction.RejectBackoffUntilTick = 0;
                return;
            }

            NoteReject(tick, faction, reject);
            if (!SkipsKeyedOrderBackoff(reject))
            {
                NoteIssuedReject(tick, faction, commandId);
            }
        }

        /// <summary>
        /// Seat stock shortfalls wait on the seat-wide backoff from
        /// <see cref="NoteReject"/>. They do not take a keyed order-backoff row.
        /// Manpower, materials, and fuel share that skip.
        /// </summary>
        private static bool SkipsKeyedOrderBackoff(AiCommandReject reject)
        {
            return reject == AiCommandReject.InsufficientManpower
                || reject == AiCommandReject.InsufficientMaterials
                || reject == AiCommandReject.InsufficientFuel;
        }

        private static void NoteReject(int tick, AiFactionState faction, AiCommandReject reject)
        {
            faction.LastRejectCode = (int)reject;
            int streak = faction.RejectStreak + 1;
            if (streak < 1)
            {
                streak = 1;
            }

            faction.RejectStreak = streak;
            int delay = RejectBackoffStartTicks;
            for (int i = 1; i < streak; i++)
            {
                if (delay >= RejectBackoffMaxTicks / 2)
                {
                    delay = RejectBackoffMaxTicks;
                    break;
                }

                delay *= 2;
            }

            faction.RejectBackoffUntilTick = tick + delay;
        }

        private void PollLateReject(int tick, AiFactionState faction)
        {
            if (faction.LastIssuedCommandId == 0 || faction.LastRejectCode != 0)
            {
                return;
            }

            AiCommandReject reject = faction.LastIssuedSequence != 0
                ? _commands.RejectOfSequence(faction.FactionId, faction.LastIssuedSequence)
                : _commands.RejectOf(faction.LastIssuedCommandId);
            if (reject == AiCommandReject.None)
            {
                return;
            }

            NoteReject(tick, faction, reject);
        }

        internal static void SumPendingRaises(
            IAiCommandSink commands,
            int tick,
            int factionId,
            IAiUnitCatalog catalog,
            out int manpower,
            out int pop,
            out int materiel,
            out int fuel)
        {
            manpower = 0;
            pop = 0;
            materiel = 0;
            fuel = 0;
            if (commands == null || catalog == null)
            {
                return;
            }

            int delay = commands.CommandDelayTicks;
            IReadOnlyList<AiIssuedCommand> issued = commands.IssuedCommands;
            if (issued == null)
            {
                return;
            }

            for (int i = 0; i < issued.Count; i++)
            {
                AiIssuedCommand cmd = issued[i];
                if (cmd.Kind != AiIssuedKind.RaiseBattalion || cmd.FactionId != factionId)
                {
                    continue;
                }

                if (!IsPendingRaise(commands, cmd, tick, delay))
                {
                    continue;
                }

                int cost = 0;
                int materielCost = 0;
                int fuelCost = 0;
                catalog.TryGetManpowerCost(cmd.UnitId, out cost);
                catalog.TryGetMaterielCost(cmd.UnitId, out materielCost);
                catalog.TryGetFuelCost(cmd.UnitId, out fuelCost);
                manpower += cost;
                materiel += materielCost;
                fuel += fuelCost;
                int unitPop = 1;
                if (catalog.TryGetPopCost(cmd.UnitId, out int havePop))
                {
                    unitPop = havePop;
                }

                pop += unitPop;
            }
        }

        internal static int CopyPendingRaiseUnitIds(
            IAiCommandSink commands,
            int tick,
            int factionId,
            string[] into)
        {
            int count = 0;
            if (commands == null || into == null)
            {
                return 0;
            }

            int delay = commands.CommandDelayTicks;
            IReadOnlyList<AiIssuedCommand> issued = commands.IssuedCommands;
            if (issued == null)
            {
                return 0;
            }

            for (int i = 0; i < issued.Count; i++)
            {
                AiIssuedCommand cmd = issued[i];
                if (cmd.Kind != AiIssuedKind.RaiseBattalion || cmd.FactionId != factionId)
                {
                    continue;
                }

                if (!IsPendingRaise(commands, cmd, tick, delay))
                {
                    continue;
                }

                if (count >= into.Length)
                {
                    break;
                }

                into[count] = cmd.UnitId;
                count++;
            }

            return count;
        }

        private static bool IsPendingRaise(
            IAiCommandSink commands,
            AiIssuedCommand cmd,
            int tick,
            int delay)
        {
            if (cmd.Tick + delay <= tick)
            {
                return false;
            }

            if (commands.RejectOf(cmd.Id) != AiCommandReject.None)
            {
                return false;
            }

            if (commands.RaiseHasStarted(cmd.Id))
            {
                return false;
            }

            return true;
        }

        private sealed class ReservedSeatView : IAiSeatView
        {
            private readonly IAiSeatView _inner;
            private int _reservedManpower;
            private int _reservedMateriel;
            private int _reservedFuel;
            private int _reservedPop;

            public ReservedSeatView(IAiSeatView inner)
            {
                _inner = inner;
            }

            public int CurrentTier
            {
                get { return _inner.CurrentTier; }
            }

            public int TierResearchCompleteTick
            {
                get { return _inner.TierResearchCompleteTick; }
            }

            public int Manpower
            {
                get
                {
                    int value = _inner.Manpower - _reservedManpower;
                    return value < 0 ? 0 : value;
                }
            }

            public int Materials
            {
                get
                {
                    int value = _inner.Materials - _reservedMateriel;
                    return value < 0 ? 0 : value;
                }
            }

            public int Fuel
            {
                get
                {
                    int value = _inner.Fuel - _reservedFuel;
                    return value < 0 ? 0 : value;
                }
            }

            public bool FuelEnabled
            {
                get { return _inner.FuelEnabled; }
            }

            public int PopUsed
            {
                get { return _inner.PopUsed + _reservedPop; }
            }

            public int PopCap
            {
                get { return _inner.PopCap; }
            }

            public bool HasSeenEnemyArmourOrMech
            {
                get { return _inner.HasSeenEnemyArmourOrMech; }
            }

            public void Add(int manpower, int pop, int materiel, int fuel)
            {
                _reservedManpower += manpower;
                _reservedPop += pop;
                _reservedMateriel += materiel;
                _reservedFuel += fuel;
            }
        }

        private static int CountIdle(AiBattalionPin[] battalions, int battalionCount)
        {
            int idle = 0;
            for (int i = 0; i < battalionCount; i++)
            {
                if (battalions[i].Assigned)
                {
                    continue;
                }

                idle++;
            }

            return idle;
        }

        private bool IsFootUnit(string unitId)
        {
            return IsFootUnit(_catalog, unitId);
        }

        private void RallyForRaise(
            bool prizeMarch,
            bool prizeGroupReady,
            string unitId,
            AiAttackTownInfo[] towns,
            int townCount,
            int homeTownId,
            int targetXCm,
            int targetYCm,
            out int xCm,
            out int yCm)
        {
            xCm = targetXCm;
            yCm = targetYCm;
            if (!prizeMarch)
            {
                return;
            }

            bool holdAtHome = IsFootUnit(unitId)
                || (!prizeGroupReady && (IsPrizeRifle(_catalog, unitId) || IsPrizeScout(_catalog, unitId)));
            if (!holdAtHome)
            {
                return;
            }

            int homeX;
            int homeY;
            if (TryTownXy(towns, townCount, homeTownId, out homeX, out homeY))
            {
                xCm = homeX;
                yCm = homeY;
            }
        }

        private void DispatchPrizeGroup(
            int tick,
            AiFactionState faction,
            AiBattalionPin[] battalions,
            int battalionCount,
            AiAttackTownInfo target)
        {
            int riflesTaken = 0;
            for (int i = 0; i < battalionCount; i++)
            {
                AiBattalionPin pin = battalions[i];
                if (!IsPrizeRifle(_catalog, pin.UnitId))
                {
                    continue;
                }

                if (riflesTaken >= _taskForceTargetSize)
                {
                    break;
                }

                DispatchPrizeBattalion(tick, faction, pin, target);
                riflesTaken++;
            }

            for (int i = 0; i < battalionCount; i++)
            {
                AiBattalionPin pin = battalions[i];
                if (!IsPrizeScout(_catalog, pin.UnitId))
                {
                    continue;
                }

                DispatchPrizeBattalion(tick, faction, pin, target);
            }
        }

        private void DispatchPrizeBattalion(
            int tick,
            AiFactionState faction,
            AiBattalionPin pin,
            AiAttackTownInfo target)
        {
            if (IsHeld(faction, pin.Id))
            {
                return;
            }

            if (HasPrizeOrder(faction, pin.Id, target.Id))
            {
                return;
            }

            if (!IssueClaimMarch(tick, faction, pin.Id, pin.UnitId, target))
            {
                return;
            }

            NoteMarched(pin.Id);
            EnsurePrizeOrder(faction, pin.Id, target.Id, target.TerritoryId);
        }

        private static bool HasPrizeOrder(AiFactionState faction, int battalionId, int townId)
        {
            if (faction == null || battalionId == 0)
            {
                return false;
            }

            for (int i = 0; i < faction.OrderCount; i++)
            {
                AiOrder order = faction.Orders[i];
                if (order.BattalionId == battalionId
                    && order.Type == AiGoalType.ClaimTown
                    && order.TargetTownId == townId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsurePrizeOrder(
            AiFactionState faction,
            int battalionId,
            int townId,
            int territoryId)
        {
            if (faction == null || battalionId == 0)
            {
                return;
            }

            for (int i = 0; i < faction.OrderCount; i++)
            {
                if (faction.Orders[i].BattalionId == battalionId)
                {
                    return;
                }
            }

            if (faction.OrderCount >= faction.MaxActiveOrders)
            {
                return;
            }

            AiOrder order = default;
            order.Id = faction.NextOrderId++;
            order.Type = AiGoalType.ClaimTown;
            order.TargetTownId = townId;
            order.TargetTerritoryId = territoryId;
            order.BattalionId = battalionId;
            faction.Orders[faction.OrderCount++] = order;
        }

        internal static bool CountsAsMixRifle(IAiUnitCatalog catalog, string unitId)
        {
            if (catalog == null || string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            if (!catalog.TryGetRole(unitId, out string role))
            {
                return false;
            }

            if (role == AiConstants.RoleGarrisonInfantry
                || role == AiConstants.RoleEngineer
                || role == AiConstants.RoleReconSpotter
                || role == AiConstants.RoleReconStrike)
            {
                return false;
            }

            return role == AiConstants.RoleLineInfantry;
        }

        internal static bool IsPrizeRifle(IAiUnitCatalog catalog, string unitId)
        {
            return CountsAsMixRifle(catalog, unitId) && !IsFootUnit(catalog, unitId);
        }

        internal static bool IsPrizeScout(IAiUnitCatalog catalog, string unitId)
        {
            if (catalog == null || string.IsNullOrEmpty(unitId) || IsFootUnit(catalog, unitId))
            {
                return false;
            }

            if (!catalog.TryGetRole(unitId, out string role))
            {
                return false;
            }

            return role == AiConstants.RoleReconSpotter || role == AiConstants.RoleReconStrike;
        }

        internal static bool IsFootUnit(IAiUnitCatalog catalog, string unitId)
        {
            if (catalog == null || string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            MovementClass moveClass;
            if (!catalog.TryGetMovementClass(unitId, out moveClass))
            {
                return false;
            }

            return moveClass == MovementClass.Foot;
        }

        private bool PrizeGroupIsReady(
            AiScenarioSeatView seat,
            AiBattalionPin[] battalions,
            int battalionCount)
        {
            int present = CountPrizeGroupMembers(battalions, battalionCount);
            if (CountAuthoredPrizeGroupMembers(seat) >= _taskForceTargetSize)
            {
                return present >= _taskForceTargetSize;
            }

            return CountPrizeRifles(battalions, battalionCount) >= _taskForceTargetSize;
        }

        private int CountPrizeRifles(AiBattalionPin[] battalions, int battalionCount)
        {
            int count = 0;
            for (int i = 0; i < battalionCount; i++)
            {
                if (IsPrizeRifle(_catalog, battalions[i].UnitId))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountPrizeGroupMembers(AiBattalionPin[] battalions, int battalionCount)
        {
            int count = 0;
            for (int i = 0; i < battalionCount; i++)
            {
                string unitId = battalions[i].UnitId;
                if (IsPrizeRifle(_catalog, unitId) || IsPrizeScout(_catalog, unitId))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountAuthoredPrizeGroupMembers(AiScenarioSeatView seat)
        {
            AiStartingUnit[] starting = seat.StartingUnits;
            if (starting == null || starting.Length == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < starting.Length; i++)
            {
                AiStartingUnit row = starting[i];
                if (row.Count < 1)
                {
                    continue;
                }

                if (IsPrizeRifle(_catalog, row.UnitId) || IsPrizeScout(_catalog, row.UnitId))
                {
                    count += row.Count;
                }
            }

            return count;
        }

        private static int CountMixRiflesAcross(
            AiBattalionPin[] battalions,
            int battalionCount,
            string[]? queuedUnitIds,
            int queuedCount,
            string[]? pendingUnitIds,
            int pendingCount,
            IAiUnitCatalog catalog)
        {
            int count = 0;
            if (battalions != null)
            {
                for (int i = 0; i < battalionCount; i++)
                {
                    if (CountsAsMixRifle(catalog, battalions[i].UnitId))
                    {
                        count++;
                    }
                }
            }

            return count
                + CountMixRifleIds(queuedUnitIds, queuedCount, catalog)
                + CountMixRifleIds(pendingUnitIds, pendingCount, catalog);
        }

        private static int CountMixRifleIds(string[]? unitIds, int count, IAiUnitCatalog catalog)
        {
            if (unitIds == null)
            {
                return 0;
            }

            int n = count;
            if (n > unitIds.Length)
            {
                n = unitIds.Length;
            }

            int found = 0;
            for (int i = 0; i < n; i++)
            {
                if (CountsAsMixRifle(catalog, unitIds[i]))
                {
                    found++;
                }
            }

            return found;
        }

        private static bool TryTownXy(
            AiAttackTownInfo[] towns,
            int townCount,
            int townId,
            out int xCm,
            out int yCm)
        {
            for (int i = 0; i < townCount; i++)
            {
                if (towns[i].Id == townId)
                {
                    xCm = towns[i].XCm;
                    yCm = towns[i].YCm;
                    return true;
                }
            }

            xCm = 0;
            yCm = 0;
            return false;
        }

        private static int CountRole(
            AiBattalionPin[] battalions,
            int battalionCount,
            IAiUnitCatalog catalog,
            string role)
        {
            int count = 0;
            if (battalions == null)
            {
                return 0;
            }

            for (int i = 0; i < battalionCount; i++)
            {
                if (battalions[i].HasRole(catalog, role))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRoleAcross(
            AiBattalionPin[] battalions,
            int battalionCount,
            string[]? queuedUnitIds,
            int queuedCount,
            string[]? pendingUnitIds,
            int pendingCount,
            IAiUnitCatalog catalog,
            string role)
        {
            return CountRole(battalions, battalionCount, catalog, role)
                + CountUnitIds(queuedUnitIds, queuedCount, catalog, role)
                + CountUnitIds(pendingUnitIds, pendingCount, catalog, role);
        }

        private static int CountUnitIds(
            string[]? unitIds,
            int count,
            IAiUnitCatalog catalog,
            string role)
        {
            if (unitIds == null || catalog == null || role == null)
            {
                return 0;
            }

            int n = count;
            if (n > unitIds.Length)
            {
                n = unitIds.Length;
            }

            int found = 0;
            for (int i = 0; i < n; i++)
            {
                string id = unitIds[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (catalog.TryGetRole(id, out string have) && have == role)
                {
                    found++;
                }
            }

            return found;
        }

        private bool IssueClaimMarch(
            int tick,
            AiFactionState faction,
            int battalionId,
            string unitId,
            AiAttackTownInfo target)
        {
            if (!UnitCanCapture(unitId, battalionId))
            {
                return false;
            }

            if (MoveBlocked(faction, battalionId, CommandKind.ClaimTown, target.Id, target.NodeId, tick))
            {
                return false;
            }

            bool retargetHq = _forceHqMarch && target.IsHq;
            if (!AlreadyOnTheWay(faction.FactionId, battalionId, target.XCm, target.YCm)
                && (retargetHq || !AlreadyMarching(faction.FactionId, battalionId))
                && !BattalionOccupiesClaim(faction, battalionId, target))
            {
                int moveId = _commands.SubmitMove(faction.FactionId, battalionId, target.XCm, target.YCm);
                Issue(tick, faction, moveId);
                if (_commands.RejectOf(moveId) != AiCommandReject.None)
                {
                    return false;
                }
            }

            if (ShouldSkipClaimTown(faction, battalionId, target))
            {
                return true;
            }

            int claimId = _commands.SubmitClaimTown(faction.FactionId, battalionId, target.Id);
            Issue(tick, faction, claimId);
            if (_commands.RejectOf(claimId) != AiCommandReject.None)
            {
                return false;
            }

            if (_owned != null)
            {
                _owned.NoteAcceptedClaimTown(faction.FactionId, target.Id);
            }

            return true;
        }

        internal static bool ClaimTownIsOccupiedBy(
            TownView town,
            int factionId,
            int battalionXCm,
            int battalionYCm)
        {
            if (town.OwnerFactionId == factionId || town.CaptureFactionId == factionId)
            {
                return true;
            }

            if (town.ZoneRadiusCm <= 0)
            {
                return false;
            }

            long dx = (long)battalionXCm - town.XCm;
            long dy = (long)battalionYCm - town.YCm;
            long radiusSq = (long)town.ZoneRadiusCm * town.ZoneRadiusCm;
            return (dx * dx) + (dy * dy) <= radiusSq;
        }

        private bool BattalionOccupiesClaim(
            AiFactionState faction,
            int battalionId,
            AiAttackTownInfo target)
        {
            if (_world == null || battalionId == 0)
            {
                return false;
            }

            if (!_world.TryGetTown(target.Id, out TownView town))
            {
                return false;
            }

            if (!_world.TryGetOwnBattalion(faction.FactionId, battalionId, out SimBattalion battalion))
            {
                return false;
            }

            return ClaimTownIsOccupiedBy(
                town,
                faction.FactionId,
                battalion.PositionXCm,
                battalion.PositionYCm);
        }

        private bool ShouldSkipClaimTown(AiFactionState faction, int battalionId, AiAttackTownInfo target)
        {
            if (_owned != null)
            {
                if (_owned.HasPendingClaimTown(faction.FactionId, target.Id))
                {
                    return true;
                }

                if (_owned.AcceptedClaimTowns(faction.FactionId, target.Id)
                    >= AiConstants.MaxClaimTownsPerSeatTown)
                {
                    return true;
                }
            }

            if (_world == null || !_world.TryGetTown(target.Id, out TownView town))
            {
                return false;
            }

            if (town.OwnerFactionId == faction.FactionId || town.CaptureFactionId == faction.FactionId)
            {
                return true;
            }

            return BattalionOccupiesClaim(faction, battalionId, target);
        }

        private void EnsureBuilderMarchingTo(
            int tick,
            AiFactionState faction,
            AiBattalionPin[] battalions,
            int battalionCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int nodeId)
        {
            if (nodeId == 0 || _world == null || !graph.TryGetNodeIndex(nodeId, out int nodeIndex))
            {
                return;
            }

            int builderId = 0;
            _world.CopyDepots(_depotScratch);
            for (int i = 0; i < _depotScratch.Count; i++)
            {
                DepotView depot = _depotScratch[i];
                if (depot.OwnerFactionId != faction.FactionId || depot.NodeId != nodeId)
                {
                    continue;
                }

                if (depot.State == DepotSiteState.Active)
                {
                    continue;
                }

                builderId = depot.BuilderBattalionId;
                break;
            }

            if (builderId == 0)
            {
                return;
            }

            if (MoveBlocked(faction, builderId, CommandKind.BuildDepot, nodeId, nodeId, tick))
            {
                return;
            }

            if (!_world.TryGetOwnBattalion(faction.FactionId, builderId, out SimBattalion battalion)
                || battalion.Hp <= 0)
            {
                return;
            }

            if (battalion.HasTarget)
            {
                return;
            }

            AiBattalionPin pin = FindPin(battalions, battalionCount, builderId);
            int atNode = pin.Id == builderId ? pin.RoadNodeId : 0;
            if (atNode == nodeId)
            {
                return;
            }

            int moveId = _commands.SubmitMove(
                faction.FactionId,
                builderId,
                graph.GetNodeXCm(nodeIndex),
                graph.GetNodeYCm(nodeIndex));
            Issue(tick, faction, moveId);
        }

        private bool AlreadyMarching(int factionId, int battalionId)
        {
            if (_world == null || battalionId == 0)
            {
                return false;
            }

            if (!_world.TryGetOwnBattalion(factionId, battalionId, out SimBattalion battalion))
            {
                return false;
            }

            return battalion.HasTarget;
        }

        private bool AlreadyOnTheWay(int factionId, int battalionId, int xCm, int yCm)
        {
            if (_world == null || battalionId == 0)
            {
                return false;
            }

            if (!_world.TryGetOwnBattalion(factionId, battalionId, out SimBattalion battalion))
            {
                return false;
            }

            return IsMarchingTo(battalion, xCm, yCm);
        }

        /// <summary>
        /// A road move is only "already going there" when the last waypoint is
        /// the new dest. Any other dest can pull the unit off the march.
        /// </summary>
        internal static bool IsMarchingTo(SimBattalion battalion, int xCm, int yCm)
        {
            if (!battalion.HasTarget)
            {
                return battalion.TargetXCm == xCm && battalion.TargetYCm == yCm;
            }

            if (battalion.PathCount > 0)
            {
                if (battalion.PathXCm == null || battalion.PathYCm == null)
                {
                    return false;
                }

                int last = battalion.PathCount - 1;
                if (last >= battalion.PathXCm.Length || last >= battalion.PathYCm.Length)
                {
                    return false;
                }

                return battalion.PathXCm[last] == xCm && battalion.PathYCm[last] == yCm;
            }

            return battalion.TargetXCm == xCm && battalion.TargetYCm == yCm;
        }

        private bool UnitCanCapture(string unitId)
        {
            return UnitCanCapture(unitId, 0);
        }

        private bool UnitCanCapture(string unitId, int battalionId)
        {
            if (!string.IsNullOrEmpty(unitId)
                && _catalog.TryGetCanCapture(unitId, out bool canCapture))
            {
                return canCapture;
            }

            if (battalionId != 0
                && _world != null
                && _world.TryGetBattalionView(battalionId, out BattalionView view)
                && !string.IsNullOrEmpty(view.UnitId)
                && _catalog.TryGetCanCapture(view.UnitId, out bool fromView))
            {
                return fromView;
            }

            return false;
        }

        private bool MoveBlocked(
            AiFactionState faction,
            int battalionId,
            CommandKind kind,
            int targetId,
            int tick)
        {
            return MoveBlocked(faction, battalionId, kind, targetId, 0, tick);
        }

        private bool MoveBlocked(
            AiFactionState faction,
            int battalionId,
            CommandKind kind,
            int targetId,
            int moveNodeId,
            int tick)
        {
            if (_owned == null || battalionId == 0)
            {
                return false;
            }

            if (_owned.IsOrderBackedOff(faction.FactionId, battalionId, kind, targetId, tick))
            {
                return true;
            }

            if (kind == CommandKind.Move)
            {
                return false;
            }

            return moveNodeId != 0
                && _owned.IsOrderBackedOff(
                    faction.FactionId,
                    battalionId,
                    CommandKind.Move,
                    moveNodeId,
                    tick);
        }

        private void NoteIssuedReject(int tick, AiFactionState faction, int commandId)
        {
            if (_owned == null || commandId == 0)
            {
                return;
            }

            IReadOnlyList<AiIssuedCommand> issued = _commands.IssuedCommands;
            if (issued == null)
            {
                return;
            }

            for (int i = issued.Count - 1; i >= 0; i--)
            {
                AiIssuedCommand cmd = issued[i];
                if (cmd.Id != commandId || cmd.FactionId != faction.FactionId)
                {
                    continue;
                }

                CommandKind kind = CommandKindOf(cmd.Kind);
                int battalionId = cmd.Arg0;
                int targetId = TargetOfIssued(cmd);
                if (kind == CommandKind.RaiseBattalion || battalionId == 0)
                {
                    return;
                }

                _owned.NoteOrderReject(faction.FactionId, battalionId, kind, targetId, tick);
                return;
            }
        }

        private static CommandKind CommandKindOf(AiIssuedKind kind)
        {
            if (kind == AiIssuedKind.ClaimTown)
            {
                return CommandKind.ClaimTown;
            }

            if (kind == AiIssuedKind.BuildDepot)
            {
                return CommandKind.BuildDepot;
            }

            if (kind == AiIssuedKind.RaiseBattalion)
            {
                return CommandKind.RaiseBattalion;
            }

            if (kind == AiIssuedKind.Attack)
            {
                return CommandKind.Attack;
            }

            return CommandKind.Move;
        }

        private int TargetOfIssued(AiIssuedCommand cmd)
        {
            if (cmd.Kind == AiIssuedKind.ClaimTown || cmd.Kind == AiIssuedKind.BuildDepot)
            {
                return cmd.Arg1;
            }

            if (cmd.Kind == AiIssuedKind.Attack)
            {
                return cmd.Arg1;
            }

            if (cmd.Kind == AiIssuedKind.Move)
            {
                return MoveDestinationNode(_world, cmd.Arg1, cmd.Arg2);
            }

            return 0;
        }

        internal static int MoveDestinationNode(ISimWorldView? world, int xCm, int yCm)
        {
            if (world == null)
            {
                return 0;
            }

            return world.GetNearestNodeId(xCm, yCm);
        }

        private int NearestIdle(
            AiFactionState faction,
            Wasteland.Sim.Movement.RoadGraph graph,
            int toNodeId,
            AiBattalionPin[] battalions,
            int battalionCount,
            string role)
        {
            return NearestIdle(faction, graph, toNodeId, battalions, battalionCount, role, false, false, 0);
        }

        private int NearestIdle(
            AiFactionState faction,
            Wasteland.Sim.Movement.RoadGraph graph,
            int toNodeId,
            AiBattalionPin[] battalions,
            int battalionCount,
            string role,
            bool prizeMarch)
        {
            return NearestIdle(faction, graph, toNodeId, battalions, battalionCount, role, prizeMarch, false, 0);
        }

        private int NearestIdle(
            AiFactionState faction,
            Wasteland.Sim.Movement.RoadGraph graph,
            int toNodeId,
            AiBattalionPin[] battalions,
            int battalionCount,
            string role,
            bool prizeMarch,
            bool capturersOnly,
            int claimTownId,
            bool skipEnRoute = false,
            int enRouteXCm = 0,
            int enRouteYCm = 0)
        {
            int bestId = 0;
            int bestDist = AiConstants.UnreachableCm;
            for (int i = 0; i < battalionCount; i++)
            {
                AiBattalionPin pin = battalions[i];
                if (pin.Assigned || IsHeld(faction, pin.Id))
                {
                    continue;
                }

                if (skipEnRoute && AlreadyOnTheWay(faction.FactionId, pin.Id, enRouteXCm, enRouteYCm))
                {
                    continue;
                }

                if (prizeMarch && IsFootUnit(pin.UnitId))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(role) && !pin.HasRole(_catalog, role))
                {
                    continue;
                }

                if (capturersOnly && !UnitCanCapture(pin.UnitId, pin.Id))
                {
                    continue;
                }

                if (claimTownId != 0
                    && MoveBlocked(
                        faction,
                        pin.Id,
                        CommandKind.ClaimTown,
                        claimTownId,
                        toNodeId,
                        _commands.Tick))
                {
                    continue;
                }

                AiSupplyResult dist = _roads.DistanceCm(graph, pin.RoadNodeId, toNodeId, faction);
                if (DeferIfExhausted(dist))
                {
                    return 0;
                }

                int d = dist.IsReached ? dist.DistanceCm : AiConstants.UnreachableCm;
                int cmp = d.CompareTo(bestDist);
                if (cmp > 0)
                {
                    continue;
                }

                if (cmp == 0 && bestId != 0 && pin.Id >= bestId)
                {
                    continue;
                }

                bestDist = d;
                bestId = pin.Id;
            }

            return bestId;
        }

        private static AiBattalionPin FindPin(AiBattalionPin[] battalions, int battalionCount, int id)
        {
            for (int i = 0; i < battalionCount; i++)
            {
                if (battalions[i].Id == id)
                {
                    return battalions[i];
                }
            }

            return default;
        }

        internal void ExpireAttackOrders(int tick, AiFactionState faction)
        {
            if (faction == null || _owned == null)
            {
                return;
            }

            for (int i = faction.IntentCount - 1; i >= 0; i--)
            {
                AiPendingIntent intent = faction.Intents[i];
                if (intent.Kind != AiIssuedKind.Attack)
                {
                    continue;
                }

                if (intent.IssuedTick > tick)
                {
                    continue;
                }

                if (tick - intent.IssuedTick < AiCombatTable.AttackTimeoutTicks)
                {
                    continue;
                }

                int battalionId = intent.BattalionId;
                int targetId = intent.TargetId;
                _owned.NoteOrderReject(
                    faction.FactionId,
                    battalionId,
                    CommandKind.Attack,
                    targetId,
                    tick);
                faction.RemoveIntentAt(i);
                ClearTimedOutAttack(tick, faction, battalionId, targetId);
            }
        }

        internal static void ClearDestroyedBattalion(AiOwnedState owned, int battalionId)
        {
            if (owned == null || battalionId == 0)
            {
                return;
            }

            owned.ClearBattalionCombatMemory(battalionId);
            for (int f = 0; f < owned.FactionCount; f++)
            {
                AiFactionState faction = owned.FactionAt(f);
                RemoveOrdersForBattalion(faction, battalionId);
                for (int i = faction.IntentCount - 1; i >= 0; i--)
                {
                    AiPendingIntent intent = faction.Intents[i];
                    bool drop = intent.BattalionId == battalionId
                        || (intent.Kind == AiIssuedKind.Attack && intent.TargetId == battalionId);
                    if (drop)
                    {
                        faction.RemoveIntentAt(i);
                    }
                }
            }
        }

        internal void SyncHqRecall(
            int tick,
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            AiBattalionPin[] battalions,
            int battalionCount,
            RoadGraph graph)
        {
            if (faction == null || _world == null || graph == null || towns == null)
            {
                return;
            }

            if (!TryTownXy(towns, townCount, faction.HomeTownId, out int hqX, out int hqY))
            {
                return;
            }

            int hqNode = _world.GetNearestNodeId(hqX, hqY);
            if (hqNode == 0)
            {
                return;
            }

            // A road search that runs out of work units returns before any hold
            // is released or added, so the previous recall set stays as it was.
            _world.CopyCombatants(faction.FactionId, _combatScratch);
            int threatStrength = 0;
            for (int i = 0; i < _combatScratch.Count; i++)
            {
                AiCombatantView combatant = _combatScratch[i];
                if (combatant.Hp <= 0 || combatant.OwnerSeat <= 0 || combatant.OwnerSeat == faction.FactionId)
                {
                    continue;
                }

                int enemyNode = _world.GetNearestNodeId(combatant.XCm, combatant.YCm);
                if (enemyNode == 0)
                {
                    continue;
                }

                AiSupplyResult dist = _roads.DistanceCm(graph, hqNode, enemyNode, faction);
                if (dist.IsBudgetExhausted)
                {
                    // Keep the previous turn's holds and retry the scan next turn.
                    return;
                }

                if (!dist.IsReached || dist.DistanceCm > AiCombatTable.HqThreatRadiusCm)
                {
                    continue;
                }

                long sum = (long)threatStrength + combatant.Hp;
                threatStrength = sum > int.MaxValue ? int.MaxValue : (int)sum;
            }

            if (threatStrength < 1)
            {
                ReleaseHqHolds(faction);
                return;
            }

            if (!RankRecall(faction, graph, hqNode, battalions, battalionCount))
            {
                return;
            }

            int recall = AiCombatTable.CountToCover(_rankStrength, _rankCount, threatStrength);

            ReleaseHqHolds(faction);
            for (int i = 0; i < recall; i++)
            {
                HoldAtHq(tick, faction, _rankId[i], hqX, hqY, hqNode);
            }
        }

        internal void IssueTownCombat(
            int tick,
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            AiBattalionPin[] battalions,
            int battalionCount,
            int extraBattalionId)
        {
            if (faction == null || _world == null || faction.AttackTargetTownId == 0)
            {
                return;
            }

            AiAttackTownInfo target = default;
            bool found = false;
            for (int i = 0; i < townCount; i++)
            {
                if (towns[i].Id == faction.AttackTargetTownId)
                {
                    target = towns[i];
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }

            int townNode = _world.GetNearestNodeId(target.XCm, target.YCm);
            _world.CopyCombatants(faction.FactionId, _combatScratch);
            int garrisonId = 0;
            int defenderId = 0;
            for (int i = 0; i < _combatScratch.Count; i++)
            {
                AiCombatantView combatant = _combatScratch[i];
                if (combatant.Hp <= 0 || !AtTown(combatant, target, townNode))
                {
                    continue;
                }

                if (combatant.OwnerSeat == SimWorld.NeutralOwnerId)
                {
                    if (garrisonId == 0 || combatant.Id < garrisonId)
                    {
                        garrisonId = combatant.Id;
                    }
                }
                else if (combatant.OwnerSeat != faction.FactionId)
                {
                    if (defenderId == 0 || combatant.Id < defenderId)
                    {
                        defenderId = combatant.Id;
                    }
                }
            }

            int[] order = garrisonId != 0
                ? new[] { garrisonId, defenderId }
                : new[] { defenderId };
            for (int s = 0; s < battalionCount; s++)
            {
                AiBattalionPin pin = battalions[s];
                if (!ShouldShootTown(faction, pin, target, extraBattalionId))
                {
                    continue;
                }

                int chosen = 0;
                for (int t = 0; t < order.Length; t++)
                {
                    int candidate = order[t];
                    if (candidate == 0)
                    {
                        continue;
                    }

                    if (MoveBlocked(faction, pin.Id, CommandKind.Attack, candidate, tick))
                    {
                        continue;
                    }

                    chosen = candidate;
                    break;
                }

                if (chosen == 0)
                {
                    continue;
                }

                if (AlreadyAttacking(faction, pin.Id, chosen, tick))
                {
                    continue;
                }

                IssueAttack(tick, faction, pin.Id, chosen);
            }
        }

        internal static int PickTownAttackTarget(
            AiCombatantView[] combatants,
            int count,
            int townNodeId,
            int townXCm,
            int townYCm,
            int zoneRadiusCm)
        {
            int garrisonId = 0;
            int defenderId = 0;
            AiAttackTownInfo town = new AiAttackTownInfo(
                0,
                0,
                townNodeId,
                townXCm,
                townYCm,
                false,
                0UL,
                zoneRadiusCm);
            for (int i = 0; i < count; i++)
            {
                AiCombatantView combatant = combatants[i];
                if (combatant.Hp <= 0 || !CombatantAtTown(combatant, town, townNodeId))
                {
                    continue;
                }

                if (combatant.OwnerSeat == SimWorld.NeutralOwnerId)
                {
                    if (garrisonId == 0 || combatant.Id < garrisonId)
                    {
                        garrisonId = combatant.Id;
                    }
                }
                else if (defenderId == 0 || combatant.Id < defenderId)
                {
                    defenderId = combatant.Id;
                }
            }

            return garrisonId != 0 ? garrisonId : defenderId;
        }

        private void IssueAttack(int tick, AiFactionState faction, int battalionId, int targetBattalionId)
        {
            int sequence = _commands.SubmitAttack(faction.FactionId, battalionId, targetBattalionId);
            Issue(tick, faction, IssuedIdForSequence(faction.FactionId, sequence));
        }

        private int IssuedIdForSequence(int factionId, int sequence)
        {
            IReadOnlyList<AiIssuedCommand> issued = _commands.IssuedCommands;
            if (issued == null)
            {
                return sequence;
            }

            for (int i = issued.Count - 1; i >= 0; i--)
            {
                if (issued[i].FactionId == factionId && issued[i].Sequence == sequence)
                {
                    return issued[i].Id;
                }
            }

            return sequence;
        }

        private bool AlreadyAttacking(AiFactionState faction, int battalionId, int targetId, int tick)
        {
            if (_world != null
                && _world.TryGetOwnBattalion(faction.FactionId, battalionId, out SimBattalion battalion)
                && battalion.AttackTargetId == targetId)
            {
                return true;
            }

            for (int i = 0; i < faction.IntentCount; i++)
            {
                AiPendingIntent intent = faction.Intents[i];
                if (intent.Kind != AiIssuedKind.Attack
                    || intent.BattalionId != battalionId
                    || intent.TargetId != targetId)
                {
                    continue;
                }

                if (intent.IssuedTick <= tick && tick - intent.IssuedTick < AiCombatTable.AttackTimeoutTicks)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearTimedOutAttack(int tick, AiFactionState faction, int battalionId, int targetId)
        {
            if (_world == null || faction.AttackTargetTownId == 0)
            {
                return;
            }

            if (!_world.TryGetOwnBattalion(faction.FactionId, battalionId, out SimBattalion battalion))
            {
                return;
            }

            if (battalion.AttackTargetId != targetId)
            {
                return;
            }

            if (!_world.TryGetTown(faction.AttackTargetTownId, out TownView town))
            {
                return;
            }

            if (IsHeld(faction, battalionId))
            {
                return;
            }

            if (IsMarchingTo(battalion, town.XCm, town.YCm))
            {
                return;
            }

            int moveId = _commands.SubmitMove(faction.FactionId, battalionId, town.XCm, town.YCm);
            Issue(tick, faction, moveId);
        }

        private bool ShouldShootTown(
            AiFactionState faction,
            AiBattalionPin pin,
            AiAttackTownInfo target,
            int extraBattalionId)
        {
            if (pin.Id == 0 || IsHeld(faction, pin.Id))
            {
                return false;
            }

            if (IsEngineerOrSupply(pin.UnitId))
            {
                return false;
            }

            bool prize = target.TerritoryStableKey != 0UL
                && faction.AttackTargetTownId == target.Id;
            if (prize && IsFootUnit(pin.UnitId))
            {
                return false;
            }

            if (pin.Id == extraBattalionId || WasMarched(pin.Id))
            {
                return true;
            }

            return HasPrizeOrder(faction, pin.Id, target.Id);
        }

        private void NoteMarched(int battalionId)
        {
            if (battalionId == 0 || WasMarched(battalionId))
            {
                return;
            }

            _marchedScratch.Add(battalionId);
        }

        private bool WasMarched(int battalionId)
        {
            for (int i = 0; i < _marchedScratch.Count; i++)
            {
                if (_marchedScratch[i] == battalionId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsEngineerOrSupply(string unitId)
        {
            if (_catalog == null || string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            if (!_catalog.TryGetRole(unitId, out string role))
            {
                return false;
            }

            return role == AiConstants.RoleEngineer || role == "supply";
        }

        private bool AtTown(AiCombatantView combatant, AiAttackTownInfo town, int townNodeId)
        {
            int nodeId = combatant.NodeId;
            if (_world != null)
            {
                int snapped = _world.GetNearestNodeId(combatant.XCm, combatant.YCm);
                if (snapped != 0)
                {
                    nodeId = snapped;
                }
            }

            if (town.ZoneRadiusCm > 0)
            {
                long dx = (long)combatant.XCm - town.XCm;
                long dy = (long)combatant.YCm - town.YCm;
                long radius = town.ZoneRadiusCm;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    return true;
                }
            }

            return townNodeId != 0 && nodeId == townNodeId;
        }

        private static bool CombatantAtTown(AiCombatantView combatant, AiAttackTownInfo town, int townNodeId)
        {
            if (town.ZoneRadiusCm > 0)
            {
                long dx = (long)combatant.XCm - town.XCm;
                long dy = (long)combatant.YCm - town.YCm;
                long radius = town.ZoneRadiusCm;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    return true;
                }
            }

            return townNodeId != 0 && combatant.NodeId == townNodeId;
        }

        private bool RankRecall(
            AiFactionState faction,
            RoadGraph graph,
            int hqNode,
            AiBattalionPin[] battalions,
            int battalionCount)
        {
            if (battalions == null)
            {
                _rankCount = 0;
                return true;
            }

            EnsureRank(battalionCount);
            int count = 0;
            for (int i = 0; i < battalionCount; i++)
            {
                AiBattalionPin pin = battalions[i];
                if (pin.Id == 0)
                {
                    continue;
                }

                int nodeId = pin.RoadNodeId;
                if (_world != null)
                {
                    int snapped = _world.GetNearestNodeId(pin.XCm, pin.YCm);
                    if (snapped != 0)
                    {
                        nodeId = snapped;
                    }
                }

                if (nodeId == 0)
                {
                    continue;
                }

                AiSupplyResult dist = _roads.DistanceCm(graph, hqNode, nodeId, faction);
                if (dist.IsBudgetExhausted)
                {
                    // Keep the previous turn's holds and retry the scan next turn.
                    return false;
                }

                int distance = dist.IsReached ? dist.DistanceCm : AiConstants.UnreachableCm;
                int commit = CommittedToAttack(faction, pin.Id) ? 1 : 0;
                int strength = OwnHitPoints(faction, pin.Id);
                int insert = count;
                while (insert > 0 && RecallBefore(
                    distance,
                    commit,
                    pin.Id,
                    _rankDist[insert - 1],
                    _rankCommit[insert - 1],
                    _rankId[insert - 1]))
                {
                    _rankDist[insert] = _rankDist[insert - 1];
                    _rankCommit[insert] = _rankCommit[insert - 1];
                    _rankStrength[insert] = _rankStrength[insert - 1];
                    _rankId[insert] = _rankId[insert - 1];
                    insert--;
                }

                _rankDist[insert] = distance;
                _rankCommit[insert] = commit;
                _rankStrength[insert] = strength;
                _rankId[insert] = pin.Id;
                count++;
            }

            _rankCount = count;
            return true;
        }

        private static bool RecallBefore(int dist, int commit, int id, int bestDist, int bestCommit, int bestId)
        {
            int cmp = dist.CompareTo(bestDist);
            if (cmp != 0)
            {
                return cmp < 0;
            }

            if (commit != bestCommit)
            {
                return commit < bestCommit;
            }

            return id < bestId;
        }

        private bool CommittedToAttack(AiFactionState faction, int battalionId)
        {
            if (faction.AttackTargetTownId == 0)
            {
                return false;
            }

            return HasPrizeOrder(faction, battalionId, faction.AttackTargetTownId);
        }

        private int OwnHitPoints(AiFactionState faction, int battalionId)
        {
            if (_world != null
                && _world.TryGetOwnBattalion(faction.FactionId, battalionId, out SimBattalion battalion)
                && battalion.Hp > 0)
            {
                return battalion.Hp;
            }

            return 1;
        }

        private void EnsureRank(int count)
        {
            if (_rankId.Length >= count)
            {
                return;
            }

            int n = count < 8 ? 8 : count;
            _rankId = new int[n];
            _rankDist = new int[n];
            _rankCommit = new int[n];
            _rankStrength = new int[n];
        }

        private void HoldAtHq(
            int tick,
            AiFactionState faction,
            int battalionId,
            int hqX,
            int hqY,
            int hqNode)
        {
            StripOrdersForBattalion(faction, battalionId);
            if (faction.OrderCount >= faction.Orders.Length)
            {
                return;
            }

            AiOrder order = default;
            order.Id = faction.NextOrderId++;
            order.Type = AiGoalType.DefendTown;
            order.TargetTownId = faction.HomeTownId;
            order.TargetTerritoryId = faction.HomeTerritoryId;
            order.BattalionId = battalionId;
            order.Score = AiCombatTable.HqHoldScore;
            faction.Orders[faction.OrderCount++] = order;

            if (_world == null)
            {
                return;
            }

            if (_world.TryGetOwnBattalion(faction.FactionId, battalionId, out SimBattalion battalion))
            {
                int atNode = _world.GetNearestNodeId(battalion.PositionXCm, battalion.PositionYCm);
                if (atNode == hqNode || IsMarchingTo(battalion, hqX, hqY))
                {
                    return;
                }
            }

            if (MoveBlocked(faction, battalionId, CommandKind.Move, hqNode, tick))
            {
                return;
            }

            int moveId = _commands.SubmitMove(faction.FactionId, battalionId, hqX, hqY);
            Issue(tick, faction, moveId);
        }

        private static void ReleaseHqHolds(AiFactionState faction)
        {
            int write = 0;
            for (int i = 0; i < faction.OrderCount; i++)
            {
                AiOrder order = faction.Orders[i];
                if (order.Type == AiGoalType.DefendTown
                    && order.TargetTownId == faction.HomeTownId
                    && order.Score == AiCombatTable.HqHoldScore)
                {
                    continue;
                }

                faction.Orders[write++] = order;
            }

            faction.OrderCount = write;
        }

        private static void StripOrdersForBattalion(AiFactionState faction, int battalionId)
        {
            RemoveOrdersForBattalion(faction, battalionId);
        }

        private static void RemoveOrdersForBattalion(AiFactionState faction, int battalionId)
        {
            int write = 0;
            for (int i = 0; i < faction.OrderCount; i++)
            {
                if (faction.Orders[i].BattalionId == battalionId)
                {
                    continue;
                }

                faction.Orders[write++] = faction.Orders[i];
            }

            faction.OrderCount = write;
        }

        private static bool IsHeld(AiFactionState faction, int battalionId)
        {
            if (faction == null || battalionId == 0)
            {
                return false;
            }

            for (int i = 0; i < faction.OrderCount; i++)
            {
                AiOrder order = faction.Orders[i];
                if (order.BattalionId == battalionId
                    && order.Type == AiGoalType.DefendTown
                    && order.TargetTownId == faction.HomeTownId
                    && order.Score == AiCombatTable.HqHoldScore)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SortTowns(AiAttackTownInfo[] towns, int count)
        {
            for (int i = 1; i < count; i++)
            {
                AiAttackTownInfo row = towns[i];
                int j = i - 1;
                while (j >= 0 && towns[j].Id > row.Id)
                {
                    towns[j + 1] = towns[j];
                    j--;
                }

                towns[j + 1] = row;
            }
        }

        private static void SortBattalions(AiBattalionPin[] battalions, int count)
        {
            if (battalions == null)
            {
                return;
            }

            for (int i = 1; i < count; i++)
            {
                AiBattalionPin row = battalions[i];
                int j = i - 1;
                while (j >= 0 && battalions[j].Id > row.Id)
                {
                    battalions[j + 1] = battalions[j];
                    j--;
                }

                battalions[j + 1] = row;
            }
        }
    }
}
