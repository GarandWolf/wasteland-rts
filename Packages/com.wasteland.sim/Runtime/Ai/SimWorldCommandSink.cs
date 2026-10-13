using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Adapter: spawn, move, claim, and depot commands go through <see cref="SimWorld.Submit"/>.
    /// </summary>
    public sealed class SimWorldCommandSink : ISimCommandSink, ISimWorldView, ICaptureOccupancyView
    {
        private readonly SimWorld _world;
        private readonly Dictionary<ulong, RejectHit> _rejectsByKey = new Dictionary<ulong, RejectHit>();
        private RoadGraph? _roads;
        private int _lastSequence;
        private int _indexedEventCount;

        private struct RejectHit
        {
            public CommandRejectReason Code;
            public int Param0;
            public int Param1;
        }

        public SimWorldCommandSink(SimWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public void BindRoads(RoadGraph roads)
        {
            _roads = roads;
        }

        public int LastSequence
        {
            get { return _lastSequence; }
        }

        public int Tick
        {
            get { return _world.Tick; }
        }

        public MatchResultView MatchResult
        {
            get { return _world.MatchResult; }
        }

        public int CommandDelayTicks
        {
            get { return _world.CommandDelayTicks; }
        }

        public int BattalionCount
        {
            get { return _world.BattalionCount; }
        }

        public SimCommand Submit(SimCommand command)
        {
            if (command.IssuedTick != Tick)
            {
                command = command.WithIssuedTick(Tick);
            }

            SimCommand stamped = _world.Submit(command);
            _lastSequence = stamped.Sequence;
            return stamped;
        }

        public int AllocateBattalionId()
        {
            return _world.AllocateBattalionId();
        }

        public SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm)
        {
            return _world.SubmitRaiseBattalion(factionId, unitId, rallyXCm, rallyYCm);
        }

        public SimCommand SubmitRaiseBattalion(int factionId, int battalionId, string unitId, int rallyXCm, int rallyYCm)
        {
            return _world.SubmitRaiseBattalion(factionId, battalionId, unitId, rallyXCm, rallyYCm);
        }

        public bool TryGetRejection(int subjectId, out CommandRejectReason code, out int param0, out int param1)
        {
            return _world.TryGetRejection(subjectId, out code, out param0, out param1);
        }

        /// <summary>
        /// Events examined by the most recent <see cref="TryGetRejectionByKey"/> call.
        /// Zero when the key index is already current.
        /// </summary>
        internal int LastRejectionScanCount { get; private set; }

        public bool TryGetRejectionByKey(ulong key, out CommandRejectReason code, out int param0, out int param1)
        {
            IndexNewRejections();
            RejectHit hit;
            if (_rejectsByKey.TryGetValue(key, out hit))
            {
                code = hit.Code;
                param0 = hit.Param0;
                param1 = hit.Param1;
                return true;
            }

            code = CommandRejectReason.None;
            param0 = 0;
            param1 = 0;
            return false;
        }

        /// <summary>
        /// Walk only events appended since the previous call and store the latest
        /// <see cref="SimEventKind.CommandRejected"/> per key. Later events win,
        /// matching a reverse scan of the full list.
        /// </summary>
        private void IndexNewRejections()
        {
            IReadOnlyList<SimEvent> events = _world.Events;
            if (_indexedEventCount > events.Count)
            {
                _indexedEventCount = 0;
                _rejectsByKey.Clear();
            }

            int start = _indexedEventCount;
            for (int i = start; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind != SimEventKind.CommandRejected)
                {
                    continue;
                }

                RejectHit hit;
                hit.Code = ev.RejectReason;
                hit.Param0 = ev.Param0;
                hit.Param1 = ev.Param1;
                _rejectsByKey[ev.Key] = hit;
            }

            LastRejectionScanCount = events.Count - start;
            _indexedEventCount = events.Count;
        }

        public bool HasBattalionRaiseStarted(int battalionId)
        {
            IReadOnlyList<SimEvent> events = _world.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == SimEventKind.BattalionRaiseStarted && events[i].SubjectId == battalionId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryCopyProductionQueue(int factionId, List<ProductionQueueItem> into)
        {
            return _world.TryCopyProductionQueue(factionId, into);
        }

        public int TownCount
        {
            get { return _world.TownCount; }
        }

        public bool TryGetTown(int townId, out TownView view)
        {
            return _world.TryGetTown(townId, out view);
        }

        public void CopyTowns(List<TownView> into)
        {
            _world.CopyTowns(into);
        }

        public bool TryGetTerritory(ulong key, out TerritoryView view)
        {
            return _world.TryGetTerritory(key, out view);
        }

        public void CopyDepots(List<DepotView> into)
        {
            _world.CopyDepots(into);
        }

        public void CopyEventsOfTick(int tick, List<SimEvent> into)
        {
            _world.CopyEventsOfTick(tick, into);
        }

        public void CopyEventsInRange(int fromTick, int toTick, List<SimEvent> into)
        {
            _world.CopyEventsInRange(fromTick, toTick, into);
        }

        public int GetNodeTerritoryId(int nodeId)
        {
            if (_roads == null || !_roads.TryGetNodeIndex(nodeId, out int index))
            {
                return 0;
            }

            return _roads.GetNodeTerritoryId(index);
        }

        public int GetNearestNodeId(int xCm, int yCm)
        {
            if (_roads == null)
            {
                return 0;
            }

            return _roads.NearestNodeId(xCm, yCm);
        }

        public CommandRejectReason PreviewDepotPlacement(int factionId, int nodeId, out int param0, out int param1)
        {
            return _world.PreviewDepotPlacement(factionId, nodeId, out param0, out param1);
        }

        public bool TryGetSeat(int factionId, out SeatView view)
        {
            return _world.TryGetSeat(factionId, out view);
        }

        public int FuelOf(int factionId)
        {
            return _world.FuelOf(factionId);
        }

        public bool TryGetBattalionView(int battalionId, out BattalionView view)
        {
            return _world.TryGetBattalionView(battalionId, out view);
        }

        public void CopyOwnBattalionIds(int factionId, List<int> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                if (battalions[i].OwnerId == factionId)
                {
                    into.Add(battalions[i].Id);
                }
            }
        }

        public void CopyVisibleEnemyBattalionIds(int factionId, List<int> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.Hp <= 0 || battalion.OwnerId == factionId)
                {
                    continue;
                }

                into.Add(battalion.Id);
            }
        }

        public void CopyCombatants(int factionId, List<AiCombatantView> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.Hp <= 0 || battalion.OwnerId == factionId)
                {
                    continue;
                }

                int nodeId = GetNearestNodeId(battalion.PositionXCm, battalion.PositionYCm);
                into.Add(new AiCombatantView(
                    battalion.Id,
                    battalion.OwnerId,
                    battalion.PositionXCm,
                    battalion.PositionYCm,
                    nodeId,
                    battalion.Hp,
                    battalion.MaxHp,
                    battalion.AttackTargetId));
            }
        }

        public CaptureZoneOccupancyResult OccupancyOf(int xCm, int yCm, int radiusCm)
        {
            if (radiusCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            long radiusSq = (long)radiusCm * radiusCm;
            int occupant = 0;
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                long dx = (long)battalion.PositionXCm - xCm;
                long dy = (long)battalion.PositionYCm - yCm;
                if ((dx * dx) + (dy * dy) > radiusSq)
                {
                    continue;
                }

                if (occupant == 0)
                {
                    occupant = battalion.OwnerId;
                }
                else if (occupant != battalion.OwnerId)
                {
                    return new CaptureZoneOccupancyResult(CaptureOccupancyKind.Contested, 0);
                }
            }

            if (occupant == 0)
            {
                return new CaptureZoneOccupancyResult(CaptureOccupancyKind.Empty, 0);
            }

            return new CaptureZoneOccupancyResult(CaptureOccupancyKind.OneFaction, occupant);
        }

        public int CountOwnBattalions(int factionId)
        {
            int count = 0;
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                if (battalions[i].OwnerId == factionId)
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetOwnBattalion(int factionId, int battalionId, out SimBattalion battalion)
        {
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                if (battalions[i].Id == battalionId)
                {
                    if (battalions[i].OwnerId != factionId)
                    {
                        battalion = default;
                        return false;
                    }

                    battalion = battalions[i];
                    return true;
                }
            }

            battalion = default;
            return false;
        }
    }
}
