using System;
using System.Globalization;

namespace Wasteland.Sim.Math
{
    /// <summary>
    /// Signed Q32.32 fixed-point number stored in a <see cref="long"/>
    /// (1 sign bit + 31 integer bits + 32 fractional bits).
    /// </summary>
    /// <remarks>
    /// Internal math only: sqrt, stepping, angles. Never store a
    /// <see cref="Fixed64"/> in sim state, hashes, or events — those use
    /// integer centimeters and parts-per-thousand.
    /// Multiplication and division use 128-bit intermediates. Division rounds
    /// toward zero. <see cref="ToDouble"/> is debug/presentation only.
    /// </remarks>
    public readonly struct Fixed64 : IEquatable<Fixed64>, IComparable<Fixed64>
    {
        public const int FractionalBits = 32;
        public const long OneRawValue = 1L << FractionalBits;

        public static readonly Fixed64 Zero = new Fixed64(0);
        public static readonly Fixed64 One = new Fixed64(OneRawValue);
        public static readonly Fixed64 Half = new Fixed64(OneRawValue >> 1);
        public static readonly Fixed64 MinusOne = new Fixed64(-OneRawValue);
        public static readonly Fixed64 MaxValue = new Fixed64(long.MaxValue);
        public static readonly Fixed64 MinValue = new Fixed64(long.MinValue);

        public readonly long Raw;

        private Fixed64(long raw)
        {
            Raw = raw;
        }

        public static Fixed64 FromRaw(long raw)
        {
            return new Fixed64(raw);
        }

        public static Fixed64 FromInt(int value)
        {
            return new Fixed64((long)value << FractionalBits);
        }

        /// <summary>Constructs a value equal to <paramref name="milli"/> / 1000.</summary>
        public static Fixed64 FromMilli(int milli)
        {
            return FromInt(milli) / FromInt(1000);
        }

        /// <summary>Constructs a value equal to <paramref name="numerator"/> / <paramref name="denominator"/>.</summary>
        public static Fixed64 FromRatio(int numerator, int denominator)
        {
            if (denominator == 0)
            {
                throw new DivideByZeroException();
            }

            return FromInt(numerator) / FromInt(denominator);
        }

        public int ToInt()
        {
            return (int)(Raw >> FractionalBits);
        }

        /// <summary>Truncates toward zero. Used when writing stepped positions back to centimeters.</summary>
        public int ToIntTowardZero()
        {
            return (int)(Raw / OneRawValue);
        }

        /// <summary>
        /// Debug/presentation helper. Never feed the result back into sim state.
        /// </summary>
        public double ToDouble()
        {
            return Raw / (double)OneRawValue;
        }

        public static Fixed64 operator +(Fixed64 a, Fixed64 b)
        {
            return new Fixed64(checked(a.Raw + b.Raw));
        }

        public static Fixed64 operator -(Fixed64 a, Fixed64 b)
        {
            return new Fixed64(checked(a.Raw - b.Raw));
        }

        public static Fixed64 operator -(Fixed64 value)
        {
            if (value.Raw == long.MinValue)
            {
                throw new OverflowException("Negating Fixed64.MinValue overflows.");
            }

            return new Fixed64(-value.Raw);
        }

        public static Fixed64 operator *(Fixed64 a, Fixed64 b)
        {
            // Two's-complement 64×64 → 128, then arithmetic shift right 32.
            Int128Math.Mul64((ulong)a.Raw, (ulong)b.Raw, out ulong hi, out ulong lo);

            // Unsigned 64×64→128 of the bit patterns is not yet a signed
            // product; sign-extend by subtracting the other operand from `hi`
            // when a factor is negative.
            if (a.Raw < 0)
            {
                hi -= (ulong)b.Raw;
            }

            if (b.Raw < 0)
            {
                hi -= (ulong)a.Raw;
            }
            ulong shifted = (lo >> FractionalBits) | (hi << FractionalBits);
            long result = unchecked((long)shifted);

            // Bits 64..95 of (product >> 32) must be the sign extension of the
            // 64-bit Q32.32 result. Anything else is out of range.
            long actualTop = unchecked((long)hi) >> FractionalBits;
            long expectedTop = result >> 63;
            if (actualTop != expectedTop)
            {
                throw new OverflowException("Fixed64 multiply overflowed Q32.32 range.");
            }

            return new Fixed64(result);
        }

        public static Fixed64 operator /(Fixed64 a, Fixed64 b)
        {
            if (b.Raw == 0)
            {
                throw new DivideByZeroException();
            }

            bool negative = (a.Raw < 0) != (b.Raw < 0);
            ulong absA = Int128Math.AbsU64(a.Raw);
            ulong absB = Int128Math.AbsU64(b.Raw);

            // |a| * 2^32 / |b|
            ulong nHi = absA >> FractionalBits;
            ulong nLo = absA << FractionalBits;
            ulong quot = Int128Math.Div128By64(nHi, nLo, absB);
            if (quot > (ulong)long.MaxValue)
            {
                throw new OverflowException("Fixed64 divide overflowed Q32.32 range.");
            }

            long raw = (long)quot;
            if (negative)
            {
                raw = -raw;
            }

            return new Fixed64(raw);
        }

        public static bool operator ==(Fixed64 a, Fixed64 b)
        {
            return a.Raw == b.Raw;
        }

        public static bool operator !=(Fixed64 a, Fixed64 b)
        {
            return a.Raw != b.Raw;
        }

        public static bool operator <(Fixed64 a, Fixed64 b)
        {
            return a.Raw < b.Raw;
        }

        public static bool operator >(Fixed64 a, Fixed64 b)
        {
            return a.Raw > b.Raw;
        }

        public static bool operator <=(Fixed64 a, Fixed64 b)
        {
            return a.Raw <= b.Raw;
        }

        public static bool operator >=(Fixed64 a, Fixed64 b)
        {
            return a.Raw >= b.Raw;
        }

        public static Fixed64 Abs(Fixed64 value)
        {
            if (value.Raw >= 0)
            {
                return value;
            }

            return -value;
        }

        public static Fixed64 Min(Fixed64 a, Fixed64 b)
        {
            return a.Raw <= b.Raw ? a : b;
        }

        public static Fixed64 Max(Fixed64 a, Fixed64 b)
        {
            return a.Raw >= b.Raw ? a : b;
        }

        public static Fixed64 Clamp(Fixed64 value, Fixed64 min, Fixed64 max)
        {
            if (min.Raw > max.Raw)
            {
                throw new ArgumentException("min must be <= max.");
            }

            if (value.Raw < min.Raw)
            {
                return min;
            }

            if (value.Raw > max.Raw)
            {
                return max;
            }

            return value;
        }

        /// <summary>
        /// Floor of the square root. Negative inputs throw.
        /// Implemented as integer sqrt of (raw &lt;&lt; 32) so the result stays Q32.32.
        /// </summary>
        public static Fixed64 Sqrt(Fixed64 value)
        {
            if (value.Raw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Cannot take the square root of a negative Fixed64.");
            }

            if (value.Raw == 0)
            {
                return Zero;
            }

            ulong nHi = (ulong)value.Raw >> FractionalBits;
            ulong nLo = (ulong)value.Raw << FractionalBits;
            ulong root = Int128Math.IntegerSqrt(nHi, nLo);
            return new Fixed64((long)root);
        }

        public int CompareTo(Fixed64 other)
        {
            return Raw.CompareTo(other.Raw);
        }

        public bool Equals(Fixed64 other)
        {
            return Raw == other.Raw;
        }

        public override bool Equals(object obj)
        {
            return obj is Fixed64 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Raw.GetHashCode();
        }

        public override string ToString()
        {
            return ToDouble().ToString("G9", CultureInfo.InvariantCulture);
        }
    }
}
