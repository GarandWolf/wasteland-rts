using System.Collections.Generic;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Test-only recording sink. Production uses
    /// <see cref="ForwardingAiCommandSink"/> and keeps no command log.
    /// </summary>
    public sealed class RecordingAiCommandSink : IAiCommandSink, IAiDepotQuery
    {
        private readonly List<AiIssuedCommand> _log = new List<AiIssuedCommand>();
        private readonly Dictionary<int, AiCommandReject> _rejects = new Dictionary<int, AiCommandReject>();
        private readonly Dictionary<long, AiDepotReadback> _depots = new Dictionary<long, AiDepotReadback>();
        private int _nextId = 1;
        private int _lastSequence;
        private AiCommandReject _nextReject = AiCommandReject.None;
        private int _tick;
        private int _depotActiveDelayTicks = 1;
        private int _commandDelayTicks;

        public int Tick
        {
            get { return _tick; }
            set { _tick = value; }
        }

        public int CommandDelayTicks
        {
            get { return _commandDelayTicks; }
            set { _commandDelayTicks = value < 0 ? 0 : value; }
        }

        public IReadOnlyList<AiIssuedCommand> IssuedCommands
        {
            get { return _log; }
        }

        public int LastSequence
        {
            get { return _lastSequence; }
        }

        public int DepotActiveDelayTicks
        {
            get { return _depotActiveDelayTicks; }
            set { _depotActiveDelayTicks = value < 0 ? 0 : value; }
        }

        public AiCommandReject NextReject
        {
            get { return _nextReject; }
            set { _nextReject = value; }
        }

        public IReadOnlyList<AiIssuedCommand> Log
        {
            get { return _log; }
        }

        public int SubmitMove(int factionId, int battalionId, int xCm, int yCm)
        {
            return Record(factionId, AiIssuedKind.Move, battalionId, xCm, yCm, 0, string.Empty);
        }

        public int SubmitClaimTown(int factionId, int battalionId, int townId)
        {
            return Record(factionId, AiIssuedKind.ClaimTown, battalionId, townId, 0, 0, string.Empty);
        }

        public int SubmitRaiseBattalion(int factionId, string unitId, int xCm, int yCm)
        {
            return Record(factionId, AiIssuedKind.RaiseBattalion, 0, xCm, yCm, 0, unitId);
        }

        public int SubmitBuildDepot(int factionId, int battalionId, int nodeId)
        {
            int id = Record(factionId, AiIssuedKind.BuildDepot, battalionId, nodeId, 0, 0, string.Empty);
            if (RejectOf(id) == AiCommandReject.None)
            {
                RecordSite(factionId, nodeId, _tick, _tick + _depotActiveDelayTicks);
            }

            return id;
        }

        public AiCommandReject RejectOf(int commandId)
        {
            AiCommandReject reject;
            if (_rejects.TryGetValue(commandId, out reject))
            {
                return reject;
            }

            return AiCommandReject.None;
        }

        public bool RaiseHasStarted(int commandId)
        {
            return false;
        }

        public bool TryGet(int factionId, int nodeId, out AiDepotReadback readback)
        {
            return _depots.TryGetValue(DepotKey(factionId, nodeId), out readback);
        }

        public void RecordSite(int factionId, int nodeId, int siteTick, int activeTick)
        {
            _depots[DepotKey(factionId, nodeId)] = new AiDepotReadback(siteTick, activeTick);
        }

        public int CountSites(int factionId)
        {
            int count = 0;
            foreach (KeyValuePair<long, AiDepotReadback> pair in _depots)
            {
                if ((int)(pair.Key >> 32) == factionId)
                {
                    count++;
                }
            }

            return count;
        }

        private int Record(
            int factionId,
            AiIssuedKind kind,
            int arg0,
            int arg1,
            int arg2,
            int arg3,
            string unitId)
        {
            int id = _nextId;
            _nextId++;
            _lastSequence = id;
            _log.Add(new AiIssuedCommand(id, _tick, factionId, kind, arg0, arg1, arg2, arg3, unitId));
            if (_nextReject != AiCommandReject.None)
            {
                _rejects[id] = _nextReject;
                _nextReject = AiCommandReject.None;
            }

            return id;
        }

        private static long DepotKey(int factionId, int nodeId)
        {
            return ((long)factionId << 32) ^ (uint)nodeId;
        }
    }
}
