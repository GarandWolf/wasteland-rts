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

        public AiAttackTownInfo(int id, int territoryId, int nodeId, int xCm, int yCm, bool isHq)
        {
            Id = id;
            TerritoryId = territoryId;
            NodeId = nodeId;
            XCm = xCm;
            YCm = yCm;
            IsHq = isHq;
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
        private IAiSideReadback _side = new FixtureAiSideReadback();

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
            IAiSideReadback side)
        {
            if (faction == null)
            {
                throw new ArgumentNullException(nameof(faction));
            }

            if (side == null)
            {
                throw new ArgumentNullException(nameof(side));
            }

            _side = side;

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
                side);
        }

        public int PickAttackTarget(
            AiFactionState faction,
            AiAttackTownInfo[] towns,
            int townCount,
            Wasteland.Sim.Movement.RoadGraph graph,
            int fromNodeId,
            int[] networkNodeIds,
            int networkCount,
            int supplyReachCm)
        {
            int nearestId = 0;
            int nearestDist = AiConstants.UnreachableCm;
            int hqId = 0;
            int hqDist = AiConstants.UnreachableCm;
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
                    continue;
                }

                int d = dist.IsReached ? dist.DistanceCm : AiConstants.UnreachableCm;
                if (BetterTarget(d, town.Id, nearestDist, nearestId))
                {
                    nearestDist = d;
                    nearestId = town.Id;
                }

                if (town.IsHq && BetterTarget(d, town.Id, hqDist, hqId))
                {
                    hqDist = d;
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
            return PickAttackTarget(faction, towns, townCount, graph, fromNodeId, none, 0, 0);
        }

        private static bool BetterTarget(int dist, int id, int bestDist, int bestId)
        {
            if (bestId == 0)
            {
                return true;
            }

            int cmp = dist.CompareTo(bestDist);
            if (cmp < 0)
            {
                return true;
            }

            return cmp == 0 && id < bestId;
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
            int idle = CountIdle(battalions, battalionCount, 0);
            if (idle < 1)
            {
                return true;
            }

            int battalionId = NearestIdle(faction, graph, defendNodeId, battalions, battalionCount, 0);
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
            IAiSideReadback side)
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
                supplyReachCm);
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

            int own = CountIdle(battalions, battalionCount, 0) + readback.StrengthOf(faction.FactionId);
            int defender = readback.StrengthOf(readback.OwnerFactionId);
            if (defender < 1)
            {
                defender = 1;
            }

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

            int battalionId = NearestIdle(faction, graph, target.NodeId, battalions, battalionCount, 0);
            if (battalionId == 0)
            {
                int currentTier = side.CurrentTier(faction.FactionId, tick);
                int raiseKind = AiConstants.UnitKindInfantry;
                if (seat.AllowsUnitForTier(AiConstants.UnitKindTierII, currentTier))
                {
                    raiseKind = AiConstants.UnitKindTierII;
                }

                TryRaise(tick, faction, seat, raiseKind, target.XCm, target.YCm);
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
            if (!seat.AllowsBuilding(AiConstants.BuildingTagDepot))
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
                AiSupplyResult fromNet = _roads.DistanceToNearestCm(
                    graph,
                    nodeId,
                    networkNodeIds,
                    networkCount,
                    faction);
                if (fromNet.IsBudgetExhausted)
                {
                    continue;
                }

                if (!fromNet.IsReached || fromNet.DistanceCm > supplyReachCm)
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

                AiSupplyResult toTarget = _roads.DistanceCm(graph, nodeId, targetNodeId, faction);
                if (toTarget.IsBudgetExhausted)
                {
                    continue;
                }

                int remain = toTarget.IsReached ? toTarget.DistanceCm : AiConstants.UnreachableCm;
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
                AiConstants.UnitKindEngineer);
            if (engineerId == 0)
            {
                TryRaise(tick, faction, seat, AiConstants.UnitKindEngineer, bestX, bestY);
                return;
            }

            AiBattalionPin engineer = FindPin(battalions, battalionCount, engineerId);
            if (PathNearEnemy(faction, towns, townCount, graph, engineer.RoadNodeId, bestNode))
            {
                int escortId = NearestIdle(faction, graph, bestNode, battalions, battalionCount, AiConstants.UnitKindInfantry);
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

        private void TryRaise(int tick, AiFactionState faction, AiScenarioSeatView seat, int unitKind, int xCm, int yCm)
        {
            int currentTier = _side.CurrentTier(faction.FactionId, tick);
            if (unitKind == AiConstants.UnitKindTierII && currentTier < 2)
            {
                return;
            }

            if (!seat.AllowsUnitForTier(unitKind, currentTier))
            {
                if (unitKind != AiConstants.UnitKindTierII)
                {
                    NoteReject(tick, faction, AiCommandReject.UnitNotRaisable);
                }

                return;
            }

            int used;
            int cap;
            if (_side.TryGetPopCap(faction.FactionId, out used, out cap) && used >= cap)
            {
                NoteReject(tick, faction, AiCommandReject.PopCapReached);
                return;
            }

            Issue(tick, faction, _commands.SubmitRaiseBattalion(faction.FactionId, unitKind, xCm, yCm));
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

        private static int CountIdle(AiBattalionPin[] battalions, int battalionCount, int unitKind)
        {
            int idle = 0;
            for (int i = 0; i < battalionCount; i++)
            {
                if (battalions[i].Assigned)
                {
                    continue;
                }

                if (unitKind != 0 && battalions[i].UnitKind != unitKind)
                {
                    continue;
                }

                idle++;
            }

            return idle;
        }

        private int NearestIdle(
            AiFactionState faction,
            Wasteland.Sim.Movement.RoadGraph graph,
            int toNodeId,
            AiBattalionPin[] battalions,
            int battalionCount,
            int unitKind)
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

                if (unitKind != 0 && pin.UnitKind != unitKind)
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
