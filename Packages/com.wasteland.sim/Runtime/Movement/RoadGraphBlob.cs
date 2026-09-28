using System;
using System.Collections.Generic;
using System.IO;

namespace Wasteland.Sim.Movement
{
    /// <summary>Little-endian reader/writer for road-graph schema v1. See docs/formats/road-graph-v1.md.</summary>
    public static class RoadGraphBlob
    {
        public const string Magic = "WRG1";

        public static byte[] Write(RoadGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            BlobWriter writer = new BlobWriter(
                MovementConfig.BlobHeaderSize
                + graph.NodeCount * MovementConfig.RoadNodeStride
                + graph.EdgeCount * MovementConfig.RoadEdgeStride);

            writer.Magic(Magic);
            writer.U16(MovementConfig.RoadSchemaVersion);
            writer.U16(MovementConfig.BlobHeaderSize);
            writer.U32((uint)graph.NodeCount);
            writer.U32((uint)graph.EdgeCount);
            writer.I32(graph.OriginXCm);
            writer.I32(graph.OriginYCm);
            writer.U32(0);
            writer.U32(0);

            for (int i = 0; i < graph.NodeCount; i++)
            {
                writer.U32((uint)graph.NodeId[i]);
                writer.I32(graph.NodeXCm[i]);
                writer.I32(graph.NodeYCm[i]);
                writer.U16(graph.NodeFlags[i]);
                writer.U16(0);
            }

            for (int i = 0; i < graph.EdgeCount; i++)
            {
                writer.U32((uint)graph.EdgeId[i]);
                writer.U32((uint)graph.NodeId[graph.EdgeFromIndex[i]]);
                writer.U32((uint)graph.NodeId[graph.EdgeToIndex[i]]);
                writer.U32((uint)graph.EdgeLengthCm[i]);
                writer.U8(graph.EdgeTier[i]);
                writer.U8(graph.EdgeSurface[i]);
                writer.U16(graph.EdgeFlags[i]);
                writer.U32(0);
            }

            return writer.ToArray();
        }

        public static RoadGraph Read(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            BlobReader reader = new BlobReader(data);
            reader.ExpectMagic(Magic);
            ushort schema = reader.U16();
            if (schema != MovementConfig.RoadSchemaVersion)
            {
                throw new InvalidDataException("Unsupported road-graph schema " + schema + ".");
            }

            ushort headerSize = reader.U16();
            if (headerSize < MovementConfig.BlobHeaderSize)
            {
                throw new InvalidDataException("Road-graph header is truncated.");
            }

            int nodeCount = checked((int)reader.U32());
            int edgeCount = checked((int)reader.U32());
            if (nodeCount < 0 || nodeCount > MovementConfig.MaxRoadNodes)
            {
                throw new InvalidDataException("Road-graph node count out of range.");
            }

            if (edgeCount < 0 || edgeCount > MovementConfig.MaxRoadEdges)
            {
                throw new InvalidDataException("Road-graph edge count out of range.");
            }

            int originX = reader.I32();
            int originY = reader.I32();
            reader.U32();
            reader.U32();
            reader.Seek(headerSize);

            long expected = (long)headerSize
                + (long)nodeCount * MovementConfig.RoadNodeStride
                + (long)edgeCount * MovementConfig.RoadEdgeStride;
            if (data.Length != expected)
            {
                throw new InvalidDataException("Road-graph blob length does not match header.");
            }

            int[] nodeId = new int[nodeCount];
            int[] nodeX = new int[nodeCount];
            int[] nodeY = new int[nodeCount];
            ushort[] nodeFlags = new ushort[nodeCount];
            int previousId = -1;
            bool havePrevious = false;
            for (int i = 0; i < nodeCount; i++)
            {
                nodeId[i] = (int)reader.U32();
                nodeX[i] = reader.I32();
                nodeY[i] = reader.I32();
                nodeFlags[i] = reader.U16();
                reader.U16();
                if (havePrevious && nodeId[i] <= previousId)
                {
                    throw new InvalidDataException("Road nodes must be sorted by unique id.");
                }

                previousId = nodeId[i];
                havePrevious = true;
            }

            int[] edgeId = new int[edgeCount];
            int[] edgeFrom = new int[edgeCount];
            int[] edgeTo = new int[edgeCount];
            int[] edgeLength = new int[edgeCount];
            byte[] edgeTier = new byte[edgeCount];
            byte[] edgeSurface = new byte[edgeCount];
            ushort[] edgeFlags = new ushort[edgeCount];
            havePrevious = false;
            previousId = -1;
            for (int i = 0; i < edgeCount; i++)
            {
                edgeId[i] = (int)reader.U32();
                int fromId = (int)reader.U32();
                int toId = (int)reader.U32();
                uint length = reader.U32();
                if (length == 0 || length > (uint)int.MaxValue)
                {
                    throw new InvalidDataException("Edge length must be > 0.");
                }

                byte tier = reader.U8();
                byte surface = reader.U8();
                ushort flags = reader.U16();
                reader.U32();
                if (havePrevious && edgeId[i] <= previousId)
                {
                    throw new InvalidDataException("Road edges must be sorted by unique id.");
                }

                if (tier > (byte)RoadTier.Highway)
                {
                    throw new InvalidDataException("Unknown road tier.");
                }

                if (surface > (byte)RoadSurface.BridgeDeck)
                {
                    throw new InvalidDataException("Unknown road surface.");
                }

                if (fromId == toId)
                {
                    throw new InvalidDataException("Self-loop road edge.");
                }

                int fromIndex = Array.BinarySearch(nodeId, fromId);
                int toIndex = Array.BinarySearch(nodeId, toId);
                if (fromIndex < 0 || toIndex < 0)
                {
                    throw new InvalidDataException("Edge endpoint is not a node.");
                }

                bool span = (flags & (ushort)RoadEdgeFlags.BridgeSpan) != 0;
                if (span)
                {
                    bool fromHead = (nodeFlags[fromIndex] & (ushort)RoadNodeFlags.BridgeHead) != 0;
                    bool toHead = (nodeFlags[toIndex] & (ushort)RoadNodeFlags.BridgeHead) != 0;
                    if (!fromHead || !toHead)
                    {
                        throw new InvalidDataException("Bridge span must join two bridge-head nodes.");
                    }
                }

                edgeFrom[i] = fromIndex;
                edgeTo[i] = toIndex;
                edgeLength[i] = (int)length;
                edgeTier[i] = tier;
                edgeSurface[i] = surface;
                edgeFlags[i] = flags;
                previousId = edgeId[i];
                havePrevious = true;
            }

            BuildAdjacency(
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
                originX,
                originY);
        }

