using Wasteland.Sim.Math;
using Wasteland.Sim.Random;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class SimRandomTests
    {
        [Fact]
        public void SameSeed_SameSequence()
        {
            SimRandom a = new SimRandom(12345UL);
            SimRandom b = new SimRandom(12345UL);
            for (int i = 0; i < 64; i++)
            {
                Assert.Equal(a.NextUInt(), b.NextUInt());
            }
        }

        [Fact]
        public void DifferentSeed_Diverges()
        {
            SimRandom a = new SimRandom(1UL);
            SimRandom b = new SimRandom(2UL);
            Assert.NotEqual(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void NextInt_StaysInRange()
        {
            SimRandom rng = new SimRandom(99UL);
            for (int i = 0; i < 1000; i++)
            {
                int value = rng.NextInt(-3, 7);
                Assert.InRange(value, -3, 6);
            }
        }

        [Fact]
        public void NextFix64_StaysInRange()
        {
            SimRandom rng = new SimRandom(7UL);
            Fix64 min = Fix64.FromInt(-2);
            Fix64 max = Fix64.FromInt(5);
            for (int i = 0; i < 256; i++)
            {
                Fix64 value = rng.NextFix64(min, max);
                Assert.True(value >= min);
                Assert.True(value < max);
            }
        }
    }
}
