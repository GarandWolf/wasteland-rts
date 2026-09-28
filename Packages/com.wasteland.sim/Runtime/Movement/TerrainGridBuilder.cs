using System;

namespace Wasteland.Sim.Movement
{
    /// <summary>In-memory authoring helper for synthetic and pipeline terrain blobs.</summary>
    public sealed class TerrainGridBuilder
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _cellSizeCm;
        private readonly int _originXCm;
        private readonly int _originYCm;
        private readonly ushort[] _slope;
        private readonly byte[] _cover;

        public TerrainGridBuilder(int width, int height, int cellSizeCm = MovementConfig.DefaultCellSizeCm, int originXCm = 0, int originYCm = 0)
        {
            if (width < 1 || height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (cellSizeCm < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            }

            _width = width;
            _height = height;
            _cellSizeCm = cellSizeCm;
            _originXCm = originXCm;
            _originYCm = originYCm;
            _slope = new ushort[width * height];
            _cover = new byte[width * height];
        }

        public TerrainGridBuilder Fill(int slopePerMille, LandCover cover)
        {
            if (slopePerMille < 0 || slopePerMille > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(slopePerMille));
            }

            byte c = (byte)cover;
            ushort s = (ushort)slopePerMille;
            for (int i = 0; i < _slope.Length; i++)
            {
                _slope[i] = s;
                _cover[i] = c;
            }

            return this;
        }

        public TerrainGridBuilder SetCell(int x, int y, int slopePerMille, LandCover cover)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (slopePerMille < 0 || slopePerMille > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(slopePerMille));
            }

            int i = y * _width + x;
            _slope[i] = (ushort)slopePerMille;
            _cover[i] = (byte)cover;
            return this;
        }

        public TerrainGridBuilder FillRect(int x0, int y0, int x1Inclusive, int y1Inclusive, int slopePerMille, LandCover cover)
        {
            for (int y = y0; y <= y1Inclusive; y++)
            {
                for (int x = x0; x <= x1Inclusive; x++)
                {
                    SetCell(x, y, slopePerMille, cover);
                }
            }

            return this;
        }

        public byte[] ToBlob()
        {
            TerrainGrid grid = new TerrainGrid(_width, _height, _cellSizeCm, _originXCm, _originYCm, _slope, _cover);
            return TerrainGridBlob.Write(grid);
        }
    }
}
