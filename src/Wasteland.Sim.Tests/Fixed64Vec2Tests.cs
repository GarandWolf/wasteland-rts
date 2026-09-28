using Wasteland.Sim.Math;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class Fixed64Vec2Tests
    {
        [Fact]
        public void Length_3_4_Is5()
        {
            Fixed64Vec2 v = Fixed64Vec2.FromInts(3, 4);
            Assert.Equal(Fixed64.FromInt(5), v.Length());
            Assert.Equal(Fixed64.FromInt(25), v.LengthSquared());
        }

        [Fact]
        public void Length_Zero()
        {
            Assert.Equal(Fixed64.Zero, Fixed64Vec2.Zero.Length());
        }

        [Fact]
        public void Length_AxisAligned()
        {
            Assert.Equal(Fixed64.FromInt(10), new Fixed64Vec2(Fixed64.FromInt(10), Fixed64.Zero).Length());
            Assert.Equal(Fixed64.FromInt(7), new Fixed64Vec2(Fixed64.Zero, Fixed64.FromInt(-7)).Length());
        }

        [Fact]
        public void Arithmetic()
        {
            Fixed64Vec2 a = Fixed64Vec2.FromInts(1, 2);
            Fixed64Vec2 b = Fixed64Vec2.FromInts(3, 4);
            Assert.Equal(Fixed64Vec2.FromInts(4, 6), a + b);
            Assert.Equal(Fixed64Vec2.FromInts(-2, -2), a - b);
            Assert.Equal(Fixed64Vec2.FromInts(2, 4), a * Fixed64.FromInt(2));
        }

        [Fact]
        public void Length_LargeDelta_DoesNotOverflow()
        {
            // Opposite corners of a 200-mile map in meters.
            Fixed64Vec2 delta = Fixed64Vec2.FromInts(321869, 321869);
            Fixed64 length = delta.Length();
            Assert.True(length > Fixed64.FromInt(400000));
            Assert.True(length < Fixed64.FromInt(500000));
        }
    }
}
