using System;

namespace Wasteland.Sim.Math
{
    /// <summary>
    /// Portable 128-bit unsigned helpers for Q32.32 arithmetic.
    /// All operations are integer-only so they are bit-identical across runtimes.
    /// </summary>
    internal static class Int128Math
    {
        public static ulong AbsU64(long value)
        {
            if (value >= 0)
            {
                return (ulong)value;
            }

            return (ulong)(-(value + 1)) + 1UL;
        }

        public static void Mul64(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            ulong aLo = (uint)a;
            ulong aHi = a >> 32;
            ulong bLo = (uint)b;
            ulong bHi = b >> 32;

            ulong p0 = aLo * bLo;
            ulong p1 = aLo * bHi;
            ulong p2 = aHi * bLo;
            ulong p3 = aHi * bHi;

            ulong mid = (p0 >> 32) + (uint)p1 + (uint)p2;
            lo = (p0 & 0xFFFFFFFFUL) | (mid << 32);
            hi = p3 + (p1 >> 32) + (p2 >> 32) + (mid >> 32);
        }

        public static void Add(ulong aHi, ulong aLo, ulong bHi, ulong bLo, out ulong hi, out ulong lo)
        {
            lo = unchecked(aLo + bLo);
            ulong carry = lo < aLo ? 1UL : 0UL;
            ulong sumHi = unchecked(aHi + bHi);
            if (sumHi < aHi)
            {
                throw new OverflowException("128-bit add overflowed.");
            }

            hi = unchecked(sumHi + carry);
            if (carry != 0 && hi < sumHi)
            {
                throw new OverflowException("128-bit add overflowed.");
            }
        }

        public static int Compare(ulong aHi, ulong aLo, ulong bHi, ulong bLo)
        {
            if (aHi != bHi)
            {
                return aHi > bHi ? 1 : -1;
            }

            if (aLo == bLo)
            {
                return 0;
            }

            return aLo > bLo ? 1 : -1;
        }

        /// <summary>
        /// Floor of the integer square root of a 128-bit unsigned value.
        /// </summary>
        public static ulong IntegerSqrt(ulong hi, ulong lo)
        {
            if (hi == 0 && lo <= 1UL)
            {
                return lo;
            }

            ulong low = 0;
            ulong high = hi == 0 ? 0x100000000UL : ulong.MaxValue;
            ulong ans = 0;

            while (low <= high)
            {
                ulong mid = low + ((high - low) >> 1);
                Mul64(mid, mid, out ulong sHi, out ulong sLo);
                if (Compare(sHi, sLo, hi, lo) <= 0)
                {
                    ans = mid;
                    if (mid == ulong.MaxValue)
                    {
                        break;
                    }

                    low = mid + 1UL;
                }
                else
                {
                    if (mid == 0UL)
                    {
                        break;
                    }

                    high = mid - 1UL;
                }
            }

            return ans;
        }

        /// <summary>
        /// Unsigned 128-bit / 64-bit division. Quotient must fit in 64 bits.
        /// </summary>
        public static ulong Div128By64(ulong nHi, ulong nLo, ulong denom)
        {
            if (denom == 0)
            {
                throw new DivideByZeroException();
            }

            if (nHi == 0)
            {
                return nLo / denom;
            }

            if (nHi >= denom)
            {
                throw new OverflowException("Division quotient does not fit in 64 bits.");
            }

            ulong rem = nHi;
            ulong quot = 0;
            for (int i = 63; i >= 0; i--)
            {
                ulong bit = (nLo >> i) & 1UL;
                bool overflow = rem > (ulong.MaxValue >> 1);
                rem = (rem << 1) | bit;
                if (overflow || rem >= denom)
                {
                    rem -= denom;
                    quot |= 1UL << i;
                }
            }

            return quot;
        }
    }
}
