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

        /// <summary>
        /// Raise <paramref name="unitIndex"/> for a seat. <paramref name="rallyXCm"/>
        /// and <paramref name="rallyYCm"/> are the first Move, not the spawn point.
        /// </summary>
        public static SimCommand RaiseBattalion(
            int issuedTick,
            int issuerId,
            int battalionId,
            int unitIndex,
            int rallyXCm,
            int rallyYCm)
        {
            return new SimCommand(
                issuedTick,
                issuerId,
                battalionId,
                CommandKind.RaiseBattalion,
                unitIndex,
                rallyXCm,
                rallyYCm);
        }

        /// <summary>
        /// Reserved building intent. SimWorld rejects <see cref="CommandKind.Build"/>.
        /// Depots use <see cref="BuildDepot"/>.
        /// </summary>
        public static SimCommand Build(int issuedTick, int issuerId, int xCm, int yCm, int buildingTag)
        {
            return new SimCommand(issuedTick, issuerId, 0, CommandKind.Build, xCm, yCm, buildingTag);
        }

        /// <summary>Road-move a capturer toward a town. Capture is resolved by the world.</summary>
        public static SimCommand ClaimTown(int issuedTick, int issuerId, int battalionId, int townId)
        {
            return new SimCommand(issuedTick, issuerId, battalionId, CommandKind.ClaimTown, townId, 0, 0);
        }

        /// <summary>Reserve a supply depot at a road node. Arg1 and Arg2 stay 0.</summary>
        public static SimCommand BuildDepot(int issuedTick, int issuerId, int builderBattalionId, int nodeId)
        {
            return new SimCommand(issuedTick, issuerId, builderBattalionId, CommandKind.BuildDepot, nodeId, 0, 0);
        }

        /// <summary>Order a battalion to attack another. Arg0 is the target battalion id.</summary>
        public static SimCommand Attack(int issuedTick, int issuerId, int battalionId, int targetBattalionId)
        {
            return new SimCommand(issuedTick, issuerId, battalionId, CommandKind.Attack, targetBattalionId, 0, 0);
        }

        /// <summary>Start tier research for the issuing seat. Arg0 is the catalog tech index; BattalionId stays 0.</summary>
        public static SimCommand ResearchTech(int issuedTick, int issuerId, int techIndex)
        {
            return new SimCommand(issuedTick, issuerId, 0, CommandKind.ResearchTech, techIndex, 0, 0);
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
