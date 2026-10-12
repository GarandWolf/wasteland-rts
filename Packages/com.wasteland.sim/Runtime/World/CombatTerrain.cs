#nullable disable
using System;
using System.IO;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.World
{
    /// <summary>
    /// The single terrain grid (about 89 m). Elevation, slope, land cover,
    /// and ford all come from that grid. Cell lookup is
    /// floor((x - origin) * width / extent), never a rounded cell size.
    /// </summary>
    public sealed class CombatTerrain
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _originXCm;
        private readonly int _originYCm;
        private readonly int _extentXCm;
        private readonly int _extentYCm;
        private readonly int _elevOffsetCm;
        private readonly int _elevUnitCm;
        private readonly ushort[] _elevation;
        private readonly ushort[] _slope;
        private readonly byte[] _cover;
        private readonly byte[] _ford;

        public CombatTerrain(
            int width,
            int height,
            int originXCm,
            int originYCm,
            int extentXCm,
            int extentYCm,
            ushort[] elevation,
            ushort[] slope,
            byte[] cover,
            byte[] ford,
            int elevOffsetCm,
            int elevUnitCm)
        {
            if (width < 1 || height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (extentXCm < 1 || extentYCm < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(extentXCm));
            }

            int cells = width * height;
            if (elevation == null || elevation.Length != cells
                || slope == null || slope.Length != cells
                || cover == null || cover.Length != cells
                || ford == null)
            {
                throw new ArgumentException("Terrain arrays must cover width * height.");
            }

            _width = width;
            _height = height;
            _originXCm = originXCm;
            _originYCm = originYCm;
            _extentXCm = extentXCm;
            _extentYCm = extentYCm;
            _elevation = elevation;
            _slope = slope;
            _cover = cover;
            _ford = ford;
            _elevOffsetCm = elevOffsetCm;
            _elevUnitCm = elevUnitCm < 1 ? 1 : elevUnitCm;
        }

        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        public int ExtentXCm
        {
            get { return _extentXCm; }
        }

        public int ElevOffsetCm
        {
            get { return _elevOffsetCm; }
        }

        public int ElevUnitCm
        {
            get { return _elevUnitCm; }
        }

        public static CombatTerrain Read(byte[] terrainBlob)
        {
            if (terrainBlob == null)
            {
                throw new ArgumentNullException(nameof(terrainBlob));
            }

            WrmbFile file = WrmbFile.Parse(terrainBlob);
            WrmbDirEntry metaEntry = file.RequireSection("META");
            byte[] meta = file.SectionBytes(metaEntry);
            if (meta.Length < 300)
            {
                throw new InvalidDataException("META is too short to carry elevation encoding.");
            }

            BlobReader metaReader = new BlobReader(meta);
            metaReader.Seek(292);
            int elevOffsetCm = metaReader.I32();
            int elevUnitCm = metaReader.I32();
            if (elevUnitCm < 1)
            {
                throw new InvalidDataException("META.elevUnitCm must be >= 1.");
            }

            WrmbDirEntry gridEntry = file.RequireSection("GRID");
            if (gridEntry.ElementSize != 56)
            {
                throw new InvalidDataException("GRID elementSize " + gridEntry.ElementSize + " != 56.");
            }

            byte[] gridBytes = file.SectionBytes(gridEntry);
            GridDesc desc = default(GridDesc);
            bool found = false;
            BlobReader grids = new BlobReader(gridBytes);
            for (int i = 0; i < gridEntry.Count; i++)
            {
                byte t0 = grids.U8();
                byte t1 = grids.U8();
                byte t2 = grids.U8();
                byte t3 = grids.U8();
                string tag = new string(new[] { (char)t0, (char)t1, (char)t2, (char)t3 });
                grids.U8();
                grids.U8();
                grids.U16();
                int width = grids.I32();
                int height = grids.I32();
                int originX = grids.I32();
                int originY = grids.I32();
                int extentX = grids.I32();
                int extentY = grids.I32();
                grids.I32();
                grids.I32();
                grids.I32();
                grids.I32();
                grids.I32();
                grids.I32();
                if (tag == "MVEL")
                {
                    desc = new GridDesc(width, height, originX, originY, extentX, extentY);
                    found = true;
                }
            }

            if (!found)
            {
                throw new InvalidDataException("GRID has no MVEL descriptor.");
            }

            if (desc.Width < 1 || desc.Height < 1 || desc.ExtentX < 1 || desc.ExtentY < 1)
            {
                throw new InvalidDataException("MVEL GRID extent is empty.");
            }

            int cells = desc.Width * desc.Height;
            WrmbDirEntry slope = file.RequireSection("MVSL");
            WrmbDirEntry cover = file.RequireSection("MVLC");
            WrmbDirEntry ford = file.RequireSection("MVFD");
            WrmbDirEntry elev = file.RequireSection("MVEL");
            if (slope.Count != cells || cover.Count != cells || elev.Count != cells)
            {
                throw new InvalidDataException("Terrain section count does not match the MVEL grid.");
            }

            int fordBytes = (cells + 7) / 8;
            if (ford.Count != fordBytes)
            {
                throw new InvalidDataException("MVFD size does not match the MVEL grid bitset.");
            }

            byte[] slopeBytes = file.SectionBytes(slope);
            byte[] coverBytes = file.SectionBytes(cover);
            byte[] elevBytes = file.SectionBytes(elev);
            ushort[] slopeOut = new ushort[cells];
            ushort[] elevOut = new ushort[cells];
            BlobReader slopeReader = new BlobReader(slopeBytes);
            BlobReader elevReader = new BlobReader(elevBytes);
            for (int i = 0; i < cells; i++)
            {
                slopeOut[i] = slopeReader.U16();
                elevOut[i] = elevReader.U16();
                if (coverBytes[i] > Wrmb.WrmbCoverSnowIce)
                {
                    throw new InvalidDataException("Unknown land-cover class.");
                }
            }

            return new CombatTerrain(
                desc.Width,
                desc.Height,
                desc.OriginX,
                desc.OriginY,
                desc.ExtentX,
                desc.ExtentY,
                elevOut,
                slopeOut,
                coverBytes,
                file.SectionBytes(ford),
                elevOffsetCm,
                elevUnitCm);
        }

        public TerrainSample Sample(int xCm, int yCm)
        {
            int index = IndexOf(xCm, yCm);
            int cover = _cover[index];
            bool ford = (_ford[index >> 3] & (1 << (index & 7))) != 0;
            long elevationCm = (long)_elevOffsetCm + (long)_elevation[index] * _elevUnitCm;
            return new TerrainSample(
                (int)FloorDiv(elevationCm, 10),
                _slope[index],
                cover == Wrmb.WrmbCoverUrban,
                cover == Wrmb.WrmbCoverForest,
                ford);
        }

        public int IndexOf(int xCm, int yCm)
        {
            long col = FloorMulDiv((long)xCm - _originXCm, _width, _extentXCm);
            long row = FloorMulDiv((long)yCm - _originYCm, _height, _extentYCm);
            if (col < 0)
            {
                col = 0;
            }

            if (row < 0)
            {
                row = 0;
            }

            if (col >= _width)
            {
                col = _width - 1;
            }

            if (row >= _height)
            {
                row = _height - 1;
            }

            return (int)row * _width + (int)col;
        }

        private static long FloorDiv(long numer, long denom)
        {
            if (denom < 1)
            {
                return 0;
            }

            if (numer >= 0)
            {
                return numer / denom;
            }

            long mag = -numer;
            long q = mag / denom;
            long r = mag % denom;
            return r == 0 ? -q : -(q + 1);
        }

        private static long FloorMulDiv(long value, long numer, long denom)
        {
            if (denom < 1)
            {
                return 0;
            }

            if (value >= 0)
            {
                return value * numer / denom;
            }

            long mag = -value;
            long product = mag * numer;
            long q = product / denom;
            long r = product % denom;
            return r == 0 ? -q : -(q + 1);
        }

        private readonly struct GridDesc
        {
            public readonly int Width;
            public readonly int Height;
            public readonly int OriginX;
            public readonly int OriginY;
            public readonly int ExtentX;
            public readonly int ExtentY;

            public GridDesc(int width, int height, int originX, int originY, int extentX, int extentY)
            {
                Width = width;
                Height = height;
                OriginX = originX;
                OriginY = originY;
                ExtentX = extentX;
                ExtentY = extentY;
            }
        }
    }

    public readonly struct TerrainSample
    {
        public readonly int ElevationDm;
        public readonly int SlopePpt;
        public readonly bool Urban;
        public readonly bool Forest;
        public readonly bool Ford;

        public TerrainSample(int elevationDm, int slopePpt, bool urban, bool forest, bool ford)
        {
            ElevationDm = elevationDm;
            SlopePpt = slopePpt;
            Urban = urban;
            Forest = forest;
            Ford = ford;
        }
    }
}
