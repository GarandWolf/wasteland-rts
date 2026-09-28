using System;
using System.Globalization;

namespace Wasteland.Sim.Math
{
    /// <summary>
    /// Signed Q31.32 fixed-point number stored in a <see cref="long"/>.
    /// Range is about ±2.147e9 with 2^-32 ≈ 2.33e-10 precision. World units
    /// are meters: a 200-mile map fits with huge headroom.
    /// </summary>
    /// <remarks>
    /// Multiplication and division use 128-bit intermediates. Division rounds
    /// toward zero. No floating-point is used in any operator — presentation
    /// code may call <see cref="ToDouble"/> for debug drawing only.
    /// </remarks>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public const int FractionalBits = 32;
        public const long OneRawValue = 1L << FractionalBits;

        public static readonly Fix64 Zero = new Fix64(0);
        public static readonly Fix64 One = new Fix64(OneRawValue);
        public static readonly Fix64 Half = new Fix64(OneRawValue >> 1);
        public static readonly Fix64 MinusOne = new Fix64(-OneRawValue);
        public static readonly Fix64 MaxValue = new Fix64(long.MaxValue);
        public static readonly Fix64 MinValue = new Fix64(long.MinValue);

        public readonly long Raw;

        private Fix64(long raw)
        {
            Raw = raw;
        }

        public static Fix64 FromRaw(long raw)
        {
            return new Fix64(raw);
        }

        public static Fix64 FromInt(int value)
        {
            return new Fix64((long)value << FractionalBits);
        }

        /// <summary>Constructs a value equal to <paramref name="milli"/> / 1000.</summary>
        public static Fix64 FromMilli(int milli)
        {
            return FromInt(milli) / FromInt(1000);
        }

        /// <summary>Constructs a value equal to <paramref name="numerator"/> / <paramref name="denominator"/>.</summary>
        public static Fix64 FromRatio(int numerator, int denominator)
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

        /// <summary>
        /// Debug/presentation helper. Never feed the result back into sim state.
        /// </summary>
        public double ToDouble()
        {
            return Raw / (double)OneRawValue;
        }

        public static Fix64 operator +(Fix64 a, Fix64 b)
        {
            return new Fix64(checked(a.Raw + b.Raw));
        }

        public static Fix64 operator -(Fix64 a, Fix64 b)
        {
            return new Fix64(checked(a.Raw - b.Raw));
        }

        public static Fix64 operator -(Fix64 value)
        {
            if (value.Raw == long.MinValue)
            {
                throw new OverflowException("Negating Fix64.MinValue overflows.");
            }

            return new Fix64(-value.Raw);
        }

        public static Fix64 operator *(Fix64 a, Fix64 b)
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
            // 64-bit Q31.32 result. Anything else is out of range.
            long actualTop = unchecked((long)hi) >> FractionalBits;
            long expectedTop = result >> 63;
            if (actualTop != expectedTop)
            {
                throw new OverflowException("Fix64 multiply overflowed Q31.32 range.");
            }

            return new Fix64(result);
        }

        public static Fix64 operator /(Fix64 a, Fix64 b)
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
                throw new OverflowException("Fix64 divide overflowed Q31.32 range.");
            }

            long raw = (long)quot;
            if (negative)
            {
                raw = -raw;
            }

            return new Fix64(raw);
        }

        public static bool operator ==(Fix64 a, Fix64 b)
        {
            return a.Raw == b.Raw;
        }

        public static bool operator !=(Fix64 a, Fix64 b)
        {
            return a.Raw != b.Raw;
        }

        public static bool operator <(Fix64 a, Fix64 b)
        {
            return a.Raw < b.Raw;
        }

        public static bool operator >(Fix64 a, Fix64 b)
        {
            return a.Raw > b.Raw;
        }

        public static bool operator <=(Fix64 a, Fix64 b)
        {
            return a.Raw <= b.Raw;
        }

        public static bool operator >=(Fix64 a, Fix64 b)
        {
            return a.Raw >= b.Raw;
        }

        public static Fix64 Abs(Fix64 value)
        {
            if (value.Raw >= 0)
            {
                return value;
            }

            return -value;
        }

        public static Fix64 Min(Fix64 a, Fix64 b)
        {
            return a.Raw <= b.Raw ? a : b;
        }

        public static Fix64 Max(Fix64 a, Fix64 b)
        {
            return a.Raw >= b.Raw ? a : b;
        }

        public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
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
        /// Implemented as integer sqrt of (raw &lt;&lt; 32) so the result stays Q31.32.
        /// </summary>
        public static Fix64 Sqrt(Fix64 value)
        {
            if (value.Raw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Cannot take the square root of a negative Fix64.");
            }

            if (value.Raw == 0)
            {
                return Zero;
            }

            ulong nHi = (ulong)value.Raw >> FractionalBits;
            ulong nLo = (ulong)value.Raw << FractionalBits;
            ulong root = Int128Math.IntegerSqrt(nHi, nLo);
            return new Fix64((long)root);
        }

        public int CompareTo(Fix64 other)
        {
            return Raw.CompareTo(other.Raw);
        }

        public bool Equals(Fix64 other)
        {
            return Raw == other.Raw;
        }

        public override bool Equals(object obj)
        {
            return obj is Fix64 other && Equals(other);
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
