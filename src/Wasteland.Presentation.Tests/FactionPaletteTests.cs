using Wasteland.Presentation;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class FactionPaletteTests
    {
        [Theory]
        [InlineData(0, 0, FactionPalette.RemnantBase, FactionPalette.RemnantAccentLight)]
        [InlineData(0, 1, FactionPalette.RemnantLight, FactionPalette.RemnantAccentDark)]
        [InlineData(0, 2, FactionPalette.RemnantDark, FactionPalette.RemnantAccentLight)]
        [InlineData(1, 0, FactionPalette.CompactBase, FactionPalette.CompactAccent)]
        [InlineData(1, 1, FactionPalette.CompactLight, FactionPalette.CompactAccent)]
        [InlineData(1, 2, FactionPalette.CompactDark, FactionPalette.CompactAccent)]
        [InlineData(2, 0, FactionPalette.ClansBase, FactionPalette.ClansAccentChrome)]
        [InlineData(2, 1, FactionPalette.ClansLight, FactionPalette.ClansAccentSoot)]
        [InlineData(2, 2, FactionPalette.ClansDark, FactionPalette.ClansAccentChrome)]
        public void ShadeHasExpectedPrimaryAndAccent(int faction, int shade, uint primary, uint accent)
        {
            Assert.Equal(primary, FactionPalette.Primary(faction, shade));
            Assert.Equal(accent, FactionPalette.Accent(faction, shade));
        }

        [Fact]
        public void FillRgba32_WritesTwoBySixteenLut()
        {
            byte[] pixels = new byte[FactionPalette.Width * FactionPalette.Height * 4];
            FactionPalette.FillRgba32(pixels);

            int remnantBase = (0 * FactionPalette.Width + FactionPalette.ColumnPrimary) * 4;
            Assert.Equal(0x3A, pixels[remnantBase]);
            Assert.Equal(0x78, pixels[remnantBase + 1]);
            Assert.Equal(0xBE, pixels[remnantBase + 2]);

            int remnantAccent = (0 * FactionPalette.Width + FactionPalette.ColumnAccent) * 4;
            Assert.Equal(0xE8, pixels[remnantAccent]);
            Assert.Equal(0xE4, pixels[remnantAccent + 1]);
            Assert.Equal(0xD8, pixels[remnantAccent + 2]);

            int compactLightRow = TeamIndexPacking.PaletteRow(1, 1);
            int compactLight = (compactLightRow * FactionPalette.Width + FactionPalette.ColumnPrimary) * 4;
            Assert.Equal(0xFF, pixels[compactLight]);
            Assert.Equal(0xEB, pixels[compactLight + 1]);
            Assert.Equal(0x99, pixels[compactLight + 2]);
        }
    }
}
