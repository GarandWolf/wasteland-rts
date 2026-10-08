using Wasteland.Sim.Commands;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Stateless production sink. Forwards each submit to the sim queue
    /// and keeps no command log.
    /// </summary>
    public sealed class ForwardingAiCommandSink : IAiCommandSink
    {
        private readonly ISimCommandSink _inner;
        private readonly AiOwnedState _owned;
        private int _nextId = 1;

        public ForwardingAiCommandSink(ISimCommandSink inner, AiOwnedState owned)
        {
            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
            _owned = owned ?? throw new System.ArgumentNullException(nameof(owned));
        }

        public int Tick
        {
            get { return _inner.Tick; }
        }

        public int SubmitMove(int factionId, int battalionId, int xCm, int yCm)
        {
            _inner.Submit(SimCommand.Move(_inner.Tick, factionId, battalionId, xCm, yCm));
            return NextId();
        }

        public int SubmitClaimTown(int factionId, int battalionId, int townId)
        {
            _inner.Submit(SimCommand.Move(_inner.Tick, factionId, battalionId, townId, 0));
            return NextId();
        }

        public int SubmitRaiseBattalion(int factionId, string unitId, int xCm, int yCm)
        {
            AiFactionState faction = _owned.GetFaction(factionId);
            int battalionId = faction.NextBattalionId;
            faction.NextBattalionId++;
            _inner.Submit(SimCommand.SpawnBattalion(
                _inner.Tick,
                factionId,
                battalionId,
                xCm,
                yCm,
                AiConstants.ClaimMarchSpeedCmPerSecond));
            return battalionId;
        }

        public int SubmitBuildDepot(int factionId, int battalionId, int nodeId)
        {
            _inner.Submit(SimCommand.Build(_inner.Tick, factionId, nodeId, 0, 0));
            return NextId();
        }

        public AiCommandReject RejectOf(int commandId)
        {
            return AiCommandReject.None;
        }

        private int NextId()
        {
            int id = _nextId;
            _nextId++;
            return id;
        }
    }
}
