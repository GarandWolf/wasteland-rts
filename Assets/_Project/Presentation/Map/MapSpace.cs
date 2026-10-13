#nullable disable
using System;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Map centimetres to Unity metres. The origin is the south-west corner.
    /// East is +X and north is +Z. Nothing is mirrored.
    /// </summary>
    public static class MapSpace
    {
        public const double CentimetresPerMetre = 100.0;

        public static double Metres(int centimetres)
        {
            return centimetres / CentimetresPerMetre;
        }

        /// <summary>
        /// <c>(x/100, elevationCm/100, y/100)</c>. Y in the file is north.
        /// </summary>
        public static void ToUnity(int xCm, int elevationCm, int yCm, out double x, out double y, out double z)
        {
            x = Metres(xCm);
            y = Metres(elevationCm);
            z = Metres(yCm);
        }

        /// <summary>
        /// <c>floor((position - origin) * cells / extent)</c> in 64-bit.
        /// Never divides by a rounded cell size.
        /// </summary>
        public static int Column(int positionCm, int originCm, int cells, int extentCm)
        {
            if (cells < 1 || extentCm < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cells));
            }

            long value = (long)positionCm - originCm;
            long index = FloorMulDiv(value, cells, extentCm);
            if (index < 0)
            {
                return 0;
            }

            if (index >= cells)
            {
                return cells - 1;
            }

            return (int)index;
        }

        public static int CellIndex(int xCm, int yCm, int originXCm, int originYCm, int width, int height, int extentXCm, int extentYCm)
        {
            int col = Column(xCm, originXCm, width, extentXCm);
            int row = Column(yCm, originYCm, height, extentYCm);
            return row * width + col;
        }

        /// <summary>Nearest centimetre. Presentation clicks are metres; the sim stays in integers.</summary>
        public static int ToCentimetres(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres))
            {
                return 0;
            }

            double scaled = metres * CentimetresPerMetre;
            if (scaled >= int.MaxValue)
            {
                return int.MaxValue;
            }

            if (scaled <= int.MinValue)
            {
                return int.MinValue;
            }

            if (scaled >= 0.0)
            {
                return (int)(scaled + 0.5);
            }

            return (int)(scaled - 0.5);
        }

        /// <summary>Clamp a centimetre into <c>[origin, origin + extent]</c>.</summary>
        public static int ClampCm(int valueCm, int originCm, int extentCm)
        {
            int end = originCm + extentCm;
            if (valueCm < originCm)
            {
                return originCm;
            }

            if (valueCm > end)
            {
                return end;
            }

            return valueCm;
        }

        /// <summary>
        /// Click-to-ground. Metres become centimetres, the point is clamped to the
        /// map, and the cell is <see cref="Column"/> (same lookup, edges clamp).
        /// </summary>
        public static void PickGround(
            double eastMetres,
            double northMetres,
            int originXCm,
            int originYCm,
            int width,
            int height,
            int extentXCm,
            int extentYCm,
            out int xCm,
            out int yCm,
            out int column,
            out int row)
        {
            xCm = ClampCm(ToCentimetres(eastMetres), originXCm, extentXCm);
            yCm = ClampCm(ToCentimetres(northMetres), originYCm, extentYCm);
            column = Column(xCm, originXCm, width, extentXCm);
            row = Column(yCm, originYCm, height, extentYCm);
        }

        /// <summary>Cell centre, <c>((2*col+1) * extent) / (2 * cells)</c>.</summary>
        public static int CellCenterCm(int column, int originCm, int cells, int extentCm)
        {
            if (cells < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cells));
            }

            return originCm + (int)(((long)(2 * column + 1) * extentCm) / (2L * cells));
        }

        public static float WorldUnitsPerPixel(float orthographicSize, int screenHeight)
        {
            int height = screenHeight < 1 ? 1 : screenHeight;
            return (orthographicSize * 2f) / height;
        }

        public static float WorldWidth(float screenPixels, float orthographicSize, int screenHeight)
        {
            return screenPixels * WorldUnitsPerPixel(orthographicSize, screenHeight);
        }

        static long FloorMulDiv(long value, long numer, long denom)
        {
            if (value >= 0)
            {
                return value * numer / denom;
            }

            long product = -value * numer;
            long quotient = product / denom;
            long remainder = product % denom;
            return remainder == 0 ? -quotient : -(quotient + 1);
        }
    }
}
