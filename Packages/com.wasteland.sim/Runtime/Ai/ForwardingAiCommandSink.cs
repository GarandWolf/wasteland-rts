using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Stateless production sink. Forwards each submit to the sim queue
    /// and keeps no saved command log. Issued commands in this session
    /// are readable for pending-cost reservation.
    /// </summary>
    public sealed class ForwardingAiCommandSink : IAiCommandSink
    {
        private readonly ISimCommandSink _inner;
        private readonly AiOwnedState _owned;
        private readonly List<AiIssuedCommand> _issued = new List<AiIssuedCommand>();
        private int _nextId = 1;
        private int _lastSequence;

        public ForwardingAiCommandSink(ISimCommandSink inner, AiOwnedState owned)
        {
            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
            _owned = owned ?? throw new System.ArgumentNullException(nameof(owned));
        }

        public int Tick
        {
            get { return _inner.Tick; }
        }

        public int CommandDelayTicks
        {
            get { return _inner.CommandDelayTicks; }
        }

        public IReadOnlyList<AiIssuedCommand> IssuedCommands
        {
            get { return _issued; }
        }

        public int SubmitMove(int factionId, int battalionId, int xCm, int yCm)
        {
            SimCommand stamped = _inner.Submit(SimCommand.Move(_inner.Tick, factionId, battalionId, xCm, yCm));
            _lastSequence = stamped.Sequence;
            if (_inner is ISimWorldView view)
            {
                int nodeId = view.GetNearestNodeId(xCm, yCm);
                if (nodeId != 0)
                {
                    _owned.NoteOrderSuccess(factionId, battalionId, CommandKind.Move, nodeId);
                }
            }

            return Record(factionId, AiIssuedKind.Move, battalionId, xCm, yCm, 0, string.Empty);
        }

        /// <summary>Sequence stamped on the last command this sink forwarded into the world.</summary>
        public int LastSequence
        {
            get { return _lastSequence; }
        }

        public int SubmitClaimTown(int factionId, int battalionId, int townId)
        {
            SimCommand stamped = _inner.Submit(SimCommand.ClaimTown(_inner.Tick, factionId, battalionId, townId));
            _lastSequence = stamped.Sequence;
            return Record(factionId, AiIssuedKind.ClaimTown, battalionId, townId, 0, 0, string.Empty);
        }

        public int SubmitRaiseBattalion(int factionId, string unitId, int xCm, int yCm)
        {
            _owned.GetFaction(factionId);
            SimCommand stamped = _inner.SubmitRaiseBattalion(factionId, unitId, xCm, yCm);
            _lastSequence = stamped.Sequence;
            return Record(factionId, AiIssuedKind.RaiseBattalion, stamped.BattalionId, xCm, yCm, 0, unitId);
        }

        public int SubmitBuildDepot(int factionId, int battalionId, int nodeId)
        {
            SimCommand stamped = _inner.Submit(SimCommand.BuildDepot(_inner.Tick, factionId, battalionId, nodeId));
            _lastSequence = stamped.Sequence;
            return Record(factionId, AiIssuedKind.BuildDepot, battalionId, nodeId, 0, 0, string.Empty);
        }

        public AiCommandReject RejectOf(int commandId)
        {
            CommandRejectReason code;
            int param0;
            int param1;
            if (_inner.TryGetRejection(commandId, out code, out param0, out param1))
            {
                return MapReject(code);
            }

            return AiCommandReject.None;
        }

        internal static AiCommandReject MapReject(CommandRejectReason code)
        {
            if (code == CommandRejectReason.TierLocked)
            {
                return AiCommandReject.TierLocked;
            }

            if (code == CommandRejectReason.UnitNotRaisable)
            {
                return AiCommandReject.UnitNotRaisable;
            }

            if (code == CommandRejectReason.PopCapReached)
            {
                return AiCommandReject.PopCapReached;
            }

            if (code == CommandRejectReason.InsufficientManpower
                || code == CommandRejectReason.InsufficientMaterials
                || code == CommandRejectReason.InsufficientFuel)
            {
                return AiCommandReject.InsufficientManpower;
            }

            if (code == CommandRejectReason.None)
            {
                return AiCommandReject.None;
            }

            return AiCommandReject.Rejected;
        }

        public bool RaiseHasStarted(int commandId)
        {
            return _inner.HasBattalionRaiseStarted(commandId);
        }

        internal void RestoreIssuedRaises(
            System.Collections.Generic.IReadOnlyList<SimCommand> commands,
            Wasteland.Sim.Scenario.BalanceCatalog? catalog)
        {
            _issued.Clear();
            if (commands == null)
            {
                return;
            }

            for (int i = 0; i < commands.Count; i++)
            {
                SimCommand command = commands[i];
                if (command.Kind != CommandKind.RaiseBattalion)
                {
                    continue;
                }

                string unitId = string.Empty;
                if (catalog != null && command.Arg0 >= 0 && command.Arg0 < catalog.UnitCount)
                {
                    unitId = catalog.UnitIdAt(command.Arg0);
                }

                _issued.Add(new AiIssuedCommand(
                    command.BattalionId,
                    command.IssuedTick,
                    command.IssuerId,
                    AiIssuedKind.RaiseBattalion,
                    command.BattalionId,
                    command.Arg1,
                    command.Arg2,
                    0,
                    unitId));
            }
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
            int id = arg0;
            if (kind != AiIssuedKind.RaiseBattalion)
            {
                id = _nextId;
                _nextId++;
            }

            _issued.Add(new AiIssuedCommand(id, _inner.Tick, factionId, kind, arg0, arg1, arg2, arg3, unitId));
            if (kind == AiIssuedKind.ClaimTown || kind == AiIssuedKind.BuildDepot)
            {
                AiPendingIntent intent = default;
                intent.Sequence = _lastSequence;
                intent.Kind = kind;
                intent.BattalionId = arg0;
                intent.TargetId = arg1;
                intent.IssuedTick = _inner.Tick;
                _owned.EnqueueIntent(factionId, intent);
            }

            return id;
        }
    }
}
