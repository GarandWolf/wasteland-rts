using System;

namespace Wasteland.Sim.Math
{
    /// <summary>
    /// 2D vector of <see cref="Fix64"/> values. Length uses a 128-bit hypot so
    /// large world coordinates (hundreds of kilometers) do not overflow the
    /// Q31.32 multiply result the way x*x + y*y would.
    /// </summary>
    public readonly struct Fix64Vec2 : IEquatable<Fix64Vec2>
    {
        public static readonly Fix64Vec2 Zero = new Fix64Vec2(Fix64.Zero, Fix64.Zero);

        public readonly Fix64 X;
        public readonly Fix64 Y;

        public Fix64Vec2(Fix64 x, Fix64 y)
        {
            X = x;
            Y = y;
        }

        public static Fix64Vec2 FromInts(int x, int y)
        {
            return new Fix64Vec2(Fix64.FromInt(x), Fix64.FromInt(y));
        }

        public static Fix64Vec2 operator +(Fix64Vec2 a, Fix64Vec2 b)
        {
            return new Fix64Vec2(a.X + b.X, a.Y + b.Y);
        }

        public static Fix64Vec2 operator -(Fix64Vec2 a, Fix64Vec2 b)
        {
            return new Fix64Vec2(a.X - b.X, a.Y - b.Y);
        }

        public static Fix64Vec2 operator -(Fix64Vec2 value)
        {
            return new Fix64Vec2(-value.X, -value.Y);
        }

        public static Fix64Vec2 operator *(Fix64Vec2 vector, Fix64 scalar)
        {
            return new Fix64Vec2(vector.X * scalar, vector.Y * scalar);
        }

        public static Fix64Vec2 operator *(Fix64 scalar, Fix64Vec2 vector)
        {
            return vector * scalar;
        }

        public static Fix64Vec2 operator /(Fix64Vec2 vector, Fix64 scalar)
        {
            return new Fix64Vec2(vector.X / scalar, vector.Y / scalar);
        }

        public static bool operator ==(Fix64Vec2 a, Fix64Vec2 b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(Fix64Vec2 a, Fix64Vec2 b)
        {
            return !(a == b);
        }

        public Fix64 Length()
        {
            ulong ax = Int128Math.AbsU64(X.Raw);
            ulong ay = Int128Math.AbsU64(Y.Raw);
            Int128Math.Mul64(ax, ax, out ulong xHi, out ulong xLo);
            Int128Math.Mul64(ay, ay, out ulong yHi, out ulong yLo);
            Int128Math.Add(xHi, xLo, yHi, yLo, out ulong sHi, out ulong sLo);
            ulong root = Int128Math.IntegerSqrt(sHi, sLo);
            return Fix64.FromRaw((long)root);
        }

        public Fix64 LengthSquared()
        {
            return (X * X) + (Y * Y);
        }

        public bool Equals(Fix64Vec2 other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is Fix64Vec2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ")";
        }
    }
}
