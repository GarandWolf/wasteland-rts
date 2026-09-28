using Wasteland.Presentation;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class TeamIndexPackingTests
    {
        [Fact]
        public void Pack_RoundTripsFields()
        {
            uint packed = TeamIndexPacking.Pack(2, 1, 2, hatch: true, valid: true);
            Assert.Equal(2, TeamIndexPacking.GetFaction(packed));
            Assert.Equal(1, TeamIndexPacking.GetShade(packed));
            Assert.Equal(2, TeamIndexPacking.GetPips(packed));
            Assert.True(TeamIndexPacking.GetHatch(packed));
            Assert.True(TeamIndexPacking.GetValid(packed));
        }

        [Fact]
        public void Zero_IsInvalidRemnantBase()
        {
            Assert.Equal(TeamIndexPacking.FactionRemnant, TeamIndexPacking.GetFaction(0));
            Assert.Equal(TeamIndexPacking.ShadeBase, TeamIndexPacking.GetShade(0));
            Assert.Equal(0, TeamIndexPacking.GetPips(0));
            Assert.False(TeamIndexPacking.GetValid(0));
        }

        [Fact]
        public void PaletteRow_IsFactionTimesFourPlusShade()
        {
            Assert.Equal(0, TeamIndexPacking.PaletteRow(0, 0));
            Assert.Equal(5, TeamIndexPacking.PaletteRow(1, 1));
            Assert.Equal(10, TeamIndexPacking.PaletteRow(2, 2));
            Assert.Equal(15, TeamIndexPacking.PaletteRow(3, 3));
        }

        [Fact]
        public void ReservedBits_StayZero()
        {
            uint packed = TeamIndexPacking.Pack(1, 2, 3, true, true);
            Assert.Equal(0u, packed >> 8);
        }

        [Fact]
        public void AsFloat_IsIeeeBitCopyNotNumericConvert()
        {
            uint packed = TeamIndexPacking.Pack(1, 2, 3, true, true);
            float bits = TeamIndexPacking.AsFloat(packed);
            Assert.Equal(packed, TeamIndexPacking.AsUInt(bits));
            Assert.NotEqual((float)packed, bits);
        }
    }
}
