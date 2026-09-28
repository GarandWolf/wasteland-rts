using System;

namespace Wasteland.Sim.Movement
{
    /// <summary>Runtime terrain grid. Cells are slope magnitude + land cover only.</summary>
    public sealed class TerrainGrid
    {
        internal readonly ushort[] SlopePerMille;
        internal readonly byte[] Cover;
        public readonly int OriginXCm;
        public readonly int OriginYCm;

        internal TerrainGrid(
            int width,
            int height,
            int cellSizeCm,
            int originXCm,
            int originYCm,
            ushort[] slopePerMille,
            byte[] cover)
        {
            Width = width;
            Height = height;
            CellSizeCm = cellSizeCm;
            OriginXCm = originXCm;
            OriginYCm = originYCm;
            SlopePerMille = slopePerMille;
            Cover = cover;
        }

        public int Width { get; }

        public int Height { get; }

        public int CellSizeCm { get; }

        public int CellCount
        {
            get { return Width * Height; }
        }

        public int Index(int x, int y)
        {
            return y * Width + x;
        }

        public int SlopeAt(int index)
        {
            return SlopePerMille[index];
        }

        public LandCover CoverAt(int index)
        {
            return (LandCover)Cover[index];
        }

        public bool InBounds(int x, int y)
        {
            return (uint)x < (uint)Width && (uint)y < (uint)Height;
        }

        public bool TryWorldToCell(int xCm, int yCm, out int cellX, out int cellY)
        {
            cellX = FloorDiv(xCm - OriginXCm, CellSizeCm);
            cellY = FloorDiv(yCm - OriginYCm, CellSizeCm);
            return InBounds(cellX, cellY);
        }

        public int CellCenterXCm(int cellX)
        {
            return OriginXCm + cellX * CellSizeCm + CellSizeCm / 2;
        }

        public int CellCenterYCm(int cellY)
        {
            return OriginYCm + cellY * CellSizeCm + CellSizeCm / 2;
        }

        public static int FloorDiv(int a, int b)
        {
            if (b <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(b));
            }

            int q = a / b;
            int r = a % b;
            if (r != 0 && a < 0)
            {
                q--;
            }

            return q;
        }
    }
}
