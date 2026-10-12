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
            return Record(factionId, AiIssuedKind.Move, battalionId, xCm, yCm, 0, string.Empty);
        }

        /// <summary>Sequence stamped on the last command this sink forwarded into the world.</summary>
        public int LastSequence
        {
            get { return _lastSequence; }
        }

        /// <summary>
        /// Next id assigned to a non-raise issued command. Saved so
        /// <see cref="AiFactionState.LastIssuedCommandId"/> does not collide
        /// with a live battalion id after load.
        /// </summary>
        internal int NextIssuedId
        {
            get { return _nextId; }
            set { _nextId = value < 1 ? 1 : value; }
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
            for (int i = 0; i < _issued.Count; i++)
            {
                if (_issued[i].Id == commandId)
                {
                    return RejectOfSequence(_issued[i].FactionId, _issued[i].Sequence);
                }
            }

            return AiCommandReject.None;
        }

        public AiCommandReject RejectOfSequence(int issuerId, int sequence)
        {
            CommandRejectReason code;
            int param0;
            int param1;
            if (_inner.TryGetRejectionByKey(RejectKey(issuerId, sequence), out code, out param0, out param1))
            {
                return MapReject(code);
            }

            return AiCommandReject.None;
        }

        private static ulong RejectKey(int issuerId, int sequence)
        {
            return ((ulong)(uint)issuerId << 32) | (uint)sequence;
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

            if (code == CommandRejectReason.InsufficientManpower)
            {
                return AiCommandReject.InsufficientManpower;
            }

            if (code == CommandRejectReason.InsufficientMaterials)
            {
                return AiCommandReject.InsufficientMaterials;
            }

            if (code == CommandRejectReason.InsufficientFuel)
            {
                return AiCommandReject.InsufficientFuel;
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
                    unitId,
                    command.Sequence));
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

            _issued.Add(new AiIssuedCommand(id, _inner.Tick, factionId, kind, arg0, arg1, arg2, arg3, unitId, _lastSequence));
            if (kind == AiIssuedKind.ClaimTown || kind == AiIssuedKind.BuildDepot || kind == AiIssuedKind.Move)
            {
                AiPendingIntent intent = default;
                intent.Sequence = _lastSequence;
                intent.Kind = kind;
                intent.BattalionId = arg0;
                intent.TargetId = TargetIdOf(kind, arg1, arg2);
                intent.IssuedTick = _inner.Tick;
                _owned.EnqueueIntent(factionId, intent);
            }

            return id;
        }

        private int TargetIdOf(AiIssuedKind kind, int arg1, int arg2)
        {
            if (kind != AiIssuedKind.Move)
            {
                return arg1;
            }

            if (_inner is ISimWorldView view)
            {
                int nodeId = view.GetNearestNodeId(arg1, arg2);
                if (nodeId != 0)
                {
                    return nodeId;
                }
            }

            return arg1;
        }
    }
}
