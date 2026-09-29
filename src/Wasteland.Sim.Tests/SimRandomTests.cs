using System;
using Wasteland.Sim.Random;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class SimRandomTests
    {
        [Fact]
        public void Golden_Pcg32_Seed42_Stream54()
        {
            // pcg-c-basic demo: pcg32_srandom_r(&rng, 42u, 54u)
            SimRandom rng = new SimRandom(42UL, 54UL);
            uint[] expected =
            {
                0xA15C02B7u,
                0x7B47F409u,
                0xBA1D3330u,
                0x83D2F293u,
                0xBFA4784Bu,
                0xCBED606Eu
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], rng.NextUInt());
            }
        }

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
        public void FromPersisted_RoundTripsStateAndIncrement()
        {
            SimRandom rng = new SimRandom(42UL, 54UL);
            rng.NextUInt();
            rng.NextUInt();
            Assert.Equal((54UL << 1) | 1UL, rng.Increment);

            SimRandom restored = SimRandom.FromPersisted(rng.State, rng.Increment);
            Assert.Equal(rng.State, restored.State);
            Assert.Equal(rng.Increment, restored.Increment);
            Assert.Equal(rng.NextUInt(), restored.NextUInt());
        }

        [Fact]
        public void FromPersisted_RejectsEvenIncrement()
        {
            Assert.Throws<ArgumentException>(() => SimRandom.FromPersisted(1UL, 2UL));
        }
    }
}
