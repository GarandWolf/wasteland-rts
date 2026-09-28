using Wasteland.Presentation;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class VatClipPackingTests
    {
        [Fact]
        public void Pack_RoundTripsClipHoldValidAndSpeed()
        {
            uint packed = VatClipPacking.Pack(2, hold: true, valid: true, speed: 1.5f);
            Assert.Equal(2, VatClipPacking.GetClip(packed));
            Assert.True(VatClipPacking.GetHold(packed));
            Assert.True(VatClipPacking.GetValid(packed));
            Assert.Equal(1.5f, VatClipPacking.GetSpeed(packed), 3);
        }

        [Fact]
        public void Zero_IsInvalidBindPose()
        {
            Assert.False(VatClipPacking.GetValid(0));
            Assert.Equal(0, VatClipPacking.GetClip(0));
            Assert.Equal(0f, VatClipPacking.GetSpeed(0));
        }

        [Fact]
        public void Speed_QuantizesToSixtyFourths()
        {
            uint packed = VatClipPacking.Pack(0, false, true, 1f);
            Assert.Equal(64, (int)((packed >> 8) & 255u));
            Assert.Equal(1f, VatClipPacking.GetSpeed(packed));
        }

        [Fact]
        public void WrapLocalTime_StaysInsideHour()
        {
            float t = VatClipPacking.WrapLocalTime(10f, 1f);
            Assert.Equal(9f, t, 3);

            float wrapped = VatClipPacking.WrapLocalTime(0.25f, 3599.5f);
            Assert.Equal(0.75f, wrapped, 3);
        }
    }
}
