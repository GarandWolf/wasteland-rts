using System;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Along-graph distances for depot pre-check and HQ/depot range.
    /// Reuses scratch buffers; they are neither hashed nor saved.
    /// Each search charges <see cref="AiFactionState.WorkUnitsThisTick"/>.
    /// </summary>
    internal sealed class AiRoadSupply
    {
        private long[] _dist = Array.Empty<long>();
        private bool[] _seen = Array.Empty<bool>();
        private readonly MinHeap _heap = new MinHeap(8);

        public int DistanceCm(RoadGraph graph, int fromNodeId, int toNodeId, AiFactionState faction)
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

            return DistanceFromIndex(graph, fromIndex, toIndex, faction);
        }

        public int DistanceToNearestCm(
            RoadGraph graph,
            int fromNodeId,
            int[] networkNodeIds,
            int networkCount,
            AiFactionState faction)
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
                int d = DistanceCm(graph, fromNodeId, networkNodeIds[i], faction);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        public bool ConnectedToNetwork(
            RoadGraph graph,
            int fromNodeId,
            int[] networkNodeIds,
            int networkCount,
            AiFactionState faction)
        {
            return DistanceToNearestCm(graph, fromNodeId, networkNodeIds, networkCount, faction)
                < AiConstants.UnreachableCm;
        }

        private int DistanceFromIndex(RoadGraph graph, int fromIndex, int toIndex, AiFactionState faction)
        {
            int nodeCount = graph.NodeCount;
            Ensure(nodeCount);
            faction.WorkUnitsThisTick++;
            for (int i = 0; i < nodeCount; i++)
            {
                _dist[i] = long.MaxValue / 4;
                _seen[i] = false;
            }

            _heap.Clear();
            _dist[fromIndex] = 0;
            _heap.Push(0, graph.GetNodeId(fromIndex), fromIndex);
            while (_heap.TryPop(out int index, out long f))
            {
                if (_seen[index])
                {
                    continue;
                }

                _seen[index] = true;
                if (index == toIndex)
                {
                    return _dist[index] > int.MaxValue ? AiConstants.UnreachableCm : (int)_dist[index];
                }

                if (f != _dist[index])
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
                    long next = _dist[index] + graph.EdgeLengthCm[edgeIndex];
                    if (next < _dist[neighbor])
                    {
                        _dist[neighbor] = next;
                        _heap.Push(next, graph.GetNodeId(neighbor), neighbor);
                    }
                }
            }

            return AiConstants.UnreachableCm;
        }

        private void Ensure(int nodeCount)
        {
            if (_dist.Length >= nodeCount)
            {
                return;
            }

            _dist = new long[nodeCount];
            _seen = new bool[nodeCount];
        }
    }
}
