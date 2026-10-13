using System;
using Wasteland.Audio;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public class CombatBedRolloffTests
    {
        const float Aspect = 16f / 9f;
        // Default zoom frames the whole 100-mile Gorge map (TacticalCamera.Frame: size * 1.04).
        const float DefaultHalfH = 160934f * 0.5f * 1.04f;

        static float Db(float gain) => gain <= 0f ? float.NegativeInfinity : 20f * (float)Math.Log10(gain);

        [Theory]
        [InlineData(DefaultHalfH)]
        [InlineData(2000f)]
        [InlineData(200f)]
        public void CameraCentredOnDalles_IsAboveMinus6Db(float halfH)
        {
            CombatBedRolloff.Compute(halfH * Aspect, halfH, 40f, out float full, out float reach);
            // Camera 40 m above ground straight over the bed; allow 200 m of relief.
            float distance = (float)Math.Sqrt(40f * 40f + 200f * 200f);
            Assert.True(Db(CombatBedRolloff.Gain(distance, full, reach)) > -6f);
        }

        [Theory]
        [InlineData(DefaultHalfH)]
        [InlineData(2000f)]
        [InlineData(200f)]
        public void OneViewWidthOffScreen_IsSilent(float halfH)
        {
            float halfW = halfH * Aspect;
            CombatBedRolloff.Compute(halfW, halfH, 40f, out float full, out float reach);
            float distance = halfW + 2f * halfW;
            Assert.Equal(0f, CombatBedRolloff.Gain(distance, full, reach));
        }

        [Fact]
        public void OldLogarithmicMinOneMetre_WasAboutMinus32Db()
        {
            float oldGain = 1f / 40f;
            Assert.True(Db(oldGain) < -30f);
        }

        [Fact]
        public void Curve_IsMonotonic_AndKneeInRange()
        {
            CombatBedRolloff.Compute(1000f, 600f, 40f, out float full, out float reach);
            Assert.True(reach > full);
            float k = CombatBedRolloff.NormalizedKnee(full, reach);
            Assert.InRange(k, 0f, 1f);
            float prev = 1f;
            for (float d = 0f; d <= reach + 10f; d += 25f)
            {
                float g = CombatBedRolloff.Gain(d, full, reach);
                Assert.True(g <= prev + 1e-6f);
                prev = g;
            }
        }
    }
}
