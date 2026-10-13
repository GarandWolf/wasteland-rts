#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Movement;
using Wasteland.Sim.World;

namespace Wasteland.Presentation
{
    public sealed class MapTown
    {
        public int Id;
        public string Name;
        public int XCm;
        public int YCm;
        public int TerritoryId;
        public int PopulationClass;
        public int RoadNodeId;
        public bool IsSeatTown;
        public bool IsScenarioHq;
    }

    public sealed class MapRoad
    {
        public int Id;
        public int FromNodeId;
        public int ToNodeId;
        public int Surface;
        public int[] XCm;
        public int[] YCm;
    }

    public sealed class MapTerritory
    {
        public int Id;
        public ulong StableKey;
        public int SeatTownId;
    }

    /// <summary>
    /// Towns, roads, water, and elevation for the tactical map. The container
    /// is <see cref="WrmbFile"/> / <see cref="WrmbMap"/> / <see cref="CombatTerrain"/>.
    /// Record fields are read from those section bytes.
    /// </summary>
    public sealed class MapDocument
    {
        public const int TownRecordSize = 64;
        public const int EdgeRecordSize = 64;
        public const int TerritoryRecordSize = 48;
        public const int GridRecordSize = 56;

        readonly ushort[] _elevation;
        readonly Dictionary<int, int> _nodeX = new Dictionary<int, int>();
        readonly Dictionary<int, int> _nodeY = new Dictionary<int, int>();

        MapDocument(
            WrmbMap map,
            CombatTerrain terrain,
            int originXCm,
            int originYCm,
            int extentXCm,
            int extentYCm,
            int elevOffsetCm,
            int elevUnitCm,
            ushort[] elevation,
            byte[] water,
            MapTown[] towns,
            MapRoad[] roads,
            MapTerritory[] territories,
            int[] landTerritory,
            int landWidth,
            int landHeight,
            int landOriginXCm,
            int landOriginYCm,
            int landExtentXCm,
            int landExtentYCm)
        {
            Map = map;
            Terrain = terrain;
            OriginXCm = originXCm;
            OriginYCm = originYCm;
            ExtentXCm = extentXCm;
            ExtentYCm = extentYCm;
            ElevOffsetCm = elevOffsetCm;
            ElevUnitCm = elevUnitCm;
            _elevation = elevation;
            Water = water;
            Towns = towns;
            Roads = roads;
            Territories = territories;
            LandTerritoryId = landTerritory;
            LandWidth = landWidth;
            LandHeight = landHeight;
            LandOriginXCm = landOriginXCm;
            LandOriginYCm = landOriginYCm;
            LandExtentXCm = landExtentXCm;
            LandExtentYCm = landExtentYCm;
            for (int i = 0; i < map.Roads.NodeCount; i++)
            {
                _nodeX[map.Roads.GetNodeId(i)] = map.Roads.GetNodeXCm(i);
                _nodeY[map.Roads.GetNodeId(i)] = map.Roads.GetNodeYCm(i);
            }
        }

        public WrmbMap Map { get; }

        public CombatTerrain Terrain { get; }

        public int OriginXCm { get; }

        public int OriginYCm { get; }

        public int ExtentXCm { get; }

        public int ExtentYCm { get; }

        public int ElevOffsetCm { get; }

        public int ElevUnitCm { get; }

        public int Width
        {
            get { return Terrain.Width; }
        }

        public int Height
        {
            get { return Terrain.Height; }
        }

        public byte[] Water { get; }

        public MapTown[] Towns { get; }

        public MapRoad[] Roads { get; }

        public MapTerritory[] Territories { get; }

        public int[] LandTerritoryId { get; }

        public int LandWidth { get; }

        public int LandHeight { get; }

        public int LandOriginXCm { get; }

        public int LandOriginYCm { get; }

        public int LandExtentXCm { get; }

        public int LandExtentYCm { get; }

