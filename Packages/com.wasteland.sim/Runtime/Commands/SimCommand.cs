using System;

namespace Wasteland.Sim.Commands
{
    /// <summary>
    /// Player or AI intent. Executes at <c>issuedTick + commandDelayTicks</c>.
    /// Presentation never mutates sim state except by enqueueing one of these.
    /// Integer centimeters only — no <c>Fixed64</c> on the wire.
    /// </summary>
    public readonly struct SimCommand : IEquatable<SimCommand>, IComparable<SimCommand>
    {
        public readonly int IssuedTick;
        public readonly int ExecuteTick;
        public readonly int IssuerId;
        public readonly int Sequence;
        public readonly int BattalionId;
        public readonly CommandKind Kind;
        public readonly int Arg0;
        public readonly int Arg1;
        public readonly int Arg2;

        public SimCommand(
            int issuedTick,
            int issuerId,
            int battalionId,
            CommandKind kind,
            int arg0,
            int arg1,
            int arg2,
            int executeTick = 0,
            int sequence = 0)
        {
            if (issuedTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(issuedTick));
            }

            IssuedTick = issuedTick;
            ExecuteTick = executeTick;
            IssuerId = issuerId;
            Sequence = sequence;
            BattalionId = battalionId;
            Kind = kind;
            Arg0 = arg0;
            Arg1 = arg1;
            Arg2 = arg2;
        }

        public static SimCommand SpawnBattalion(int issuedTick, int issuerId, int battalionId, int xCm, int yCm, int speedCmPerSecond)
        {
            return new SimCommand(issuedTick, issuerId, battalionId, CommandKind.SpawnBattalion, xCm, yCm, speedCmPerSecond);
        }

        public static SimCommand Move(int issuedTick, int issuerId, int battalionId, int xCm, int yCm)
        {
            return new SimCommand(issuedTick, issuerId, battalionId, CommandKind.Move, xCm, yCm, 0);
        }

        public SimCommand WithIssuedTick(int issuedTick)
        {
            return new SimCommand(issuedTick, IssuerId, BattalionId, Kind, Arg0, Arg1, Arg2, ExecuteTick, Sequence);
        }

        public SimCommand WithSchedule(int executeTick, int sequence)
        {
            return new SimCommand(IssuedTick, IssuerId, BattalionId, Kind, Arg0, Arg1, Arg2, executeTick, sequence);
        }

        public int CompareTo(SimCommand other)
        {
            int c = ExecuteTick.CompareTo(other.ExecuteTick);
            if (c != 0)
            {
                return c;
            }

            c = IssuerId.CompareTo(other.IssuerId);
            if (c != 0)
            {
                return c;
            }

            return Sequence.CompareTo(other.Sequence);
        }

        public bool Equals(SimCommand other)
        {
            return IssuedTick == other.IssuedTick
                && ExecuteTick == other.ExecuteTick
                && IssuerId == other.IssuerId
                && Sequence == other.Sequence
                && BattalionId == other.BattalionId
                && Kind == other.Kind
                && Arg0 == other.Arg0
                && Arg1 == other.Arg1
                && Arg2 == other.Arg2;
        }

        public override bool Equals(object obj)
        {
            return obj is SimCommand other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IssuedTick;
                hash = (hash * 397) ^ ExecuteTick;
                hash = (hash * 397) ^ IssuerId;
                hash = (hash * 397) ^ Sequence;
                hash = (hash * 397) ^ BattalionId;
                hash = (hash * 397) ^ (byte)Kind;
                hash = (hash * 397) ^ Arg0;
                hash = (hash * 397) ^ Arg1;
                hash = (hash * 397) ^ Arg2;
                return hash;
            }
        }
    }
}
