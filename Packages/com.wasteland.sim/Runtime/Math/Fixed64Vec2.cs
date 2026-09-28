using System;

namespace Wasteland.Sim.Math
{
    /// <summary>
    /// 2D vector of <see cref="Fixed64"/> values for internal stepping math.
    /// Length uses a 128-bit hypot so large centimeter deltas do not overflow.
    /// </summary>
    public readonly struct Fixed64Vec2 : IEquatable<Fixed64Vec2>
    {
        public static readonly Fixed64Vec2 Zero = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        public readonly Fixed64 X;
        public readonly Fixed64 Y;

        public Fixed64Vec2(Fixed64 x, Fixed64 y)
        {
            X = x;
            Y = y;
        }

        public static Fixed64Vec2 FromInts(int x, int y)
        {
            return new Fixed64Vec2(Fixed64.FromInt(x), Fixed64.FromInt(y));
        }

        public static Fixed64Vec2 operator +(Fixed64Vec2 a, Fixed64Vec2 b)
        {
            return new Fixed64Vec2(a.X + b.X, a.Y + b.Y);
        }

        public static Fixed64Vec2 operator -(Fixed64Vec2 a, Fixed64Vec2 b)
        {
            return new Fixed64Vec2(a.X - b.X, a.Y - b.Y);
        }

        public static Fixed64Vec2 operator -(Fixed64Vec2 value)
        {
            return new Fixed64Vec2(-value.X, -value.Y);
        }

        public static Fixed64Vec2 operator *(Fixed64Vec2 vector, Fixed64 scalar)
        {
            return new Fixed64Vec2(vector.X * scalar, vector.Y * scalar);
        }

        public static Fixed64Vec2 operator *(Fixed64 scalar, Fixed64Vec2 vector)
        {
            return vector * scalar;
        }

        public static Fixed64Vec2 operator /(Fixed64Vec2 vector, Fixed64 scalar)
        {
            return new Fixed64Vec2(vector.X / scalar, vector.Y / scalar);
        }

        public static bool operator ==(Fixed64Vec2 a, Fixed64Vec2 b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(Fixed64Vec2 a, Fixed64Vec2 b)
        {
            return !(a == b);
        }

        public Fixed64 Length()
        {
            ulong ax = Int128Math.AbsU64(X.Raw);
            ulong ay = Int128Math.AbsU64(Y.Raw);
            Int128Math.Mul64(ax, ax, out ulong xHi, out ulong xLo);
            Int128Math.Mul64(ay, ay, out ulong yHi, out ulong yLo);
            Int128Math.Add(xHi, xLo, yHi, yLo, out ulong sHi, out ulong sLo);
            ulong root = Int128Math.IntegerSqrt(sHi, sLo);
            return Fixed64.FromRaw((long)root);
        }

        public Fixed64 LengthSquared()
        {
            return (X * X) + (Y * Y);
        }

        public bool Equals(Fixed64Vec2 other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is Fixed64Vec2 other && Equals(other);
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