        public static MapDocument Load(byte[] mainBlob, byte[] terrainBlob)
        {
            if (mainBlob == null)
            {
                throw new ArgumentNullException(nameof(mainBlob));
            }

            if (terrainBlob == null)
            {
                throw new ArgumentNullException(nameof(terrainBlob));
            }

            WrmbFile main = WrmbFile.Parse(mainBlob);
            WrmbMap map = new WrmbMap(
                main,
                Wrmb.ReadRoadGraph(main),
                Wrmb.ReadChokepoints(main),
                Wrmb.ReadTerritories(main));
            WrmbFile terrainFile = WrmbFile.Parse(terrainBlob);
            CombatTerrain terrain = CombatTerrain.Read(terrainBlob);
            GridDesc elevationGrid = ReadGrid(terrainFile, "MVEL");
            int offset;
            int unit;
            ReadElevationEncoding(terrainFile, out offset, out unit);
            ushort[] elevation = ReadU16Grid(terrainFile, "MVEL", elevationGrid.Width * elevationGrid.Height);
            byte[] water = ReadWater(terrainFile, elevationGrid.Width, elevationGrid.Height);
            MapTerritory[] territories = ReadTerritories(main);
            MapTown[] towns = ReadTowns(main, territories);
            MapRoad[] roads = ReadRoads(main, map.Roads);
            int landWidth = 0;
            int landHeight = 0;
            int landOx = 0;
            int landOy = 0;
            int landEx = 0;
            int landEy = 0;
            int[] land = Array.Empty<int>();
            if (main.TryGetSection("CELX", out WrmbDirEntry celx) && main.TryGetSection("GRID", out _))
            {
                GridDesc landGrid = ReadGrid(main, "CELX");
                landWidth = landGrid.Width;
                landHeight = landGrid.Height;
                landOx = landGrid.OriginXCm;
                landOy = landGrid.OriginYCm;
                landEx = landGrid.ExtentXCm;
                landEy = landGrid.ExtentYCm;
                land = ReadLand(main, celx, landWidth * landHeight);
            }

            return new MapDocument(
                map,
                terrain,
                elevationGrid.OriginXCm,
                elevationGrid.OriginYCm,
                elevationGrid.ExtentXCm,
                elevationGrid.ExtentYCm,
                offset,
                unit,
                elevation,
                water,
                towns,
                roads,
                territories,
                land,
                landWidth,
                landHeight,
                landOx,
                landOy,
                landEx,
                landEy);
        }

        public int CellIndex(int xCm, int yCm)
        {
            return MapSpace.CellIndex(xCm, yCm, OriginXCm, OriginYCm, Width, Height, ExtentXCm, ExtentYCm);
        }

        public int ElevationCmAt(int xCm, int yCm)
        {
            int index = CellIndex(xCm, yCm);
            return ElevOffsetCm + _elevation[index] * ElevUnitCm;
        }

        public ushort ElevationRawAt(int index)
        {
            return _elevation[index];
        }

        public int WaterAt(int xCm, int yCm)
        {
            return Water[CellIndex(xCm, yCm)];
        }

        public bool TryTownByName(string name, out MapTown town)
        {
            for (int i = 0; i < Towns.Length; i++)
            {
                if (Towns[i].Name == name)
                {
                    town = Towns[i];
                    return true;
                }
            }

            town = null;
            return false;
        }

        public bool TryNode(int nodeId, out int xCm, out int yCm)
        {
            if (_nodeX.TryGetValue(nodeId, out xCm) && _nodeY.TryGetValue(nodeId, out yCm))
            {
                return true;
            }

            xCm = 0;
            yCm = 0;
            return false;
        }

        public void BindScenarioSeats(ulong[] territoryKeys)
        {
            for (int i = 0; i < Towns.Length; i++)
            {
                Towns[i].IsScenarioHq = false;
            }

            if (territoryKeys == null)
            {
                return;
            }

            for (int k = 0; k < territoryKeys.Length; k++)
            {
                int seatTown = -1;
                for (int t = 0; t < Territories.Length; t++)
                {
                    if (Territories[t].StableKey == territoryKeys[k])
                    {
                        seatTown = Territories[t].SeatTownId;
                        break;
                    }
                }

                if (seatTown < 0)
                {
                    continue;
                }

                for (int i = 0; i < Towns.Length; i++)
                {
                    if (Towns[i].Id == seatTown)
                    {
                        Towns[i].IsScenarioHq = true;
                    }
                }
            }
        }

        public int LandTerritoryAt(int xCm, int yCm)
        {
            if (LandWidth < 1 || LandHeight < 1 || LandTerritoryId.Length == 0)
            {
                return 0;
            }

            int index = MapSpace.CellIndex(
                xCm,
                yCm,
                LandOriginXCm,
                LandOriginYCm,
                LandWidth,
                LandHeight,
                LandExtentXCm,
                LandExtentYCm);
            return LandTerritoryId[index];
        }

        static void ReadElevationEncoding(WrmbFile file, out int offsetCm, out int unitCm)
        {
            byte[] meta = file.SectionBytes(file.RequireSection("META"));
            if (meta.Length < 300)
            {
                throw new InvalidDataException("META is too short to carry elevation encoding.");
            }

            offsetCm = ReadI32(meta, 292);
            unitCm = ReadI32(meta, 296);
            if (unitCm < 1)
            {
                throw new InvalidDataException("META.elevUnitCm must be >= 1.");
            }
        }

