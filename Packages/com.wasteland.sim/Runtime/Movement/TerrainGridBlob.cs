using System;
using System.IO;

namespace Wasteland.Sim.Movement
{
    /// <summary>Little-endian reader/writer for terrain-grid schema v1. See docs/formats/terrain-grid-v1.md.</summary>
    public static class TerrainGridBlob
    {
        public const string Magic = "WTG1";

        public static byte[] Write(TerrainGrid grid)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            int cells = grid.CellCount;
            BlobWriter writer = new BlobWriter(MovementConfig.BlobHeaderSize + cells * MovementConfig.TerrainCellStride);
            writer.Magic(Magic);
            writer.U16(MovementConfig.TerrainSchemaVersion);
            writer.U16(MovementConfig.BlobHeaderSize);
            writer.U32((uint)grid.Width);
            writer.U32((uint)grid.Height);
            writer.U32((uint)grid.CellSizeCm);
            writer.I32(grid.OriginXCm);
            writer.I32(grid.OriginYCm);
            writer.U32(0);

            for (int i = 0; i < cells; i++)
            {
                writer.U16(grid.SlopePerMille[i]);
                writer.U8(grid.Cover[i]);
                writer.U8(0);
            }

            return writer.ToArray();
        }

        public static TerrainGrid Read(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            BlobReader reader = new BlobReader(data);
            reader.ExpectMagic(Magic);
            ushort schema = reader.U16();
            if (schema != MovementConfig.TerrainSchemaVersion)
            {
                throw new InvalidDataException("Unsupported terrain-grid schema " + schema + ".");
            }

            ushort headerSize = reader.U16();
            if (headerSize < MovementConfig.BlobHeaderSize)
            {
                throw new InvalidDataException("Terrain-grid header is truncated.");
            }

            int width = checked((int)reader.U32());
            int height = checked((int)reader.U32());
            int cellSize = checked((int)reader.U32());
            int originX = reader.I32();
            int originY = reader.I32();
            reader.U32();

            if (width < 1 || height < 1
                || width > MovementConfig.MaxGridDimension
                || height > MovementConfig.MaxGridDimension)
            {
                throw new InvalidDataException("Terrain-grid dimensions out of range.");
            }

            if (cellSize < 1)
            {
                throw new InvalidDataException("CellSizeCm must be >= 1.");
            }

            long cellCountLong = (long)width * height;
            if (cellCountLong > int.MaxValue)
            {
                throw new InvalidDataException("Terrain-grid cell count overflows int.");
            }

            int cellCount = (int)cellCountLong;
            reader.Seek(headerSize);
            long expected = (long)headerSize + (long)cellCount * MovementConfig.TerrainCellStride;
            if (data.Length != expected)
            {
                throw new InvalidDataException("Terrain-grid blob length does not match header.");
            }

            ushort[] slope = new ushort[cellCount];
            byte[] cover = new byte[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                slope[i] = reader.U16();
                byte land = reader.U8();
                reader.U8();
                if (land > (byte)LandCover.Ford)
                {
                    throw new InvalidDataException("Unknown land-cover class.");
                }

                cover[i] = land;
            }

            return new TerrainGrid(width, height, cellSize, originX, originY, slope, cover);
        }
    }
}
