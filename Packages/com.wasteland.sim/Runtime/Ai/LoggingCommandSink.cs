using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Forwards to <see cref="SimWorld.Submit"/> while recording issued
    /// commands so a headless save/load can rebuild the world by replay.
    /// </summary>
    public sealed class LoggingCommandSink : ISimCommandSink, ISimWorldView
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

        public SimCommand Submit(SimCommand command)
        {
            _log.Add(command);
            return _inner.Submit(command);
        }

        public void CopyOwnBattalionIds(int factionId, List<int> into)
        {
            _inner.CopyOwnBattalionIds(factionId, into);
        }

        public void CopyVisibleEnemyBattalionIds(int factionId, List<int> into)
        {
            _inner.CopyVisibleEnemyBattalionIds(factionId, into);
        }

        public void CopyAllBattalions(List<SimBattalion> into)
        {
            _inner.CopyAllBattalions(into);
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
