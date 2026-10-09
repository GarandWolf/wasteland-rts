#nullable disable
using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.World
{
    public sealed partial class SimWorld
    {
        private readonly List<DepotState> _depots = new List<DepotState>();
        private long[] _roadDist = Array.Empty<long>();
        private MinHeap _roadHeap;
        private int _nextDepotId = 1;

        public void CopyDepots(List<DepotView> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            for (int i = 0; i < _depots.Count; i++)
            {
                into.Add(ToDepotView(_depots[i]));
            }
        }

        /// <summary>
        /// Placement check used by <see cref="CommandKind.BuildDepot"/> at execute time.
        /// Reserved, started, and active sites all count for spacing, one-per-node, and the cap.
        /// </summary>
        public CommandRejectReason PreviewDepotPlacement(int factionId, int nodeId, out int param0, out int param1)
        {
            return CheckDepotPlacement(factionId, nodeId, out param0, out param1);
        }

        private void ApplyBuildDepot(SimCommand command)
        {
            int seatIndex = FindSeat(command.IssuerId);
            if (seatIndex < 0)
            {
                Reject(command, CommandRejectReason.UnknownSeat, 0, 0);
                return;
            }

            if (!_battalionIndexById.TryGetValue(command.BattalionId, out int battalionIndex))
            {
                Reject(command, CommandRejectReason.UnknownBattalion, 0, 0);
                return;
            }

            SimBattalion battalion = _battalions[battalionIndex];
            if (battalion.OwnerId != command.IssuerId)
            {
                Reject(command, CommandRejectReason.NotOwner, 0, 0);
                return;
            }

            if (battalion.StrengthPpt <= 0)
            {
                Reject(command, CommandRejectReason.UnknownBattalion, 0, 0);
                return;
            }

            if (_catalog == null || battalion.UnitIndex < 0 || !_catalog.UnitCanBuildAt(battalion.UnitIndex))
            {
                Reject(command, CommandRejectReason.NotBuilder, 0, 0);
                return;
            }

            SeatState seat = _seats[seatIndex];
            if (!seat.SupplyDepotAllowed)
            {
                Reject(command, CommandRejectReason.BuildingNotBuildable, 0, 0);
                return;
            }

            DepotState replaced = default(DepotState);
            bool hadReplaced = TryDetachReserved(command.BattalionId, out replaced);
            int param0;
            int param1;
            CommandRejectReason placement = CheckDepotPlacement(command.IssuerId, command.Arg0, out param0, out param1);
            if (placement != CommandRejectReason.None)
            {
                if (hadReplaced)
                {
                    InsertDepot(replaced);
                }

                Reject(command, placement, param0, param1);
                return;
            }

            CaptureBalance rules = _catalog.Capture;
            if (hadReplaced)
            {
                seat = Credit(seat, replaced);
            }

            if (seat.Manpower < rules.DepotManpower)
            {
                if (hadReplaced)
                {
                    seat = Debit(seat, replaced);
                    _seats[seatIndex] = seat;
                    InsertDepot(replaced);
                }

                Reject(command, CommandRejectReason.InsufficientManpower, rules.DepotManpower, seat.Manpower);
                return;
            }

            if (seat.Materials < rules.DepotMateriel)
            {
                if (hadReplaced)
                {
                    seat = Debit(seat, replaced);
                    _seats[seatIndex] = seat;
                    InsertDepot(replaced);
                }

                Reject(command, CommandRejectReason.InsufficientMaterials, rules.DepotMateriel, seat.Materials);
                return;
            }

            if (seat.FuelEnabled && seat.Fuel < rules.DepotFuel)
            {
                if (hadReplaced)
                {
                    seat = Debit(seat, replaced);
                    _seats[seatIndex] = seat;
                    InsertDepot(replaced);
                }

                Reject(command, CommandRejectReason.InsufficientFuel, rules.DepotFuel, seat.Fuel);
                return;
            }

            seat.Manpower -= rules.DepotManpower;
            seat.Materials -= rules.DepotMateriel;
            int fuelCharged = 0;
            if (seat.FuelEnabled)
            {
                seat.Fuel -= rules.DepotFuel;
                fuelCharged = rules.DepotFuel;
            }

            _seats[seatIndex] = seat;

            int nodeIndex = _roads.TryGetNodeIndex(command.Arg0, out int found) ? found : -1;
            ulong territoryKey = 0UL;
            int nodeX = 0;
            int nodeY = 0;
            if (nodeIndex >= 0)
            {
                nodeX = _roads.GetNodeXCm(nodeIndex);
                nodeY = _roads.GetNodeYCm(nodeIndex);
                territoryKey = TerritoryKeyOf(_roads.GetNodeTerritoryId(nodeIndex));
            }

            DepotState depot = new DepotState();
            depot.DepotId = _nextDepotId;
            _nextDepotId++;
            depot.OwnerFactionId = command.IssuerId;
            depot.NodeId = command.Arg0;
            depot.NodeXCm = nodeX;
            depot.NodeYCm = nodeY;
            depot.TerritoryKey = territoryKey;
            depot.BuilderBattalionId = command.BattalionId;
            depot.SiteTick = Tick;
            depot.StartTick = -1;
            depot.ActiveTick = -1;
            depot.State = DepotSiteState.Reserved;
            depot.MaterielCharged = rules.DepotMateriel;
            depot.FuelCharged = fuelCharged;
            depot.BuildTicks = rules.DepotBuildTicks;
            InsertDepot(depot);
            Emit(
                SimEventKind.DepotStarted,
                depot.DepotId,
                depot.OwnerFactionId,
                depot.NodeId,
                -1,
                depot.TerritoryKey);
            if (hadReplaced)
            {
                Emit(
                    SimEventKind.OrderRefunded,
                    replaced.OwnerFactionId,
                    (int)CommandKind.BuildDepot,
                    replaced.NodeId,
                    replaced.MaterielCharged,
                    replaced.TerritoryKey);
            }

            bool moved = false;
            if (_movement != null && _roads != null)
            {
                moved = ApplyRoadMove(ref battalion, nodeX, nodeY, command);
            }
            else
            {
                ClearPath(ref battalion);
                battalion.TargetXCm = nodeX;
                battalion.TargetYCm = nodeY;
                battalion.HasTarget = true;
                moved = true;
            }

            _battalions[battalionIndex] = battalion;
            if (!moved)
            {
                DropDepot(depot.DepotId, CommandKind.BuildDepot, true);
            }
        }

        private void DropReservedDepotForReorder(SimCommand command)
        {
            if (command.Kind != CommandKind.Move)
            {
                return;
            }

            if (!TryDetachReserved(command.BattalionId, out DepotState depot))
            {
                return;
            }

            int seatIndex = FindSeat(depot.OwnerFactionId);
            if (seatIndex >= 0)
            {
                _seats[seatIndex] = Credit(_seats[seatIndex], depot);
            }

            Emit(
                SimEventKind.OrderRefunded,
                depot.OwnerFactionId,
                (int)CommandKind.BuildDepot,
                depot.NodeId,
                depot.MaterielCharged,
                depot.TerritoryKey);
        }

        private void StartDepotBuilds()
        {
            if (_depots.Count == 0 || _catalog == null)
            {
                return;
            }

            int radius = _catalog.Capture.NodeControlRadiusCm;
            long radiusSq = (long)radius * radius;
            for (int b = 0; b < _battalions.Count; b++)
            {
                SimBattalion battalion = _battalions[b];
                if (battalion.StrengthPpt <= 0)
                {
                    continue;
                }

                for (int d = 0; d < _depots.Count; d++)
                {
                    DepotState depot = _depots[d];
                    if (depot.State != DepotSiteState.Reserved || depot.BuilderBattalionId != battalion.Id)
                    {
                        continue;
                    }

                    if (!InsideSq(battalion.PositionXCm, battalion.PositionYCm, depot.NodeXCm, depot.NodeYCm, radiusSq))
                    {
                        continue;
                    }

                    depot.State = DepotSiteState.UnderConstruction;
                    depot.StartTick = Tick;
                    depot.ActiveTick = checked(Tick + depot.BuildTicks);
                    _depots[d] = depot;
                }
            }
        }

        private void DestroyFlippedDepots()
        {
            if (_flippedKeys.Count == 0)
            {
                return;
            }

            int index = 0;
            while (index < _depots.Count)
            {
                DepotState depot = _depots[index];
                if (!KeyFlipped(depot.TerritoryKey))
                {
                    index++;
                    continue;
                }

                int under = depot.State == DepotSiteState.Active ? 0 : 1;
                Emit(
                    SimEventKind.DepotDestroyed,
                    depot.DepotId,
                    depot.OwnerFactionId,
                    depot.NodeId,
                    under,
                    depot.TerritoryKey);
                _depots.RemoveAt(index);
            }
        }

        private void AdvanceDepotConstruction()
        {
            int index = 0;
            while (index < _depots.Count)
            {
                DepotState depot = _depots[index];
                if (depot.State == DepotSiteState.Reserved && !BuilderAlive(depot.BuilderBattalionId))
                {
                    DropDepotAt(index, CommandKind.BuildDepot, true);
                    continue;
                }

                if (depot.State == DepotSiteState.UnderConstruction && Tick >= depot.ActiveTick && depot.ActiveTick >= 0)
                {
                    depot.State = DepotSiteState.Active;
                    _depots[index] = depot;
                    Emit(
                        SimEventKind.DepotActivated,
                        depot.DepotId,
                        depot.OwnerFactionId,
                        depot.NodeId,
                        0,
                        depot.TerritoryKey);
                }

                index++;
            }
        }

        private CommandRejectReason CheckDepotPlacement(int factionId, int nodeId, out int param0, out int param1)
        {
            param0 = 0;
            param1 = 0;
            int seatIndex = FindSeat(factionId);
            if (seatIndex < 0)
            {
                return CommandRejectReason.UnknownSeat;
            }

            if (_roads == null || !_roads.TryGetNodeIndex(nodeId, out int nodeIndex))
            {
                param0 = nodeId;
                return CommandRejectReason.UnknownNode;
            }

            if (_catalog == null)
            {
                param0 = nodeId;
                return CommandRejectReason.UnknownNode;
            }

            CaptureBalance rules = _catalog.Capture;
            int territoryId = _roads.GetNodeTerritoryId(nodeIndex);
            int territoryIndex = IndexOfTerritoryId(_territories, territoryId);
            if (territoryIndex < 0 || _territories[territoryIndex].OwnerFactionId != factionId)
            {
                param0 = nodeId;
                param1 = territoryId;
                return CommandRejectReason.TerritoryNotOwned;
            }

            int onNode = 0;
            for (int i = 0; i < _depots.Count; i++)
            {
                if (_depots[i].NodeId == nodeId)
                {
                    onNode++;
                }
            }

            if (onNode >= rules.MaxDepotsPerNode)
            {
                param0 = nodeId;
                return CommandRejectReason.NodeHasDepot;
            }

            FillRoadDistance(nodeId);
            if (!ConnectedToOwnSupply(seatIndex, factionId, rules))
            {
                param0 = nodeId;
                return CommandRejectReason.NotConnectedToSupply;
            }

            for (int i = 0; i < _depots.Count; i++)
            {
                long distance = DistanceToNode(_depots[i].NodeId);
                if (distance < rules.DepotMinSpacingCm)
                {
                    param0 = nodeId;
                    param1 = _depots[i].NodeId;
                    return CommandRejectReason.DepotTooClose;
                }
            }

            int owned = 0;
            for (int i = 0; i < _depots.Count; i++)
            {
                if (_depots[i].OwnerFactionId == factionId)
                {
                    owned++;
                }
            }

            if (owned >= rules.MaxDepotsPerFaction)
            {
                param0 = rules.MaxDepotsPerFaction;
                param1 = owned;
                return CommandRejectReason.DepotCapReached;
            }

            return CommandRejectReason.None;
        }

        private bool ConnectedToOwnSupply(int seatIndex, int factionId, CaptureBalance rules)
        {
            SeatState seat = _seats[seatIndex];
            int home = IndexOfTown(seat.HomeTownId);
            if (home >= 0)
            {
                long hq = DistanceToNode(_towns[home].RoadNodeId);
                if (hq <= rules.HqRangeCm)
                {
                    return true;
                }
            }

            for (int i = 0; i < _depots.Count; i++)
            {
                DepotState depot = _depots[i];
                if (depot.OwnerFactionId != factionId || depot.State != DepotSiteState.Active)
                {
                    continue;
                }

                long distance = DistanceToNode(depot.NodeId);
                if (distance <= rules.DepotRangeCm)
                {
                    return true;
                }
            }

            return false;
        }

        private void FillRoadDistance(int sourceNodeId)
        {
            int count = _roads.NodeCount;
            if (_roadDist.Length < count)
            {
                _roadDist = new long[count];
            }

            for (int i = 0; i < count; i++)
            {
                _roadDist[i] = long.MaxValue;
            }

            if (!_roads.TryGetNodeIndex(sourceNodeId, out int source))
            {
                return;
            }

            if (_roadHeap == null)
            {
                _roadHeap = new MinHeap(count);
            }

            _roadHeap.Clear();
            _roadDist[source] = 0L;
            _roadHeap.Push(0L, _roads.GetNodeId(source), source);
            while (_roadHeap.Count > 0)
            {
                int index;
                long distance;
                if (!_roadHeap.TryPop(out index, out distance))
                {
                    break;
                }

                if (index < 0 || index >= count || distance != _roadDist[index])
                {
                    continue;
                }

                int start = _roads.AdjOffset[index];
                int end = _roads.AdjOffset[index + 1];
                for (int edge = start; edge < end; edge++)
                {
                    int neighbor = _roads.AdjNeighborIndex[edge];
                    int edgeIndex = _roads.AdjEdgeIndex[edge];
                    int length = _roads.EdgeLengthCm[edgeIndex];
                    if (length < 0 || _roadDist[index] > long.MaxValue - length)
                    {
                        continue;
                    }

                    long next = _roadDist[index] + length;
                    if (next < _roadDist[neighbor])
                    {
                        _roadDist[neighbor] = next;
                        _roadHeap.Push(next, _roads.GetNodeId(neighbor), neighbor);
                    }
                }
            }
        }

        private long DistanceToNode(int nodeId)
        {
            if (_roads == null || !_roads.TryGetNodeIndex(nodeId, out int index))
            {
                return long.MaxValue;
            }

            if (index < 0 || index >= _roadDist.Length)
            {
                return long.MaxValue;
            }

            return _roadDist[index];
        }

        private ulong TerritoryKeyOf(int territoryId)
        {
            int index = IndexOfTerritoryId(_territories, territoryId);
            if (index < 0)
            {
                return 0UL;
            }

            return _territories[index].Key;
        }

        private bool KeyFlipped(ulong key)
        {
            for (int i = 0; i < _flippedKeys.Count; i++)
            {
                if (_flippedKeys[i] == key)
                {
                    return true;
                }
            }

            return false;
        }

        private bool BuilderAlive(int battalionId)
        {
            if (!_battalionIndexById.TryGetValue(battalionId, out int index))
            {
                return false;
            }

            return _battalions[index].StrengthPpt > 0;
        }

        private bool TryDetachReserved(int builderId, out DepotState depot)
        {
            for (int i = 0; i < _depots.Count; i++)
            {
                if (_depots[i].BuilderBattalionId == builderId && _depots[i].State == DepotSiteState.Reserved)
                {
                    depot = _depots[i];
                    _depots.RemoveAt(i);
                    return true;
                }
            }

            depot = default(DepotState);
            return false;
        }

        private void DropDepot(int depotId, CommandKind kind, bool refund)
        {
            for (int i = 0; i < _depots.Count; i++)
            {
                if (_depots[i].DepotId == depotId)
                {
                    DropDepotAt(i, kind, refund);
                    return;
                }
            }
        }

        private void DropDepotAt(int index, CommandKind kind, bool refund)
        {
            DepotState depot = _depots[index];
            _depots.RemoveAt(index);
            if (!refund)
            {
                return;
            }

            int seatIndex = FindSeat(depot.OwnerFactionId);
            if (seatIndex >= 0)
            {
                _seats[seatIndex] = Credit(_seats[seatIndex], depot);
            }

            Emit(
                SimEventKind.OrderRefunded,
                depot.OwnerFactionId,
                (int)kind,
                depot.NodeId,
                depot.MaterielCharged,
                depot.TerritoryKey);
        }

        private static SeatState Credit(SeatState seat, DepotState depot)
        {
            seat.Materials = checked(seat.Materials + depot.MaterielCharged);
            seat.Fuel = checked(seat.Fuel + depot.FuelCharged);
            return seat;
        }

        private static SeatState Debit(SeatState seat, DepotState depot)
        {
            seat.Materials -= depot.MaterielCharged;
            seat.Fuel -= depot.FuelCharged;
            return seat;
        }

        private void InsertDepot(DepotState depot)
        {
            int index = _depots.Count;
            for (int i = 0; i < _depots.Count; i++)
            {
                if (_depots[i].DepotId > depot.DepotId)
                {
                    index = i;
                    break;
                }
            }

            _depots.Insert(index, depot);
        }

        private void HashDepots(XxHash64Builder hash)
        {
            hash.AddInt(_nextDepotId);
            hash.AddInt(_depots.Count);
            for (int i = 0; i < _depots.Count; i++)
            {
                DepotState depot = _depots[i];
                hash.AddInt(depot.DepotId);
                hash.AddInt(depot.OwnerFactionId);
                hash.AddInt(depot.NodeId);
                hash.AddULong(depot.TerritoryKey);
                hash.AddInt(depot.BuilderBattalionId);
                hash.AddInt(depot.SiteTick);
                hash.AddInt(depot.StartTick);
                hash.AddInt(depot.ActiveTick);
                hash.AddByte((byte)depot.State);
                hash.AddInt(depot.MaterielCharged);
                hash.AddInt(depot.FuelCharged);
            }
        }

        private static DepotView ToDepotView(DepotState depot)
        {
            return new DepotView(
                depot.DepotId,
                depot.OwnerFactionId,
                depot.NodeId,
                depot.TerritoryKey,
                depot.BuilderBattalionId,
                depot.SiteTick,
                depot.StartTick,
                depot.ActiveTick,
                depot.State);
        }

        private struct DepotState
        {
            public int DepotId;
            public int OwnerFactionId;
            public int NodeId;
            public int NodeXCm;
            public int NodeYCm;
            public ulong TerritoryKey;
            public int BuilderBattalionId;
            public int SiteTick;
            public int StartTick;
            public int ActiveTick;
            public DepotSiteState State;
            public int MaterielCharged;
            public int FuelCharged;
            public int BuildTicks;
        }
    }
}
