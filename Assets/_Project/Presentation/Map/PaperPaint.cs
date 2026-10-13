#nullable disable
namespace Wasteland.Presentation
{
    /// <summary>Paper, hillshade multiply, water fills, and territory wash.</summary>
    public static class PaperPaint
    {
        public const int Paper = 0xE6DDC6;
        public const int WaterFill = 0xAFC3C2;
        public const int WaterEdge = 0x5E7F83;
        public const int FordGravel = 0xC8C0A8;
        public const float HillshadeWeight = 0.20f;
        public const float FordableAlpha = 0.70f;
        public const float WetlandAlpha = 0.40f;
        public const float WashAlpha = 0.35f;
        public const int BlockingEdgePx = 2;
        public const int FordableEdgePx = 1;

        public static Rgb MultiplyHillshade(Rgb paper, float shade)
        {
            float weight = shade;
            if (weight < 0f)
            {
                weight = 0f;
            }
            else if (weight > 1f)
            {
                weight = 1f;
            }

            float scale = (1f - HillshadeWeight) + HillshadeWeight * weight;
            return new Rgb(
                Scale(paper.R, scale),
                Scale(paper.G, scale),
                Scale(paper.B, scale));
        }

        public static Rgb PaintWater(Rgb shaded, int code)
        {
            if (code == 1)
            {
                return Rgb.FromHex(WaterFill);
            }

            if (code == 2)
            {
                return Over(shaded, Rgb.FromHex(WaterFill), FordableAlpha);
            }

            if (code == 3)
            {
                return Rgb.FromHex(FordGravel);
            }

            if (code == 4)
            {
                return Over(shaded, Rgb.FromHex(WaterFill), WetlandAlpha);
            }

            return shaded;
        }

        public static Rgb PaintWash(Rgb under, Rgb owner, float alpha)
        {
            return Over(under, owner, alpha);
        }

        public static int EdgePx(int waterCode)
        {
            if (waterCode == 1)
            {
                return BlockingEdgePx;
            }

            if (waterCode == 2)
            {
                return FordableEdgePx;
            }

            return 0;
        }

        public static Rgb Over(Rgb under, Rgb fill, float alpha)
        {
            float a = alpha;
            if (a < 0f)
            {
                a = 0f;
            }
            else if (a > 1f)
            {
                a = 1f;
            }

            float keep = 1f - a;
            return new Rgb(
                (byte)(fill.R * a + under.R * keep + 0.5f),
                (byte)(fill.G * a + under.G * keep + 0.5f),
                (byte)(fill.B * a + under.B * keep + 0.5f));
        }

        static byte Scale(byte channel, float scale)
        {
            float value = channel * scale;
            if (value < 0f)
            {
                return 0;
            }

            if (value > 255f)
            {
                return 255;
            }

            return (byte)(value + 0.5f);
        }
    }

    public readonly struct Rgb
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static Rgb FromHex(int hex)
        {
            return new Rgb(
                (byte)((hex >> 16) & 255),
                (byte)((hex >> 8) & 255),
                (byte)(hex & 255));
        }
    }
}
