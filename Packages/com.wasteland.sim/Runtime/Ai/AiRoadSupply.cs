using System;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Along-graph distances for depot pre-check and HQ/depot range.
    /// Uses the loaded <see cref="RoadGraph"/>; does not persist node ids as
    /// map identity.
    /// </summary>
    internal static class AiRoadSupply
    {
        public static int DistanceCm(RoadGraph graph, int fromNodeId, int toNodeId)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (fromNodeId == toNodeId)
            {
                return 0;
            }

            if (!graph.TryGetNodeIndex(fromNodeId, out int fromIndex)
                || !graph.TryGetNodeIndex(toNodeId, out int toIndex))
            {
                return AiConstants.UnreachableCm;
            }

            return DistanceFromIndex(graph, fromIndex, toIndex);
        }

        public static int DistanceToNearestCm(RoadGraph graph, int fromNodeId, int[] networkNodeIds, int networkCount)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (networkNodeIds == null || networkCount < 1)
            {
                return AiConstants.UnreachableCm;
            }

            int best = AiConstants.UnreachableCm;
            for (int i = 0; i < networkCount; i++)
            {
                int d = DistanceCm(graph, fromNodeId, networkNodeIds[i]);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        public static bool ConnectedToNetwork(RoadGraph graph, int nodeId, int[] networkNodeIds, int networkCount)
        {
            return DistanceToNearestCm(graph, nodeId, networkNodeIds, networkCount) < AiConstants.UnreachableCm;
        }

        private static int DistanceFromIndex(RoadGraph graph, int fromIndex, int toIndex)
        {
            int nodeCount = graph.NodeCount;
            long[] dist = new long[nodeCount];
            bool[] seen = new bool[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                dist[i] = long.MaxValue / 4;
            }

            dist[fromIndex] = 0;
            MinHeap heap = new MinHeap(nodeCount);
            heap.Push(0, graph.GetNodeId(fromIndex), fromIndex);
            while (heap.TryPop(out int index, out long f))
            {
                if (seen[index])
                {
                    continue;
                }

                seen[index] = true;
                if (index == toIndex)
                {
                    return dist[index] > int.MaxValue ? AiConstants.UnreachableCm : (int)dist[index];
                }

                if (f != dist[index])
                {
                    continue;
                }

                int start = graph.AdjOffset[index];
                int end = graph.AdjOffset[index + 1];
                for (int e = start; e < end; e++)
                {
                    int edgeIndex = graph.AdjEdgeIndex[e];
                    if (graph.IsCut(edgeIndex))
                    {
                        continue;
                    }

                    int neighbor = graph.AdjNeighborIndex[e];
                    long next = dist[index] + graph.EdgeLengthCm[edgeIndex];
                    if (next < dist[neighbor])
                    {
                        dist[neighbor] = next;
                        heap.Push(next, graph.GetNodeId(neighbor), neighbor);
                    }
                }
            }

            return AiConstants.UnreachableCm;
        }
    }
}
