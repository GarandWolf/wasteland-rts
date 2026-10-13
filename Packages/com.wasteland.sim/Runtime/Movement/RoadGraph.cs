using System;
using System.Collections.Generic;
using System.IO;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// Runtime road graph. The blob is immutable; cut spans are a parallel
    /// bool array. <see cref="Version"/> starts at 1 and increments on each
    /// successful cut.
    /// </summary>
    public sealed class RoadGraph
    {
        internal readonly int[] NodeId;
        internal readonly int[] NodeXCm;
        internal readonly int[] NodeYCm;
        internal readonly ushort[] NodeFlags;
        internal readonly ushort[] NodeTerritoryId;
        internal readonly int[] EdgeId;
        internal readonly int[] EdgeFromIndex;
        internal readonly int[] EdgeToIndex;
        internal readonly int[] EdgeLengthCm;
        internal readonly byte[] EdgeTier;
        internal readonly byte[] EdgeSurface;
        internal readonly ushort[] EdgeFlags;
        internal readonly bool[] EdgeCut;
        internal readonly int[] AdjOffset;
        internal readonly int[] AdjNeighborIndex;
        internal readonly int[] AdjEdgeIndex;
        public readonly int OriginXCm;
        public readonly int OriginYCm;

        internal RoadGraph(
            int[] nodeId,
            int[] nodeXCm,
            int[] nodeYCm,
            ushort[] nodeFlags,
            ushort[] nodeTerritoryId,
            int[] edgeId,
            int[] edgeFromIndex,
            int[] edgeToIndex,
            int[] edgeLengthCm,
            byte[] edgeTier,
            byte[] edgeSurface,
            ushort[] edgeFlags,
            int[] adjOffset,
            int[] adjNeighborIndex,
            int[] adjEdgeIndex,
            int originXCm,
            int originYCm)
        {
            NodeId = nodeId;
            NodeXCm = nodeXCm;
            NodeYCm = nodeYCm;
            NodeFlags = nodeFlags;
            NodeTerritoryId = nodeTerritoryId ?? Array.Empty<ushort>();
            EdgeId = edgeId;
            EdgeFromIndex = edgeFromIndex;
            EdgeToIndex = edgeToIndex;
            EdgeLengthCm = edgeLengthCm;
            EdgeTier = edgeTier;
            EdgeSurface = edgeSurface;
            EdgeFlags = edgeFlags;
            EdgeCut = new bool[edgeId.Length];
            AdjOffset = adjOffset;
            AdjNeighborIndex = adjNeighborIndex;
            AdjEdgeIndex = adjEdgeIndex;
            OriginXCm = originXCm;
            OriginYCm = originYCm;
            Version = 1;
        }

        public int Version { get; private set; }

        public int NodeCount
        {
            get { return NodeId.Length; }
        }

        public int EdgeCount
        {
            get { return EdgeId.Length; }
        }

        public bool TryGetNodeIndex(int nodeId, out int index)
        {
            index = Array.BinarySearch(NodeId, nodeId);
            return index >= 0;
        }

        public bool TryGetEdgeIndex(int edgeId, out int index)
        {
            index = Array.BinarySearch(EdgeId, edgeId);
            return index >= 0;
        }

        public int GetNodeId(int index)
        {
            return NodeId[index];
        }

        public int GetNodeXCm(int index)
        {
            return NodeXCm[index];
        }

        public int GetNodeYCm(int index)
        {
            return NodeYCm[index];
        }

        /// <summary>
        /// Nearest node to a point, or 0 when the graph has no nodes.
        /// Ties break to the lower node id. The id comes from this graph.
        /// </summary>
        public int NearestNodeId(int xCm, int yCm)
        {
            if (NodeCount < 1)
            {
                return 0;
            }

            int bestIndex = 0;
            int bestId = NodeId[0];
            long bestDist = long.MaxValue;
            for (int i = 0; i < NodeCount; i++)
            {
                long dx = (long)NodeXCm[i] - xCm;
                long dy = (long)NodeYCm[i] - yCm;
                long dist = (dx * dx) + (dy * dy);
                int id = NodeId[i];
                if (dist < bestDist || (dist == bestDist && id < bestId))
                {
                    bestDist = dist;
                    bestId = id;
                    bestIndex = i;
                }
            }

            return NodeId[bestIndex];
        }

        public RoadNodeFlags GetNodeFlags(int index)
        {
            return (RoadNodeFlags)NodeFlags[index];
        }

        public ushort GetNodeTerritoryId(int index)
        {
            return NodeTerritoryId[index];
        }

        public int GetEdgeId(int index)
        {
            return EdgeId[index];
        }

        public int GetEdgeLengthCm(int index)
        {
            return EdgeLengthCm[index];
        }

        public RoadTier GetEdgeTier(int index)
        {
            return (RoadTier)EdgeTier[index];
        }

        public RoadSurface GetEdgeSurface(int index)
        {
            return (RoadSurface)EdgeSurface[index];
        }

        public RoadEdgeFlags GetEdgeFlags(int index)
        {
            return (RoadEdgeFlags)EdgeFlags[index];
        }

        public bool IsCut(int edgeIndex)
        {
            return EdgeCut[edgeIndex];
        }

        /// <summary>
        /// Marks a bridge-span edge as cut. Returns false if the id is unknown,
        /// the edge is not a span, or it is already cut.
        /// </summary>
        public bool CutSpan(int edgeId)
        {
            if (!TryGetEdgeIndex(edgeId, out int index))
            {
                return false;
            }

            if ((EdgeFlags[index] & (ushort)RoadEdgeFlags.BridgeSpan) == 0)
            {
                return false;
            }

            if (EdgeCut[index])
            {
                return false;
            }

            EdgeCut[index] = true;
            Version++;
            return true;
        }

        internal int NeighborCount(int nodeIndex)
        {
            return AdjOffset[nodeIndex + 1] - AdjOffset[nodeIndex];
        }

        internal static int IndexOfSorted(int[] ids, int id)
        {
            int index = Array.BinarySearch(ids, id);
            if (index < 0)
            {
                throw new InvalidDataException("Unknown id " + id + ".");
            }

            return index;
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