        static GridDesc ReadGrid(WrmbFile file, string tag)
        {
            WrmbDirEntry grid = file.RequireSection("GRID");
            if (grid.ElementSize != GridRecordSize)
            {
                throw new InvalidDataException("GRID elementSize " + grid.ElementSize + " != " + GridRecordSize + ".");
            }

            byte[] data = file.SectionBytes(grid);
            for (int i = 0; i < grid.Count; i++)
            {
                int at = i * GridRecordSize;
                string found = Encoding.ASCII.GetString(data, at, 4);
                if (found != tag)
                {
                    continue;
                }

                return new GridDesc(
                    ReadI32(data, at + 8),
                    ReadI32(data, at + 12),
                    ReadI32(data, at + 16),
                    ReadI32(data, at + 20),
                    ReadI32(data, at + 24),
                    ReadI32(data, at + 28));
            }

            throw new InvalidDataException("GRID has no " + tag + " descriptor.");
        }

        static ushort[] ReadU16Grid(WrmbFile file, string tag, int cells)
        {
            WrmbDirEntry entry = file.RequireSection(tag);
            if (entry.Count != cells)
            {
                throw new InvalidDataException(tag + " count does not match its GRID.");
            }

            byte[] data = file.SectionBytes(entry);
            ushort[] values = new ushort[cells];
            for (int i = 0; i < cells; i++)
            {
                values[i] = ReadU16(data, i * 2);
            }

            return values;
        }

        static byte[] ReadWater(WrmbFile file, int width, int height)
        {
            WrmbDirEntry entry = file.RequireSection("WTRA");
            byte[] data = file.SectionBytes(entry);
            if (data.Length < 16)
            {
                throw new InvalidDataException("WTRA is shorter than its header.");
            }

            int version = (int)ReadU32(data, 0);
            int w = (int)ReadU32(data, 4);
            int h = (int)ReadU32(data, 8);
            if (version != 1 && version != 2)
            {
                throw new InvalidDataException("WTRA version " + version + " is not 1 or 2.");
            }

            if (w != width || h != height)
            {
                throw new InvalidDataException("WTRA dimensions do not match the movement grid.");
            }

            int cells = width * height;
            if (data.Length < 16 + cells)
            {
                throw new InvalidDataException("WTRA marks are truncated.");
            }

            int maxCode = version == 1 ? 3 : 4;
            byte[] codes = new byte[cells];
            for (int i = 0; i < cells; i++)
            {
                byte code = data[16 + i];
                if (code > maxCode)
                {
                    throw new InvalidDataException("WTRA code " + code + " is out of range.");
                }

                codes[i] = code;
            }

            return codes;
        }

        static MapTerritory[] ReadTerritories(WrmbFile file)
        {
            if (!file.TryGetSection("TERR", out WrmbDirEntry terr))
            {
                return Array.Empty<MapTerritory>();
            }

            if (terr.ElementSize < TerritoryRecordSize)
            {
                throw new InvalidDataException("TERR elementSize " + terr.ElementSize + " is shorter than " + TerritoryRecordSize + ".");
            }

            byte[] data = file.SectionBytes(terr);
            List<MapTerritory> list = new List<MapTerritory>();
            for (int i = 0; i < terr.Count; i++)
            {
                int at = i * terr.ElementSize;
                int id = ReadI32(data, at);
                ulong key = ReadU64(data, at + 40);
                if (key == 0UL && id == 0)
                {
                    continue;
                }

                list.Add(new MapTerritory
                {
                    Id = id,
                    SeatTownId = ReadI32(data, at + 4),
                    StableKey = key
                });
            }

            return list.ToArray();
        }

