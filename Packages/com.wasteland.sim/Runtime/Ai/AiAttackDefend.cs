using System;

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

        public AiAttackTownInfo(
            int id,
            int territoryId,
            int nodeId,
            int xCm,
            int yCm,
            bool isHq,
            ulong territoryStableKey)
        {
            Id = id;
            TerritoryId = territoryId;
            NodeId = nodeId;
            XCm = xCm;
            YCm = yCm;
            IsHq = isHq;
            TerritoryStableKey = territoryStableKey;
        }
    }

    /// <summary>
    /// Attack the nearest enemy town, preferring the enemy HQ once it is in
    /// supply. Place a forward depot with an engineer first when the target is
    /// beyond supply reach. Defend own HQ first; defence outranks attack.
    /// </summary>
    internal sealed class AiAttackDefend
    {
        public const int RejectBackoffStartTicks = 8;
        public const int RejectBackoffMaxTicks = 128;

        private readonly IAiCommandSink _commands;
        private readonly IAiTownQuery _towns;
        private readonly IAiDepotQuery _depots;
        private readonly AiRoadSupply _roads = new AiRoadSupply();
        private IAiSeatView _seatView = new FixtureAiSeatView();
        private IAiUnitCatalog _catalog = AiUnitCatalog.Empty;
        private IAiBuildingCatalog _buildings = AiBuildingCatalog.Empty;
        private int _lineUnitsPerAntiArmour = 2;
        private int _minGarrisonPerTown;

        public AiAttackDefend(IAiCommandSink commands, IAiTownQuery towns, IAiDepotQuery depots)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _towns = towns ?? throw new ArgumentNullException(nameof(towns));
            _depots = depots ?? throw new ArgumentNullException(nameof(depots));
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
            int minGarrisonPerTown)
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

            _seatView = seatView;
            _catalog = catalog;
            _buildings = buildings;
            _lineUnitsPerAntiArmour = lineUnitsPerAntiArmour;
            _minGarrisonPerTown = minGarrisonPerTown;

            if (towns == null)
            {
                throw new ArgumentNullException(nameof(towns));
            }

            if (_commands is RecordingAiCommandSink recorded)
            {
                recorded.Tick = tick;
            }

            if (faction.RejectBackoffUntilTick > 0 && tick < faction.RejectBackoffUntilTick)
            {
                return;
            }

            SortTowns(towns, townCount);
            SortBattalions(battalions, battalionCount);

            if (TryDefend(tick, faction, towns, townCount, battalions, battalionCount, graph))
            {
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
            int prizeTargetBonusPpt)
        {
            int nearestId = 0;
            int nearestDist = AiConstants.UnreachableCm;
            bool nearestPrize = false;
            int hqId = 0;
            int hqDist = AiConstants.UnreachableCm;
            bool hqPrize = false;
            int hqNodeId = 0;
            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                if (readback.OwnerFactionId == 0 || readback.OwnerFactionId == faction.FactionId)
                {
                    continue;
                }

                AiSupplyResult dist = _roads.DistanceCm(graph, fromNodeId, town.NodeId, faction);
                if (dist.IsBudgetExhausted)
                {
                    return 0;
                }

                int d = dist.IsReached ? dist.DistanceCm : AiConstants.UnreachableCm;
                bool isPrize = prizeTerritoryKey != 0UL && town.TerritoryStableKey == prizeTerritoryKey;
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
            for (int i = 0; i < townCount; i++)
            {
                AiAttackTownInfo town = towns[i];
                if (!_towns.TryGet(town.Id, out AiTownReadback readback))
                {
                    continue;
                }

                bool isOwnHq = town.Id == faction.HomeTownId;
                if (readback.OwnerFactionId != faction.FactionId && !isOwnHq)
                {
                    continue;
                }

                if (!readback.Contested && !isOwnHq)
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
            if (battalionId == 0)
            {
                return true;
            }

            Issue(tick, faction, _commands.SubmitMove(faction.FactionId, battalionId, defendX, defendY));
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

            int own = CountIdle(battalions, battalionCount) + readback.StrengthOf(faction.FactionId);
            int defender = readback.StrengthOf(readback.OwnerFactionId);
            if (!StrengthAllowsAttack(own, defender, attackStrengthRatioPpt))
            {
                return;
            }

            AiSupplyResult supply = _roads.DistanceToNearestCm(
                graph,
                target.NodeId,
                networkNodeIds,
                networkCount,
                faction);
            if (supply.IsBudgetExhausted)
            {
                return;
            }

            if (supply.IsUnreachable || supply.DistanceCm > supplyReachCm)
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
                    supplyReachCm);
                return;
            }

            int battalionId = NearestIdle(faction, graph, target.NodeId, battalions, battalionCount, string.Empty);
            if (battalionId == 0)
            {
                string raiseId = SelectRaiseUnit(
                    seat,
                    _catalog,
                    seatView,
                    battalions,
                    battalionCount,
                    _lineUnitsPerAntiArmour,
                    false,
                    GarrisonShort(faction, towns, townCount));
                TryRaise(tick, faction, seat, raiseId, target.XCm, target.YCm);
                return;
            }

            int moveId = _commands.SubmitMove(faction.FactionId, battalionId, target.XCm, target.YCm);
            Issue(tick, faction, moveId);
            if (_commands.RejectOf(moveId) != AiCommandReject.None)
            {
                return;
            }

            int claimId = _commands.SubmitClaimTown(faction.FactionId, battalionId, target.Id);
            Issue(tick, faction, claimId);
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
            int supplyReachCm)
        {
            if (!CanBuildDepot(seat, _buildings))
            {
                return;
            }

            AiSupplyResult fromNet = _roads.FillFromSources(graph, networkNodeIds, networkCount, faction);
            if (fromNet.IsBudgetExhausted)
            {
                return;
            }

            if (!graph.TryGetNodeIndex(targetNodeId, out int targetIndex))
            {
                return;
            }

            AiSupplyResult fromTarget = _roads.FillFromNode(graph, targetNodeId, faction);
            if (fromTarget.IsBudgetExhausted)
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
                int netDist = _roads.NetworkDistanceAt(i);
                if (netDist > supplyReachCm)
                {
                    continue;
                }

                if (_depots.TryGet(faction.FactionId, nodeId, out AiDepotReadback existing))
                {
                    if (!existing.IsActive(tick))
                    {
                        return;
                    }

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
                bestX = graph.GetNodeXCm(i);
                bestY = graph.GetNodeYCm(i);
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
            if (engineerId == 0)
            {
                string engineerIdToRaise = SelectRaiseUnit(
                    seat,
                    _catalog,
                    _seatView,
                    battalions,
                    battalionCount,
                    _lineUnitsPerAntiArmour,
                    true,
                    false);
                TryRaise(tick, faction, seat, engineerIdToRaise, bestX, bestY);
                return;
            }

            AiBattalionPin engineer = FindPin(battalions, battalionCount, engineerId);
            if (PathNearEnemy(faction, towns, townCount, graph, engineer.RoadNodeId, bestNode))
            {
                int escortId = NearestIdle(faction, graph, bestNode, battalions, battalionCount, AiConstants.RoleLineInfantry);
                if (escortId != 0 && escortId != engineerId)
                {
                    int escortCmd = _commands.SubmitMove(faction.FactionId, escortId, bestX, bestY);
                    Issue(tick, faction, escortCmd);
                    if (_commands.RejectOf(escortCmd) != AiCommandReject.None)
                    {
                        return;
                    }
                }
            }

            if (engineer.RoadNodeId != bestNode)
            {
                int moveId = _commands.SubmitMove(faction.FactionId, engineerId, bestX, bestY);
                Issue(tick, faction, moveId);
                if (_commands.RejectOf(moveId) != AiCommandReject.None)
                {
                    return;
                }
            }

            int buildId = _commands.SubmitBuildDepot(faction.FactionId, engineerId, bestNode);
            Issue(tick, faction, buildId);
            if (_commands.RejectOf(buildId) == AiCommandReject.None
                && _depots.TryGet(faction.FactionId, bestNode, out AiDepotReadback built))
            {
                faction.DepotSiteTick = built.SiteTick;
                faction.DepotActiveTick = built.ActiveTick;
            }
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
            int line = CountRole(battalions, battalionCount, catalog, AiConstants.RoleLineInfantry);
            int at = CountRole(battalions, battalionCount, catalog, AiConstants.RoleAntiTank);
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

            Issue(tick, faction, _commands.SubmitRaiseBattalion(faction.FactionId, unitId, xCm, yCm));
        }

        private bool GarrisonShort(AiFactionState faction, AiAttackTownInfo[] towns, int townCount)
        {
            if (_minGarrisonPerTown < 1)
            {
                return false;
            }

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

                if (readback.StrengthOf(faction.FactionId) < _minGarrisonPerTown)
                {
                    return true;
                }
            }

            return false;
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

        private static bool CanAfford(IAiSeatView seatView, IAiUnitCatalog catalog, string unitId)
        {
            if (string.IsNullOrEmpty(unitId) || !catalog.TryGetManpowerCost(unitId, out int cost))
            {
                return true;
            }

            return seatView.Manpower >= cost;
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

        private bool PathNearEnemy(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int fromNodeId,
            int toNodeId)
        {
            AiSupplyResult path = _roads.DistanceCm(graph, fromNodeId, toNodeId, faction);
            if (!path.IsReached)
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
                if (!viaResult.IsReached)
                {
                    continue;
                }

                AiSupplyResult restResult = _roads.DistanceCm(graph, town.NodeId, toNodeId, faction);
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
            AiCommandReject reject = _commands.RejectOf(commandId);
            if (reject == AiCommandReject.None)
            {
                faction.LastRejectCode = 0;
                faction.RejectStreak = 0;
                faction.RejectBackoffUntilTick = 0;
                return;
            }

            NoteReject(tick, faction, reject);
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

        private int NearestIdle(
            AiFactionState faction,
            Wasteland.Sim.Movement.RoadGraph graph,
            int toNodeId,
            AiBattalionPin[] battalions,
            int battalionCount,
            string role)
        {
            int bestId = 0;
            int bestDist = AiConstants.UnreachableCm;
            for (int i = 0; i < battalionCount; i++)
            {
                AiBattalionPin pin = battalions[i];
                if (pin.Assigned)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(role) && !pin.HasRole(_catalog, role))
                {
                    continue;
                }

                AiSupplyResult dist = _roads.DistanceCm(graph, pin.RoadNodeId, toNodeId, faction);
                if (dist.IsBudgetExhausted)
                {
                    continue;
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
