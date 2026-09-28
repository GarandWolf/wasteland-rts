using System;
using Wasteland.Sim.Math;

namespace Wasteland.Sim.Commands
{
    /// <summary>
    /// Player or AI intent, scheduled for an exact simulation tick.
    /// Presentation never mutates sim state except by enqueueing one of these.
    /// </summary>
    public readonly struct SimCommand : IEquatable<SimCommand>, IComparable<SimCommand>
    {
        public readonly int Tick;
        public readonly int PlayerId;
        public readonly int UnitId;
        public readonly CommandKind Kind;
        public readonly Fix64 Arg0;
        public readonly Fix64 Arg1;
        public readonly Fix64 Arg2;

        public SimCommand(int tick, int playerId, int unitId, CommandKind kind, Fix64 arg0, Fix64 arg1, Fix64 arg2)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            Tick = tick;
            PlayerId = playerId;
            UnitId = unitId;
            Kind = kind;
            Arg0 = arg0;
            Arg1 = arg1;
            Arg2 = arg2;
        }

        public static SimCommand Spawn(int tick, int playerId, int unitId, Fix64Vec2 position, Fix64 speed)
        {
            if (speed.Raw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be non-negative.");
            }

            return new SimCommand(tick, playerId, unitId, CommandKind.Spawn, position.X, position.Y, speed);
        }

        public static SimCommand Move(int tick, int playerId, int unitId, Fix64Vec2 target)
        {
            return new SimCommand(tick, playerId, unitId, CommandKind.Move, target.X, target.Y, Fix64.Zero);
        }

        public int CompareTo(SimCommand other)
        {
            int c = Tick.CompareTo(other.Tick);
            if (c != 0)
            {
                return c;
            }

            c = PlayerId.CompareTo(other.PlayerId);
            if (c != 0)
            {
                return c;
            }

            c = UnitId.CompareTo(other.UnitId);
            if (c != 0)
            {
                return c;
            }

            c = ((byte)Kind).CompareTo((byte)other.Kind);
            if (c != 0)
            {
                return c;
            }

            c = Arg0.Raw.CompareTo(other.Arg0.Raw);
            if (c != 0)
            {
                return c;
            }

            c = Arg1.Raw.CompareTo(other.Arg1.Raw);
            if (c != 0)
            {
                return c;
            }

            return Arg2.Raw.CompareTo(other.Arg2.Raw);
        }

        public bool Equals(SimCommand other)
        {
            return CompareTo(other) == 0;
        }

        public override bool Equals(object obj)
        {
            return obj is SimCommand other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Tick;
                hash = (hash * 397) ^ PlayerId;
                hash = (hash * 397) ^ UnitId;
                hash = (hash * 397) ^ (byte)Kind;
                hash = (hash * 397) ^ Arg0.Raw.GetHashCode();
                hash = (hash * 397) ^ Arg1.Raw.GetHashCode();
                hash = (hash * 397) ^ Arg2.Raw.GetHashCode();
                return hash;
            }
        }
    }
}
