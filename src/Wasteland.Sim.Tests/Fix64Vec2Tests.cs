using Wasteland.Sim.Math;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class Fix64Vec2Tests
    {
        [Fact]
        public void Length_3_4_Is5()
        {
            Fix64Vec2 v = Fix64Vec2.FromInts(3, 4);
            Assert.Equal(Fix64.FromInt(5), v.Length());
            Assert.Equal(Fix64.FromInt(25), v.LengthSquared());
        }

        [Fact]
        public void Length_Zero()
        {
            Assert.Equal(Fix64.Zero, Fix64Vec2.Zero.Length());
        }

        [Fact]
        public void Length_AxisAligned()
        {
            Assert.Equal(Fix64.FromInt(10), new Fix64Vec2(Fix64.FromInt(10), Fix64.Zero).Length());
            Assert.Equal(Fix64.FromInt(7), new Fix64Vec2(Fix64.Zero, Fix64.FromInt(-7)).Length());
        }

        [Fact]
        public void Arithmetic()
        {
            Fix64Vec2 a = Fix64Vec2.FromInts(1, 2);
            Fix64Vec2 b = Fix64Vec2.FromInts(3, 4);
            Assert.Equal(Fix64Vec2.FromInts(4, 6), a + b);
            Assert.Equal(Fix64Vec2.FromInts(-2, -2), a - b);
            Assert.Equal(Fix64Vec2.FromInts(2, 4), a * Fix64.FromInt(2));
        }

        [Fact]
        public void Length_LargeDelta_DoesNotOverflow()
        {
            // Opposite corners of a 200-mile map in meters.
            Fix64Vec2 delta = Fix64Vec2.FromInts(321869, 321869);
            Fix64 length = delta.Length();
            Assert.True(length > Fix64.FromInt(400000));
            Assert.True(length < Fix64.FromInt(500000));
        }
    }
}