        internal static void BuildAdjacency(
            int nodeCount,
            int[] edgeFrom,
            int[] edgeTo,
            int[] edgeId,
            int[] nodeId,
            out int[] adjOffset,
            out int[] adjNeighbor,
            out int[] adjEdge)
        {
            int edgeCount = edgeFrom.Length;
            int[] degree = new int[nodeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                degree[edgeFrom[i]]++;
                degree[edgeTo[i]]++;
            }

            adjOffset = new int[nodeCount + 1];
            int total = 0;
            for (int i = 0; i < nodeCount; i++)
            {
                adjOffset[i] = total;
                total += degree[i];
            }

            adjOffset[nodeCount] = total;
            int[] neighbor = new int[total];
            int[] edgeIndex = new int[total];
            int[] cursor = new int[nodeCount];
            Array.Copy(adjOffset, cursor, nodeCount);

            for (int i = 0; i < edgeCount; i++)
            {
                int a = edgeFrom[i];
                int b = edgeTo[i];
                int pa = cursor[a]++;
                neighbor[pa] = b;
                edgeIndex[pa] = i;
                int pb = cursor[b]++;
                neighbor[pb] = a;
                edgeIndex[pb] = i;
            }

            List<int> order = new List<int>();
            for (int n = 0; n < nodeCount; n++)
            {
                int start = adjOffset[n];
                int end = adjOffset[n + 1];
                int count = end - start;
                if (count <= 1)
                {
                    continue;
                }

                order.Clear();
                for (int k = 0; k < count; k++)
                {
                    order.Add(start + k);
                }

                order.Sort(delegate (int ia, int ib)
                {
                    int na = neighbor[ia];
                    int nb = neighbor[ib];
                    int c = nodeId[na].CompareTo(nodeId[nb]);
                    if (c != 0)
                    {
                        return c;
                    }

                    return edgeId[edgeIndex[ia]].CompareTo(edgeId[edgeIndex[ib]]);
                });

                int[] nbCopy = new int[count];
                int[] eCopy = new int[count];
                for (int k = 0; k < count; k++)
                {
                    nbCopy[k] = neighbor[order[k]];
                    eCopy[k] = edgeIndex[order[k]];
                }

                for (int k = 0; k < count; k++)
                {
                    neighbor[start + k] = nbCopy[k];
                    edgeIndex[start + k] = eCopy[k];
                }
            }

            adjNeighbor = neighbor;
            adjEdge = edgeIndex;
        }
    }
}
