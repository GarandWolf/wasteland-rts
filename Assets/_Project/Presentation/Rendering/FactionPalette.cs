namespace Wasteland.Presentation
{
    /// <summary>
    /// Faction hex values from faction-briefs-v1.1 §6. Stored as 0xRRGGBB.
    /// Column 0 of the LUT is primary; column 1 is the paired accent.
    /// </summary>
    public static class FactionPalette
    {
        public const int Width = 2;
        public const int Height = 16;
        public const int ColumnPrimary = 0;
        public const int ColumnAccent = 1;

        public const uint RemnantBase = 0x3A78BEu;
        public const uint RemnantLight = 0x8FC3F0u;
        public const uint RemnantDark = 0x1F3F75u;
        public const uint RemnantAccentLight = 0xE8E4D8u;
        public const uint RemnantAccentDark = 0x1E1F1Du;

        public const uint CompactBase = 0xF2B418u;
        public const uint CompactLight = 0xFFEB99u;
        public const uint CompactDark = 0xA67A00u;
        public const uint CompactAccent = 0x1C1B19u;

        public const uint ClansBase = 0xB81E2Au;
        public const uint ClansLight = 0xE8657Au;
        public const uint ClansDark = 0x5A0C14u;
        public const uint ClansAccentChrome = 0xC4C8CCu;
        public const uint ClansAccentSoot = 0x262422u;

        public const uint NeutralPrimary = 0xC9C4B8u;
        public const uint NeutralAccent = 0x1E1F1Du;

        public static uint Primary(int faction, int shade)
        {
            switch (faction)
            {
                case TeamIndexPacking.FactionRemnant:
                    return ShadePick(shade, RemnantBase, RemnantLight, RemnantDark);
                case TeamIndexPacking.FactionCompact:
                    return ShadePick(shade, CompactBase, CompactLight, CompactDark);
                case TeamIndexPacking.FactionClans:
                    return ShadePick(shade, ClansBase, ClansLight, ClansDark);
                default:
                    return NeutralPrimary;
            }
        }

        public static uint Accent(int faction, int shade)
        {
            switch (faction)
            {
                case TeamIndexPacking.FactionRemnant:
                    return shade == TeamIndexPacking.ShadeLight ? RemnantAccentDark : RemnantAccentLight;
                case TeamIndexPacking.FactionCompact:
                    return CompactAccent;
                case TeamIndexPacking.FactionClans:
                    return shade == TeamIndexPacking.ShadeLight ? ClansAccentSoot : ClansAccentChrome;
                default:
                    return NeutralAccent;
            }
        }

        public static void FillRgba32(byte[] pixels)
        {
            int expected = Width * Height * 4;
            if (pixels.Length < expected)
            {
                throw new System.ArgumentException("Palette buffer must be at least 2x16 RGBA.", nameof(pixels));
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = 0;
            }

            for (int faction = 0; faction < 4; faction++)
            {
                for (int shade = 0; shade < 4; shade++)
                {
                    int row = TeamIndexPacking.PaletteRow(faction, shade);
                    WritePixel(pixels, ColumnPrimary, row, Primary(faction, shade));
                    WritePixel(pixels, ColumnAccent, row, Accent(faction, shade));
                }
            }
        }

        public static void UnpackRgb(uint hex, out byte r, out byte g, out byte b)
        {
            r = (byte)((hex >> 16) & 0xFFu);
            g = (byte)((hex >> 8) & 0xFFu);
            b = (byte)(hex & 0xFFu);
        }

        private static uint ShadePick(int shade, uint baseColor, uint light, uint dark)
        {
            if (shade == TeamIndexPacking.ShadeLight)
            {
                return light;
            }

            if (shade == TeamIndexPacking.ShadeDark || shade == TeamIndexPacking.ShadeFallback)
            {
                return dark;
            }

            return baseColor;
        }

        private static void WritePixel(byte[] pixels, int x, int y, uint hex)
        {
            UnpackRgb(hex, out byte r, out byte g, out byte b);
            int i = (y * Width + x) * 4;
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }
    }
}
