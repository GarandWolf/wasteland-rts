using System;
using Wasteland.Sim.Math;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class Fixed64Tests
    {
        [Fact]
        public void FromInt_RoundTripsSmallIntegers()
        {
            for (int i = -50; i <= 50; i++)
            {
                Assert.Equal(i, Fixed64.FromInt(i).ToInt());
            }
        }

        [Fact]
        public void AddSub_MatchesIntegerArithmetic()
        {
            Fixed64 a = Fixed64.FromInt(40);
            Fixed64 b = Fixed64.FromInt(2);
            Assert.Equal(Fixed64.FromInt(42), a + b);
            Assert.Equal(Fixed64.FromInt(38), a - b);
            Assert.Equal(Fixed64.FromInt(-40), -a);
        }

        [Fact]
        public void Mul_IntegersAndFractions()
        {
            Assert.Equal(Fixed64.FromInt(12), Fixed64.FromInt(3) * Fixed64.FromInt(4));
            Assert.Equal(Fixed64.FromInt(-12), Fixed64.FromInt(-3) * Fixed64.FromInt(4));
            Assert.Equal(Fixed64.FromInt(12), Fixed64.FromInt(-3) * Fixed64.FromInt(-4));
            Assert.Equal(Fixed64.FromRatio(-1, 4), Fixed64.FromInt(-1) * Fixed64.Half * Fixed64.Half);
            Assert.Equal(Fixed64.FromRatio(1, 4), Fixed64.Half * Fixed64.Half);
            Assert.Equal(Fixed64.FromInt(1), Fixed64.FromMilli(250) * Fixed64.FromInt(4));
        }

        [Fact]
        public void Div_TowardZero()
        {
            Assert.Equal(Fixed64.FromMilli(2500), Fixed64.FromInt(10) / Fixed64.FromInt(4));
            Assert.Equal(Fixed64.FromMilli(-2500), Fixed64.FromInt(-10) / Fixed64.FromInt(4));
            Assert.Equal(Fixed64.FromMilli(-2500), Fixed64.FromInt(10) / Fixed64.FromInt(-4));
            Assert.Equal(Fixed64.FromMilli(2500), Fixed64.FromInt(-10) / Fixed64.FromInt(-4));
            Assert.Equal(Fixed64.FromInt(2), Fixed64.One / Fixed64.FromRatio(1, 2));
        }

        [Fact]
        public void Div_ByZero_Throws()
        {
            Assert.Throws<DivideByZeroException>(() => Fixed64.One / Fixed64.Zero);
        }

        [Fact]
        public void Sqrt_PerfectSquares()
        {
            Assert.Equal(Fixed64.Zero, Fixed64.Sqrt(Fixed64.Zero));
            Assert.Equal(Fixed64.One, Fixed64.Sqrt(Fixed64.One));
            Assert.Equal(Fixed64.FromInt(2), Fixed64.Sqrt(Fixed64.FromInt(4)));
            Assert.Equal(Fixed64.FromInt(12), Fixed64.Sqrt(Fixed64.FromInt(144)));
        }

        [Fact]
        public void Sqrt_Two_SquaresBackNearTwo()
        {
            Fixed64 root = Fixed64.Sqrt(Fixed64.FromInt(2));
            Fixed64 squared = root * root;
            long error = System.Math.Abs(squared.Raw - Fixed64.FromInt(2).Raw);
            Assert.True(error <= 8, "sqrt(2)^2 should land within 8 ULP of 2, error was " + error);
        }

        [Fact]
        public void Sqrt_Negative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixed64.Sqrt(Fixed64.FromInt(-1)));
        }

        [Fact]
        public void Comparisons_AndHelpers()
        {
            Assert.True(Fixed64.FromInt(1) < Fixed64.FromInt(2));
            Assert.True(Fixed64.FromInt(2) > Fixed64.FromInt(1));
            Assert.True(Fixed64.One >= Fixed64.FromMilli(999));
            Assert.True(Fixed64.One <= Fixed64.FromMilli(1001));
            Assert.Equal(Fixed64.FromInt(3), Fixed64.Max(Fixed64.FromInt(1), Fixed64.FromInt(3)));
            Assert.Equal(Fixed64.FromInt(4), Fixed64.Abs(Fixed64.FromInt(-4)));
            Assert.Equal(Fixed64.FromInt(2), Fixed64.Clamp(Fixed64.FromInt(5), Fixed64.Zero, Fixed64.FromInt(2)));
        }

        [Fact]
        public void Mul_Overflow_Throws()
        {
            Assert.Throws<OverflowException>(() => Fixed64.FromInt(100000) * Fixed64.FromInt(100000));
        }

        [Fact]
        public void Add_Overflow_Throws()
        {
            Assert.Throws<OverflowException>(() => Fixed64.MaxValue + Fixed64.One);
        }

        [Fact]
        public void FromMilli_Fractions()
        {
            Assert.Equal(Fixed64.One, Fixed64.FromMilli(1000));
            Assert.Equal(Fixed64.Half, Fixed64.FromMilli(500));
            Assert.Equal(Fixed64.FromRatio(3, 2), Fixed64.FromMilli(1500));
        }

        [Fact]
        public void LargeWorldCoordinate_StillFits()
        {
            // Internal math must cover 200-mile-scale values (meters here; sim state uses cm).
            Fixed64 meters = Fixed64.FromInt(321869);
            Assert.Equal(321869, meters.ToInt());
            Assert.Equal(Fixed64.FromInt(643738), meters + meters);
        }
    }
}
