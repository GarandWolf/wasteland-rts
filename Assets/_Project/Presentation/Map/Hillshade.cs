#nullable disable
using System;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Horn hillshade. Azimuth 315, altitude 45, exaggeration applied to the
    /// slope only. Row 0 is south. The elevation samples are not modified.
    /// </summary>
    public static class Hillshade
    {
        public const double AzimuthDegrees = 315.0;
        public const double AltitudeDegrees = 45.0;
        public const double Exaggeration = 1.5;

        public static void Compute(
            int width,
            int height,
            ushort[] raw,
            int elevOffsetCm,
            int elevUnitCm,
            int extentXCm,
            int extentYCm,
            double exaggeration,
            float[] shade)
        {
            if (width < 1 || height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (raw == null || raw.Length != width * height)
            {
                throw new ArgumentException("Elevation length must be width * height.", nameof(raw));
            }

            if (shade == null || shade.Length != raw.Length)
            {
                throw new ArgumentException("Shade length must match the grid.", nameof(shade));
            }

            if (elevUnitCm < 1 || extentXCm < 1 || extentYCm < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(elevUnitCm));
            }

            double cellX = extentXCm / (double)width;
            double cellY = extentYCm / (double)height;
            double zenith = (90.0 - AltitudeDegrees) * Math.PI / 180.0;
            double azimuth = (360.0 - AzimuthDegrees + 90.0) * Math.PI / 180.0;
            double cosZenith = Math.Cos(zenith);
            double sinZenith = Math.Sin(zenith);
            int unit = elevUnitCm;
            int offset = elevOffsetCm;

            for (int row = 0; row < height; row++)
            {
                int south = row == 0 ? 0 : row - 1;
                int north = row == height - 1 ? height - 1 : row + 1;
                for (int col = 0; col < width; col++)
                {
                    int west = col == 0 ? 0 : col - 1;
                    int east = col == width - 1 ? width - 1 : col + 1;
                    double a = Elevation(raw, width, west, south, offset, unit);
                    double b = Elevation(raw, width, col, south, offset, unit);
                    double c = Elevation(raw, width, east, south, offset, unit);
                    double d = Elevation(raw, width, west, row, offset, unit);
                    double f = Elevation(raw, width, east, row, offset, unit);
                    double g = Elevation(raw, width, west, north, offset, unit);
                    double h = Elevation(raw, width, col, north, offset, unit);
                    double i = Elevation(raw, width, east, north, offset, unit);
                    double dzdx = ((c + 2.0 * f + i) - (a + 2.0 * d + g)) / (8.0 * cellX);
                    double dzdy = ((g + 2.0 * h + i) - (a + 2.0 * b + c)) / (8.0 * cellY);
                    double slope = Math.Atan(exaggeration * Math.Sqrt(dzdx * dzdx + dzdy * dzdy));
                    double aspect = Math.Atan2(dzdy, -dzdx);
                    if (aspect < 0.0)
                    {
                        aspect += 2.0 * Math.PI;
                    }

                    double lit = cosZenith * Math.Cos(slope) + sinZenith * Math.Sin(slope) * Math.Cos(azimuth - aspect);
                    if (lit < 0.0)
                    {
                        lit = 0.0;
                    }
                    else if (lit > 1.0)
                    {
                        lit = 1.0;
                    }

                    shade[row * width + col] = (float)lit;
                }
            }
        }

        static double Elevation(ushort[] raw, int width, int col, int row, int offset, int unit)
        {
            return (double)offset + raw[row * width + col] * (double)unit;
        }
    }
}
