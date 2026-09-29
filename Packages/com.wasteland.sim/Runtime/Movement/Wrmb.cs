using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// Sim-facing WRMB reader/writer. Roads come from the map/AI file (RNOD/REDG);
    /// terrain comes from a separate WRMB file (META + MVSL/MVLC/MVFD). Unknown
    /// directory tags are skipped. formatVersion 0 and 1 are accepted; v1 requires
    /// <see cref="WrmbFile.FlagV1Final"/> and a WTRA section on terrain files
    /// (art-only — the sim does not read its bytes). Layout constants live in
    /// <see cref="WrmbFile"/>.
    /// </summary>
    public static class Wrmb
    {
        /// <summary>Synthetic fixture META (no <c>fileKind</c>). The writer still emits this size.</summary>
        public const int MetaElementSize = 288;

        /// <summary>Pipeline META: <c>fileKind</c> immediately after <c>schemaVersion</c>.</summary>
        public const int PipelineMetaElementSize = 376;

        public const int RnodElementSize = 24;
        public const int RedgElementSize = 64;
        public const int ChokElementSizeV0 = 80;
        public const int ChokElementSizeV1 = 88;
        public const int TerrElementSizeV0 = 40;
        public const int TerrElementSizeV1 = 48;
        public const byte NoDefenseGroup = 255;

        public const byte WrmbCoverNodata = 0;
        public const byte WrmbCoverField = 1;
        public const byte WrmbCoverScrub = 2;
        public const byte WrmbCoverUrban = 3;
        public const byte WrmbCoverForest = 4;
        public const byte WrmbCoverMarsh = 5;
        public const byte WrmbCoverWater = 6;
        public const byte WrmbCoverSnowIce = 7;

        public const byte WrmbNodeFlagBridgeHead = 2;
        public const ushort WrmbEdgeFlagBridgeSpan = 2;

        public static RoadGraph ReadRoadGraph(byte[] mapBlob)
        {
            return ReadRoadGraph(WrmbFile.Parse(mapBlob));
        }

        public static RoadGraph ReadRoadGraph(WrmbFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            WrmbDirEntry metaEntry = file.RequireSection("META");
            CheckMetaElementSize(metaEntry.ElementSize);
            if (metaEntry.Count < 1)
            {
                throw new InvalidDataException("WRMB META is empty.");
            }

            WrmbMeta meta = ReadMeta(file.SectionBytes(metaEntry));
            WrmbDirEntry rnod = file.RequireSection("RNOD");
            CheckElementSize("RNOD", rnod.ElementSize, RnodElementSize);
            WrmbDirEntry redg = file.RequireSection("REDG");
            CheckElementSize("REDG", redg.ElementSize, RedgElementSize);

            if (meta.RoadNodeCount != rnod.Count)
            {
                throw new InvalidDataException("META.roadNodeCount does not match RNOD.");
            }

            if (meta.RoadEdgeCount != redg.Count)
            {
                throw new InvalidDataException("META.roadEdgeCount does not match REDG.");
            }

            if (rnod.Count > MovementConfig.MaxRoadNodes)
            {
                throw new InvalidDataException("Road-graph node count out of range.");
            }

            if (redg.Count > MovementConfig.MaxRoadEdges)
            {
                throw new InvalidDataException("Road-graph edge count out of range.");
            }

            int nodeCount = rnod.Count;
            int[] nodeId = new int[nodeCount];
            int[] nodeX = new int[nodeCount];
            int[] nodeY = new int[nodeCount];
            ushort[] nodeFlags = new ushort[nodeCount];
            ushort[] nodeTerritory = new ushort[nodeCount];
            BlobReader nodes = new BlobReader(file.SectionBytes(rnod));
            int previousId = -1;
            bool havePrevious = false;
            for (int i = 0; i < nodeCount; i++)
            {
                int id = nodes.I32();
                int x = nodes.I32();
                int y = nodes.I32();
                nodes.I32();
                byte flags = nodes.U8();
                nodes.I8();
                nodeTerritory[i] = nodes.U16();
                nodes.I32();
                if (havePrevious && id <= previousId)
                {
                    throw new InvalidDataException("Road nodes must be sorted by unique id.");
                }

                nodeId[i] = id;
                nodeX[i] = x;
                nodeY[i] = y;
                nodeFlags[i] = MapNodeFlags(flags);
                previousId = id;
                havePrevious = true;
            }

            int edgeCount = redg.Count;
            int[] edgeId = new int[edgeCount];
            int[] edgeFrom = new int[edgeCount];
            int[] edgeTo = new int[edgeCount];
            int[] edgeLength = new int[edgeCount];
            byte[] edgeTier = new byte[edgeCount];
            byte[] edgeSurface = new byte[edgeCount];
            ushort[] edgeFlags = new ushort[edgeCount];
            BlobReader edges = new BlobReader(file.SectionBytes(redg));
            havePrevious = false;
            previousId = -1;
            for (int i = 0; i < edgeCount; i++)
            {
                int id = edges.I32();
                int a = edges.I32();
                int b = edges.I32();
                int length = edges.I32();
                edges.I32();
                edges.I32();
                edges.I32();
                edges.I32();
                edges.I32();
                edges.I32();
                edges.I8();
                sbyte surface = edges.I8();
                edges.I8();
                sbyte highway = edges.I8();
                ushort wrmbFlags = edges.U16();
                edges.Skip(2);
                edges.U32();
                edges.U32();
                edges.U32();
                edges.U32();

                if (havePrevious && id <= previousId)
                {
                    throw new InvalidDataException("Road edges must be sorted by unique id.");
                }

                if (length <= 0)
                {
                    throw new InvalidDataException("Edge length must be > 0.");
                }

                if (a == b)
                {
                    throw new InvalidDataException("Self-loop road edge.");
                }

                int fromIndex = Array.BinarySearch(nodeId, a);
                int toIndex = Array.BinarySearch(nodeId, b);
                if (fromIndex < 0 || toIndex < 0)
                {
                    throw new InvalidDataException("Edge endpoint is not a node.");
                }

                ushort simFlags = MapEdgeFlags(wrmbFlags);
                bool span = (simFlags & (ushort)RoadEdgeFlags.BridgeSpan) != 0;
                if (span)
                {
                    bool fromHead = (nodeFlags[fromIndex] & (ushort)RoadNodeFlags.BridgeHead) != 0;
                    bool toHead = (nodeFlags[toIndex] & (ushort)RoadNodeFlags.BridgeHead) != 0;
                    if (!fromHead || !toHead)
                    {
                        throw new InvalidDataException("Bridge span must join two bridge-head nodes.");
                    }
                }

                if (highway < 0 || highway > 7)
                {
                    throw new InvalidDataException("Unknown highwayClass.");
                }

                if (surface < 0 || surface > 2)
                {
                    throw new InvalidDataException("Unknown road surface.");
                }

                edgeId[i] = id;
                edgeFrom[i] = fromIndex;
                edgeTo[i] = toIndex;
                edgeLength[i] = length;
                edgeTier[i] = MapHighwayClass(highway);
                edgeSurface[i] = MapSurface(surface, span);
                edgeFlags[i] = simFlags;
                previousId = id;
                havePrevious = true;
            }

            RoadGraph.BuildAdjacency(
                nodeCount,
                edgeFrom,
                edgeTo,
                edgeId,
                nodeId,
                out int[] adjOffset,
                out int[] adjNeighbor,
                out int[] adjEdge);

            return new RoadGraph(
                nodeId,
                nodeX,
                nodeY,
                nodeFlags,
                nodeTerritory,
                edgeId,
                edgeFrom,
                edgeTo,
                edgeLength,
                edgeTier,
                edgeSurface,
                edgeFlags,
                adjOffset,
                adjNeighbor,
                adjEdge,
                0,
                0);
        }

        public static TerrainGrid ReadTerrain(byte[] terrainBlob)
        {
            return ReadTerrain(WrmbFile.Parse(terrainBlob));
        }

        public static TerrainGrid ReadTerrain(WrmbFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            WrmbDirEntry metaEntry = file.RequireSection("META");
            CheckMetaElementSize(metaEntry.ElementSize);
            if (metaEntry.Count < 1)
            {
                throw new InvalidDataException("WRMB META is empty.");
            }

            WrmbMeta meta = ReadMeta(file.SectionBytes(metaEntry));
            if (meta.CellSizeCm < 1)
            {
                throw new InvalidDataException("META.cellSizeCm must be >= 1.");
            }

            WrmbDirEntry slope = file.RequireSection("MVSL");
            CheckElementSize("MVSL", slope.ElementSize, 2);
            WrmbDirEntry cover = file.RequireSection("MVLC");
            CheckElementSize("MVLC", cover.ElementSize, 1);
            WrmbDirEntry ford = file.RequireSection("MVFD");
            CheckElementSize("MVFD", ford.ElementSize, 1);
            if (file.FileFormatVersion >= WrmbFile.FormatVersionV1)
            {
                WrmbDirEntry wtra = file.RequireSection("WTRA");
                CheckElementSize("WTRA", wtra.ElementSize, 1);
            }

            int width = meta.MoveWidth;
            int height = meta.MoveHeight;
            if (width < 1 || height < 1
                || width > MovementConfig.MaxGridDimension
                || height > MovementConfig.MaxGridDimension)
            {
                throw new InvalidDataException("Movement grid dimensions out of range.");
            }

            long cellCountLong = (long)width * height;
            if (cellCountLong > int.MaxValue)
            {
                throw new InvalidDataException("Movement grid cell count overflows int.");
            }

            int cells = (int)cellCountLong;
            if (slope.Count != cells)
            {
                throw new InvalidDataException("MVSL count does not match META.moveWidth * moveHeight.");
            }

            if (cover.Count != cells)
            {
                throw new InvalidDataException("MVLC count does not match the movement grid.");
            }

            int fordBytes = (cells + 7) / 8;
            if (ford.Count != fordBytes)
            {
                throw new InvalidDataException("MVFD size does not match the movement grid bitset.");
            }

            int movementCellSizeCm = MovementCellSizeCm(meta);
            if (movementCellSizeCm < 1)
            {
                throw new InvalidDataException("Movement cell size must be >= 1 cm.");
            }
            byte[] slopeBytes = file.SectionBytes(slope);
            byte[] coverBytes = file.SectionBytes(cover);
            byte[] fordBytesArr = file.SectionBytes(ford);
            ushort[] slopeOut = new ushort[cells];
            byte[] coverOut = new byte[cells];
            BlobReader slopeReader = new BlobReader(slopeBytes);
            for (int i = 0; i < cells; i++)
            {
                slopeOut[i] = slopeReader.U16();
                byte wrmbCover = coverBytes[i];
                if (wrmbCover > WrmbCoverSnowIce)
                {
                    throw new InvalidDataException("Unknown land-cover class.");
                }

                bool isFord = (fordBytesArr[i >> 3] & (1 << (i & 7))) != 0;
                coverOut[i] = isFord ? (byte)LandCover.Ford : MapLandCover(wrmbCover);
            }

            return new TerrainGrid(width, height, movementCellSizeCm, 0, 0, slopeOut, coverOut);
        }

        public static byte[] WriteRoadGraph(RoadGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            BlobWriter rnod = new BlobWriter(graph.NodeCount * RnodElementSize);
            for (int i = 0; i < graph.NodeCount; i++)
            {
                bool head = (graph.NodeFlags[i] & (ushort)RoadNodeFlags.BridgeHead) != 0;
                rnod.I32(graph.NodeId[i]);
                rnod.I32(graph.NodeXCm[i]);
                rnod.I32(graph.NodeYCm[i]);
                rnod.I32(0);
                rnod.U8(head ? WrmbNodeFlagBridgeHead : (byte)0);
                rnod.U8(head ? (byte)2 : (byte)0);
                rnod.U8(0);
                rnod.U8(0);
                rnod.I32(0);
            }

            BlobWriter pts = new BlobWriter(graph.EdgeCount * 16);
            BlobWriter redg = new BlobWriter(graph.EdgeCount * RedgElementSize);
            int ptCount = 0;
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                int from = graph.EdgeFromIndex[i];
                int to = graph.EdgeToIndex[i];
                int aId = graph.NodeId[from];
                int bId = graph.NodeId[to];
                int ax = graph.NodeXCm[from];
                int ay = graph.NodeYCm[from];
                int bx = graph.NodeXCm[to];
                int by = graph.NodeYCm[to];
                if (aId > bId)
                {
                    int tmp = aId;
                    aId = bId;
                    bId = tmp;
                    tmp = ax;
                    ax = bx;
                    bx = tmp;
                    tmp = ay;
                    ay = by;
                    by = tmp;
                }

                bool span = (graph.EdgeFlags[i] & (ushort)RoadEdgeFlags.BridgeSpan) != 0;
                byte surface = graph.EdgeSurface[i];
                sbyte wrmbSurface = MapSurfaceToWrmb(surface);
                ushort wrmbFlags = span ? WrmbEdgeFlagBridgeSpan : (ushort)0;
                pts.I32(ax);
                pts.I32(ay);
                pts.I32(bx);
                pts.I32(by);

                redg.I32(graph.EdgeId[i]);
                redg.I32(aId);
                redg.I32(bId);
                redg.I32(graph.EdgeLengthCm[i]);
                redg.I32(graph.EdgeLengthCm[i]);
                redg.I32(0);
                redg.I32(0);
                redg.I32(0);
                redg.I32(0);
                redg.I32(0);
                redg.U8(0);
                redg.U8((byte)wrmbSurface);
                redg.U8(0);
                redg.U8(MapTierToHighway(graph.EdgeTier[i]));
                redg.U16(wrmbFlags);
                redg.U16(0);
                redg.U32((uint)ptCount);
                redg.U32(2);
                redg.U32(0);
                redg.U32(0);
                ptCount += 2;
            }

            WrmbMetaFields meta = new WrmbMetaFields
            {
                MapWidthCm = 1,
                MapHeightCm = 1,
                CellSizeCm = 1,
                GridWidth = 1,
                GridHeight = 1,
                RoadNodeCount = graph.NodeCount,
                RoadEdgeCount = graph.EdgeCount,
                MoveWidth = 1,
                MoveHeight = 1,
                MoveCellsPerCell = 1
            };

            return WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", WriteMeta(meta), MetaElementSize),
                    new WrmbSectionPayload("RNOD", rnod.ToArray(), RnodElementSize),
                    new WrmbSectionPayload("REDG", redg.ToArray(), RedgElementSize),
                    new WrmbSectionPayload("PTSP", pts.ToArray(), 8)
                });
        }

        public static byte[] WriteTerrain(TerrainGrid grid)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            int cells = grid.CellCount;
            BlobWriter slope = new BlobWriter(cells * 2);
            byte[] cover = new byte[cells];
            int fordLen = (cells + 7) / 8;
            byte[] ford = new byte[fordLen];
            for (int i = 0; i < cells; i++)
            {
                slope.U16(grid.SlopePerMille[i]);
                LandCover land = (LandCover)grid.Cover[i];
                if (land == LandCover.Ford)
                {
                    cover[i] = WrmbCoverMarsh;
                    ford[i >> 3] |= (byte)(1 << (i & 7));
                }
                else
                {
                    cover[i] = MapLandCoverToWrmb(land);
                }
            }

            WrmbMetaFields meta = new WrmbMetaFields
            {
                MapWidthCm = checked(grid.Width * grid.CellSizeCm),
                MapHeightCm = checked(grid.Height * grid.CellSizeCm),
                CellSizeCm = grid.CellSizeCm,
                GridWidth = grid.Width,
                GridHeight = grid.Height,
                MoveWidth = grid.Width,
                MoveHeight = grid.Height,
                MoveCellsPerCell = 1
            };

            return WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", WriteMeta(meta), MetaElementSize),
                    new WrmbSectionPayload("MVSL", slope.ToArray(), 2),
                    new WrmbSectionPayload("MVLC", cover, 1),
                    new WrmbSectionPayload("MVFD", ford, 1)
                });
        }

        internal static int MovementCellSizeCm(WrmbMeta meta)
        {
            int per = meta.MoveCellsPerCell;
            if (per > 1
                && meta.GridWidth > 0
                && meta.GridHeight > 0
                && meta.MoveWidth == meta.GridWidth * per
                && meta.MoveHeight == meta.GridHeight * per)
            {
                return meta.CellSizeCm / per;
            }

            return meta.CellSizeCm;
        }

        internal static byte MapLandCover(byte wrmb)
        {
            switch (wrmb)
            {
                case WrmbCoverNodata:
                    return (byte)LandCover.Water;
                case WrmbCoverField:
                    return (byte)LandCover.Field;
                case WrmbCoverScrub:
                    return (byte)LandCover.Scrub;
                case WrmbCoverUrban:
                    return (byte)LandCover.Urban;
                case WrmbCoverForest:
                    return (byte)LandCover.Forest;
                case WrmbCoverMarsh:
                    return (byte)LandCover.Marsh;
                case WrmbCoverWater:
                    return (byte)LandCover.Water;
                case WrmbCoverSnowIce:
                    return (byte)LandCover.Scrub;
                default:
                    throw new InvalidDataException("Unknown land-cover class.");
            }
        }

        internal static byte MapLandCoverToWrmb(LandCover cover)
        {
            switch (cover)
            {
                case LandCover.Field:
                    return WrmbCoverField;
                case LandCover.Scrub:
                    return WrmbCoverScrub;
                case LandCover.Urban:
                    return WrmbCoverUrban;
                case LandCover.Forest:
                    return WrmbCoverForest;
                case LandCover.Marsh:
                    return WrmbCoverMarsh;
                case LandCover.Water:
                    return WrmbCoverWater;
                case LandCover.Ford:
                    return WrmbCoverMarsh;
                default:
                    throw new InvalidDataException("Unknown land-cover class.");
            }
        }

        internal static ushort MapNodeFlags(byte wrmbFlags)
        {
            return (wrmbFlags & WrmbNodeFlagBridgeHead) != 0
                ? (ushort)RoadNodeFlags.BridgeHead
                : (ushort)0;
        }

        internal static ushort MapEdgeFlags(ushort wrmbFlags)
        {
            return (wrmbFlags & WrmbEdgeFlagBridgeSpan) != 0
                ? (ushort)RoadEdgeFlags.BridgeSpan
                : (ushort)0;
        }

        internal static byte MapHighwayClass(int highwayClass)
        {
            if (highwayClass <= 1)
            {
                return (byte)RoadTier.Highway;
            }

            if (highwayClass <= 3)
            {
                return (byte)RoadTier.Paved;
            }

            if (highwayClass <= 6)
            {
                return (byte)RoadTier.Dirt;
            }

            return (byte)RoadTier.Track;
        }

        internal static byte MapTierToHighway(byte tier)
        {
            switch ((RoadTier)tier)
            {
                case RoadTier.Highway:
                    return 0;
                case RoadTier.Paved:
                    return 2;
                case RoadTier.Dirt:
                    return 4;
                default:
                    return 7;
            }
        }

        internal static byte MapSurface(int wrmbSurface, bool span)
        {
            if (span)
            {
                return (byte)RoadSurface.BridgeDeck;
            }

            switch (wrmbSurface)
            {
                case 0:
                    return (byte)RoadSurface.Dirt;
                case 1:
                    return (byte)RoadSurface.Gravel;
                default:
                    return (byte)RoadSurface.Asphalt;
            }
        }

        internal static sbyte MapSurfaceToWrmb(byte surface)
        {
            switch ((RoadSurface)surface)
            {
                case RoadSurface.Dirt:
                    return 0;
                case RoadSurface.Gravel:
                    return 1;
                default:
                    return 2;
            }
        }

        internal static void CheckElementSize(string tag, int actual, int expected)
        {
            if (actual != expected)
            {
                throw new InvalidDataException(tag + ": elementSize " + actual + " != schema " + expected + ".");
            }
        }

        internal static void CheckMetaElementSize(int actual)
        {
            if (actual != MetaElementSize && actual != PipelineMetaElementSize)
            {
                throw new InvalidDataException(
                    "META: elementSize " + actual + " is not " + MetaElementSize
                    + " (synthetic) or " + PipelineMetaElementSize + " (pipeline).");
            }
        }

        internal static WrmbMeta ReadMeta(byte[] record)
        {
            if (record.Length < MetaElementSize)
            {
                throw new InvalidDataException("META record truncated.");
            }

            BlobReader r = new BlobReader(record);
            WrmbMeta m = new WrmbMeta();
            m.SchemaVersion = r.I32();
            if (record.Length >= PipelineMetaElementSize)
            {
                r.I32();
            }

            m.MapWidthCm = r.I32();
            m.MapHeightCm = r.I32();
            m.CellSizeCm = r.I32();
            m.GridWidth = r.I32();
            m.GridHeight = r.I32();
            m.TownCount = r.I32();
            m.TerritoryCount = r.I32();
            m.RoadNodeCount = r.I32();
            m.RoadEdgeCount = r.I32();
            r.Skip(60);
            m.MoveWidth = r.I32();
            m.MoveHeight = r.I32();
            m.MoveCellsPerCell = r.I32();
            if (record.Length >= PipelineMetaElementSize)
            {
                r.Seek(340);
                m.TerrainPayloadSha256 = r.Bytes(32);
            }
            else
            {
                m.TerrainPayloadSha256 = new byte[32];
            }

            if (m.SchemaVersion != WrmbFile.SchemaVersion)
            {
                throw new InvalidDataException("META.schemaVersion " + m.SchemaVersion + " is unsupported.");
            }

            return m;
        }

        internal static byte[] WriteMeta(WrmbMetaFields f)
        {
            BlobWriter w = new BlobWriter(MetaElementSize);
            w.I32(WrmbFile.SchemaVersion);
            w.I32(f.MapWidthCm);
            w.I32(f.MapHeightCm);
            w.I32(f.CellSizeCm);
            w.I32(f.GridWidth);
            w.I32(f.GridHeight);
            w.I32(0);
            w.I32(0);
            w.I32(f.RoadNodeCount);
            w.I32(f.RoadEdgeCount);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(0);
            w.I32(f.MoveWidth);
            w.I32(f.MoveHeight);
            w.I32(f.MoveCellsPerCell);
            w.U64(0);
            for (int i = 0; i < 5; i++)
            {
                w.U32(0);
                w.U32(0);
            }

            byte[] emptyHash;
            using (SHA256 sha = SHA256.Create())
            {
                emptyHash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Empty));
            }

            w.Bytes(emptyHash);
            w.Zeroes(32);
            w.Zeroes(32);
            w.Zeroes(32);
            if (w.Length != MetaElementSize)
            {
                throw new InvalidOperationException("META writer produced " + w.Length + " bytes.");
            }

            return w.ToArray();
        }

        public static Chokepoint[] ReadChokepoints(WrmbFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (!file.TryGetSection("CHOK", out WrmbDirEntry chok))
            {
                return Array.Empty<Chokepoint>();
            }

            int expected = file.FileFormatVersion >= WrmbFile.FormatVersionV1
                ? ChokElementSizeV1
                : ChokElementSizeV0;
            CheckElementSize("CHOK", chok.ElementSize, expected);
            byte[] data = file.SectionBytes(chok);
            Chokepoint[] records = new Chokepoint[chok.Count];
            BlobReader r = new BlobReader(data);
            for (int i = 0; i < chok.Count; i++)
            {
                r.Seek(i * chok.ElementSize);
                int id = r.I32();
                byte kind = r.U8();
                r.U8();
                r.U8();
                byte defenseGroupId = r.U8();
                int x = r.I32();
                int y = r.I32();
                r.I32();
                int roadNodeId = r.I32();
                int roadEdgeId = r.I32();
                r.Skip(16);
                int sideA = r.I32();
                int sideB = r.I32();
                r.Skip(24);
                r.U16();
                byte bypassWheeled = 0;
                ulong stableKey = 0;
                if (chok.ElementSize >= 80)
                {
                    r.U8();
                    bypassWheeled = r.U8();
                }

                if (chok.ElementSize >= ChokElementSizeV1)
                {
                    stableKey = r.U64();
                }

                records[i] = new Chokepoint(
                    id,
                    kind,
                    defenseGroupId,
                    bypassWheeled,
                    x,
                    y,
                    roadNodeId,
                    roadEdgeId,
                    sideA,
                    sideB,
                    stableKey);
            }

            return records;
        }

        public static Territory[] ReadTerritories(WrmbFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (!file.TryGetSection("TERR", out WrmbDirEntry terr))
            {
                return Array.Empty<Territory>();
            }

            int expected = file.FileFormatVersion >= WrmbFile.FormatVersionV1
                ? TerrElementSizeV1
                : TerrElementSizeV0;
            CheckElementSize("TERR", terr.ElementSize, expected);
            byte[] data = file.SectionBytes(terr);
            Territory[] records = new Territory[terr.Count];
            BlobReader r = new BlobReader(data);
            for (int i = 0; i < terr.Count; i++)
            {
                r.Seek(i * terr.ElementSize);
                int id = r.I32();
                ulong stableKey = 0;
                if (terr.ElementSize >= TerrElementSizeV1)
                {
                    r.Skip(36);
                    stableKey = r.U64();
                }

                records[i] = new Territory(id, stableKey);
            }

            return records;
        }

        internal static byte[] WritePipelineMeta(WrmbMetaFields f, int fileKind)
        {
            BlobWriter w = new BlobWriter(PipelineMetaElementSize);
            w.I32(WrmbFile.SchemaVersion);
            w.I32(fileKind);
            w.I32(f.MapWidthCm);
            w.I32(f.MapHeightCm);
            w.I32(f.CellSizeCm);
            w.I32(f.GridWidth);
            w.I32(f.GridHeight);
            w.I32(0);
            w.I32(0);
            w.I32(f.RoadNodeCount);
            w.I32(f.RoadEdgeCount);
            for (int i = 0; i < 15; i++)
            {
                w.I32(0);
            }

            w.I32(f.MoveWidth);
            w.I32(f.MoveHeight);
            w.I32(f.MoveCellsPerCell);
            w.U64(0);
            for (int i = 0; i < 5; i++)
            {
                w.U32(0);
                w.U32(0);
            }

            byte[] emptyHash;
            using (SHA256 sha = SHA256.Create())
            {
                emptyHash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Empty));
            }

            w.Bytes(emptyHash);
            w.Zeroes(32);
            w.Zeroes(32);
            w.Zeroes(32);
            w.I32(0);
            w.I32(0);
            w.U32(0);
            w.U32(0);
            w.Zeroes(32);
            w.Zeroes(32);
            w.Zeroes(4);
            if (w.Length != PipelineMetaElementSize)
            {
                throw new InvalidOperationException("Pipeline META writer produced " + w.Length + " bytes.");
            }

            return w.ToArray();
        }
    }

    internal struct WrmbMeta
    {
        public int SchemaVersion;
        public int MapWidthCm;
        public int MapHeightCm;
        public int CellSizeCm;
        public int GridWidth;
        public int GridHeight;
        public int TownCount;
        public int TerritoryCount;
        public int RoadNodeCount;
        public int RoadEdgeCount;
        public int MoveWidth;
        public int MoveHeight;
        public int MoveCellsPerCell;
        public byte[] TerrainPayloadSha256;
    }

    internal struct WrmbMetaFields
    {
        public int MapWidthCm;
        public int MapHeightCm;
        public int CellSizeCm;
        public int GridWidth;
        public int GridHeight;
        public int RoadNodeCount;
        public int RoadEdgeCount;
        public int MoveWidth;
        public int MoveHeight;
        public int MoveCellsPerCell;
    }
}
