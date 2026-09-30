using System;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Along-graph distances for depot pre-check and HQ/depot range.
    /// Reuses scratch buffers; they are neither hashed nor saved.
    /// Each settled node charges <see cref="AiFactionState.WorkUnitsThisTick"/>
    /// against <see cref="AiFactionState.WorkUnitsPerTick"/>. A search that
    /// exhausts the budget returns <see cref="AiConstants.UnreachableCm"/>.
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

            return Search(graph, fromIndex, toIndex, Array.Empty<int>(), 0, faction);
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

            if (!graph.TryGetNodeIndex(fromNodeId, out int fromIndex))
            {
                return AiConstants.UnreachableCm;
            }

            for (int i = 0; i < networkCount; i++)
            {
                if (networkNodeIds[i] == fromNodeId)
                {
                    return 0;
                }
            }

            return Search(graph, fromIndex, -1, networkNodeIds, networkCount, faction);
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

        /// <summary>
        /// Single-source Dijkstra from <paramref name="fromIndex"/>. When
        /// <paramref name="stopIndex"/> is a node index, stop on that node.
        /// When <paramref name="networkNodeIds"/> is set, stop as soon as any
        /// network node is settled (one multi-source-equivalent pass).
        /// </summary>
        private int Search(
            RoadGraph graph,
            int fromIndex,
            int stopIndex,
            int[] networkNodeIds,
            int networkCount,
            AiFactionState faction)
        {
            int nodeCount = graph.NodeCount;
            Ensure(nodeCount);
            bool stopOnNetwork = networkCount > 0;
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

                if (!TryCharge(faction))
                {
                    return AiConstants.UnreachableCm;
                }

                _seen[index] = true;
                if (f != _dist[index])
                {
                    continue;
                }

                if (stopIndex >= 0 && index == stopIndex)
                {
                    return DistanceOf(index);
                }

                if (stopOnNetwork && IsNetworkIndex(graph, index, networkNodeIds, networkCount))
                {
                    return DistanceOf(index);
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

            if (stopIndex >= 0)
            {
                return AiConstants.UnreachableCm;
            }

            if (!stopOnNetwork)
            {
                return AiConstants.UnreachableCm;
            }

            int best = AiConstants.UnreachableCm;
            for (int i = 0; i < networkCount; i++)
            {
                if (!graph.TryGetNodeIndex(networkNodeIds[i], out int idx) || !_seen[idx])
                {
                    continue;
                }

                int d = DistanceOf(idx);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        private static bool IsNetworkIndex(RoadGraph graph, int index, int[] networkNodeIds, int networkCount)
        {
            int nodeId = graph.GetNodeId(index);
            for (int i = 0; i < networkCount; i++)
            {
                if (networkNodeIds[i] == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        private int DistanceOf(int index)
        {
            return _dist[index] > int.MaxValue ? AiConstants.UnreachableCm : (int)_dist[index];
        }

        private static bool TryCharge(AiFactionState faction)
        {
            if (faction.WorkUnitsThisTick >= faction.WorkUnitsPerTick)
            {
                return false;
            }

            faction.WorkUnitsThisTick++;
            return true;
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
