using System.Collections.Generic;
using Wasteland.Sim.Commands;
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