        static MapTown[] ReadTowns(WrmbFile file, MapTerritory[] territories)
        {
            WrmbDirEntry towns = file.RequireSection("TOWN");
            if (towns.ElementSize != TownRecordSize)
            {
                throw new InvalidDataException("TOWN elementSize must be " + TownRecordSize + ".");
            }

            byte[] data = file.SectionBytes(towns);
            byte[] names = file.SectionBytes(file.RequireSection("STRP"));
            MapTown[] result = new MapTown[towns.Count];
            for (int i = 0; i < towns.Count; i++)
            {
                int at = i * TownRecordSize;
                int id = ReadI32(data, at);
                bool seat = false;
                for (int t = 0; t < territories.Length; t++)
                {
                    if (territories[t].SeatTownId == id && territories[t].StableKey != 0UL)
                    {
                        seat = true;
                        break;
                    }
                }

                int nameOff = (int)ReadU32(data, at + 52);
                int nameLen = (int)ReadU32(data, at + 56);
                result[i] = new MapTown
                {
                    Id = id,
                    XCm = ReadI32(data, at + 4),
                    YCm = ReadI32(data, at + 8),
                    TerritoryId = ReadI32(data, at + 12),
                    PopulationClass = (sbyte)data[at + 34],
                    RoadNodeId = ReadI32(data, at + 28),
                    Name = ReadUtf8(names, nameOff, nameLen),
                    IsSeatTown = seat
                };
            }

            return result;
        }

        static MapRoad[] ReadRoads(WrmbFile file, RoadGraph graph)
        {
            WrmbDirEntry edges = file.RequireSection("REDG");
            if (edges.ElementSize != EdgeRecordSize)
            {
                throw new InvalidDataException("REDG elementSize must be " + EdgeRecordSize + ".");
            }

            byte[] data = file.SectionBytes(edges);
            byte[] points = file.TryGetSection("PTSP", out WrmbDirEntry ptsp)
                ? file.SectionBytes(ptsp)
                : Array.Empty<byte>();
            MapRoad[] roads = new MapRoad[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                int at = i * EdgeRecordSize;
                int fromId = ReadI32(data, at + 4);
                int toId = ReadI32(data, at + 8);
                int pointOff = (int)ReadU32(data, at + 48);
                int pointCount = (int)ReadU32(data, at + 52);
                int[] xs;
                int[] ys;
                if (pointCount >= 2)
                {
                    xs = new int[pointCount];
                    ys = new int[pointCount];
                    for (int p = 0; p < pointCount; p++)
                    {
                        int byteAt = (pointOff + p) * 8;
                        if (byteAt < 0 || byteAt + 8 > points.Length)
                        {
                            throw new InvalidDataException("REDG geometry is outside PTSP.");
                        }

                        xs[p] = ReadI32(points, byteAt);
                        ys[p] = ReadI32(points, byteAt + 4);
                    }
                }
                else
                {
                    if (!graph.TryGetNodeIndex(fromId, out int fromIndex) || !graph.TryGetNodeIndex(toId, out int toIndex))
                    {
                        throw new InvalidDataException("REDG endpoint is not a road node.");
                    }

                    xs = new[] { graph.GetNodeXCm(fromIndex), graph.GetNodeXCm(toIndex) };
                    ys = new[] { graph.GetNodeYCm(fromIndex), graph.GetNodeYCm(toIndex) };
                }

                roads[i] = new MapRoad
                {
                    Id = ReadI32(data, at),
                    FromNodeId = fromId,
                    ToNodeId = toId,
                    Surface = (sbyte)data[at + 41],
                    XCm = xs,
                    YCm = ys
                };
            }

            return roads;
        }

        static int[] ReadLand(WrmbFile file, WrmbDirEntry celx, int cells)
        {
            if (celx.Count != cells)
            {
                throw new InvalidDataException("CELX count does not match its GRID.");
            }

            byte[] data = file.SectionBytes(celx);
            int[] ids = new int[cells];
            for (int i = 0; i < cells; i++)
            {
                ids[i] = ReadI32(data, i * celx.ElementSize);
            }

            return ids;
        }

        static string ReadUtf8(byte[] strp, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > strp.Length)
            {
                throw new InvalidDataException("STRP span out of bounds.");
            }

            return Encoding.UTF8.GetString(strp, offset, length);
        }

        static int ReadI32(byte[] data, int offset)
        {
            return (int)ReadU32(data, offset);
        }

        static uint ReadU32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        static ushort ReadU16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        static ulong ReadU64(byte[] data, int offset)
        {
            ulong lo = ReadU32(data, offset);
            ulong hi = ReadU32(data, offset + 4);
            return lo | (hi << 32);
        }

        readonly struct GridDesc
        {
            public readonly int Width;
            public readonly int Height;
            public readonly int OriginXCm;
            public readonly int OriginYCm;
            public readonly int ExtentXCm;
            public readonly int ExtentYCm;

            public GridDesc(int width, int height, int originXCm, int originYCm, int extentXCm, int extentYCm)
            {
                Width = width;
                Height = height;
                OriginXCm = originXCm;
                OriginYCm = originYCm;
                ExtentXCm = extentXCm;
                ExtentYCm = extentYCm;
            }
        }
    }
}
