using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Forwards to <see cref="SimWorld.Submit"/> while recording issued
    /// commands so a headless save/load can rebuild the world by replay.
    /// The log is capped at <see cref="AiConstants.CommandLogCapacity"/>.
    /// Entries keep the issued tick (overwritten to the sink tick) and leave
    /// executeTick at 0 so replay goes through <see cref="SimWorld.Submit"/>
    /// again. Further submits still reach the world; they are not recorded.
    /// M1 10k-tick matches stay under the cap, so mid-run replay keeps a
    /// complete command log from tick 0.
    /// </summary>
    public sealed class LoggingCommandSink : ISimCommandSink, ISimWorldView, ICaptureOccupancyView
    {
        private readonly SimWorldCommandSink _inner;
        private readonly List<SimCommand> _log = new List<SimCommand>();

        public LoggingCommandSink(SimWorld world)
        {
            _inner = new SimWorldCommandSink(world);
        }

        public int Tick
        {
            get { return _inner.Tick; }
        }

        public int CommandDelayTicks
        {
            get { return _inner.CommandDelayTicks; }
        }

        public int BattalionCount
        {
            get { return _inner.BattalionCount; }
        }

        public IReadOnlyList<SimCommand> Log
        {
            get { return _log; }
        }

        public int DroppedFromLog { get; private set; }

        public SimCommand Submit(SimCommand command)
        {
            SimCommand stamped = _inner.Submit(command);
            if (_log.Count < AiConstants.CommandLogCapacity)
            {
                _log.Add(stamped.WithSchedule(0, 0));
            }
            else
            {
                DroppedFromLog++;
            }

            return stamped;
        }

        public int AllocateBattalionId()
        {
            return _inner.AllocateBattalionId();
        }

        public SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm)
        {
            return RecordRaise(_inner.SubmitRaiseBattalion(factionId, unitId, rallyXCm, rallyYCm));
        }

        public SimCommand SubmitRaiseBattalion(int factionId, int battalionId, string unitId, int rallyXCm, int rallyYCm)
        {
            return RecordRaise(_inner.SubmitRaiseBattalion(factionId, battalionId, unitId, rallyXCm, rallyYCm));
        }

        public bool HasBattalionRaiseStarted(int battalionId)
        {
            return _inner.HasBattalionRaiseStarted(battalionId);
        }

        public bool TryCopyProductionQueue(int factionId, List<ProductionQueueItem> into)
        {
            return _inner.TryCopyProductionQueue(factionId, into);
        }

        private SimCommand RecordRaise(SimCommand stamped)
        {
            if (_log.Count < AiConstants.CommandLogCapacity)
            {
                _log.Add(stamped.WithSchedule(0, 0));
            }
            else
            {
                DroppedFromLog++;
            }

            return stamped;
        }

        public bool TryGetRejection(int subjectId, out CommandRejectReason code, out int param0, out int param1)
        {
            return _inner.TryGetRejection(subjectId, out code, out param0, out param1);
        }

        public bool TryGetSeat(int factionId, out SeatView view)
        {
            return _inner.TryGetSeat(factionId, out view);
        }

        public bool TryGetBattalionView(int battalionId, out BattalionView view)
        {
            return _inner.TryGetBattalionView(battalionId, out view);
        }

        public void CopyOwnBattalionIds(int factionId, List<int> into)
        {
            _inner.CopyOwnBattalionIds(factionId, into);
        }

        public void CopyVisibleEnemyBattalionIds(int factionId, List<int> into)
        {
            _inner.CopyVisibleEnemyBattalionIds(factionId, into);
        }

        public CaptureZoneOccupancyResult OccupancyOf(int xCm, int yCm, int radiusCm)
        {
            return _inner.OccupancyOf(xCm, yCm, radiusCm);
        }

        public int CountOwnBattalions(int factionId)
        {
            return _inner.CountOwnBattalions(factionId);
        }

        public bool TryGetOwnBattalion(int factionId, int battalionId, out SimBattalion battalion)
        {
            return _inner.TryGetOwnBattalion(factionId, battalionId, out battalion);
        }
    }
}
