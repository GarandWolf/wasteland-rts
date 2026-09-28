using System;
using Wasteland.Sim.Math;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class Fix64Tests
    {
        [Fact]
        public void FromInt_RoundTripsSmallIntegers()
        {
            for (int i = -50; i <= 50; i++)
            {
                Assert.Equal(i, Fix64.FromInt(i).ToInt());
            }
        }

        [Fact]
        public void AddSub_MatchesIntegerArithmetic()
        {
            Fix64 a = Fix64.FromInt(40);
            Fix64 b = Fix64.FromInt(2);
            Assert.Equal(Fix64.FromInt(42), a + b);
            Assert.Equal(Fix64.FromInt(38), a - b);
            Assert.Equal(Fix64.FromInt(-40), -a);
        }

        [Fact]
        public void Mul_IntegersAndFractions()
        {
            Assert.Equal(Fix64.FromInt(12), Fix64.FromInt(3) * Fix64.FromInt(4));
            Assert.Equal(Fix64.FromInt(-12), Fix64.FromInt(-3) * Fix64.FromInt(4));
            Assert.Equal(Fix64.FromInt(12), Fix64.FromInt(-3) * Fix64.FromInt(-4));
            Assert.Equal(Fix64.FromRatio(-1, 4), Fix64.FromInt(-1) * Fix64.Half * Fix64.Half);
            Assert.Equal(Fix64.FromRatio(1, 4), Fix64.Half * Fix64.Half);
            Assert.Equal(Fix64.FromInt(1), Fix64.FromMilli(250) * Fix64.FromInt(4));
        }

        [Fact]
        public void Div_TowardZero()
        {
            Assert.Equal(Fix64.FromMilli(2500), Fix64.FromInt(10) / Fix64.FromInt(4));
            Assert.Equal(Fix64.FromMilli(-2500), Fix64.FromInt(-10) / Fix64.FromInt(4));
            Assert.Equal(Fix64.FromMilli(-2500), Fix64.FromInt(10) / Fix64.FromInt(-4));
            Assert.Equal(Fix64.FromMilli(2500), Fix64.FromInt(-10) / Fix64.FromInt(-4));
            Assert.Equal(Fix64.FromInt(2), Fix64.One / Fix64.FromRatio(1, 2));
        }

        [Fact]
        public void Div_ByZero_Throws()
        {
            Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
        }

        [Fact]
        public void Sqrt_PerfectSquares()
        {
            Assert.Equal(Fix64.Zero, Fix64.Sqrt(Fix64.Zero));
            Assert.Equal(Fix64.One, Fix64.Sqrt(Fix64.One));
            Assert.Equal(Fix64.FromInt(2), Fix64.Sqrt(Fix64.FromInt(4)));
            Assert.Equal(Fix64.FromInt(12), Fix64.Sqrt(Fix64.FromInt(144)));
        }

        [Fact]
        public void Sqrt_Two_SquaresBackNearTwo()
        {
            Fix64 root = Fix64.Sqrt(Fix64.FromInt(2));
            Fix64 squared = root * root;
            long error = System.Math.Abs(squared.Raw - Fix64.FromInt(2).Raw);
            Assert.True(error <= 8, "sqrt(2)^2 should land within 8 ULP of 2, error was " + error);
        }

        [Fact]
        public void Sqrt_Negative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.Sqrt(Fix64.FromInt(-1)));
        }

        [Fact]
        public void Comparisons_AndHelpers()
        {
            Assert.True(Fix64.FromInt(1) < Fix64.FromInt(2));
            Assert.True(Fix64.FromInt(2) > Fix64.FromInt(1));
            Assert.True(Fix64.One >= Fix64.FromMilli(999));
            Assert.True(Fix64.One <= Fix64.FromMilli(1001));
            Assert.Equal(Fix64.FromInt(3), Fix64.Max(Fix64.FromInt(1), Fix64.FromInt(3)));
            Assert.Equal(Fix64.FromInt(4), Fix64.Abs(Fix64.FromInt(-4)));
            Assert.Equal(Fix64.FromInt(2), Fix64.Clamp(Fix64.FromInt(5), Fix64.Zero, Fix64.FromInt(2)));
        }

        [Fact]
        public void Mul_Overflow_Throws()
        {
            Assert.Throws<OverflowException>(() => Fix64.FromInt(100000) * Fix64.FromInt(100000));
        }

        [Fact]
        public void Add_Overflow_Throws()
        {
            Assert.Throws<OverflowException>(() => Fix64.MaxValue + Fix64.One);
        }

        [Fact]
        public void FromMilli_Fractions()
        {
            Assert.Equal(Fix64.One, Fix64.FromMilli(1000));
            Assert.Equal(Fix64.Half, Fix64.FromMilli(500));
            Assert.Equal(Fix64.FromRatio(3, 2), Fix64.FromMilli(1500));
        }

        [Fact]
        public void LargeWorldCoordinate_StillFits()
        {
            // 200 miles ≈ 321,869 meters. Positions must live in Q31.32.
            Fix64 meters = Fix64.FromInt(321869);
            Assert.Equal(321869, meters.ToInt());
            Assert.Equal(Fix64.FromInt(643738), meters + meters);
        }
    }
}
