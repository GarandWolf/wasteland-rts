using System;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Along-graph distances for depot pre-check and HQ/depot range.
    /// Reuses scratch buffers; they are neither hashed nor saved.
    /// Each settled node charges <see cref="AiFactionState.WorkUnitsThisTick"/>
    /// against <see cref="AiFactionState.WorkUnitsPerTick"/>. Exhausting the
    /// budget returns <see cref="AiSupplyStatus.BudgetExhausted"/>, not
    /// unreachability.
    /// </summary>
    internal sealed class AiRoadSupply
    {
        private long[] _dist = Array.Empty<long>();
        private int[] _fromNet = Array.Empty<int>();
        private int[] _fromTarget = Array.Empty<int>();
        private bool[] _seen = Array.Empty<bool>();
        private readonly MinHeap _heap = new MinHeap(8);

        public AiSupplyResult DistanceCm(RoadGraph graph, int fromNodeId, int toNodeId, AiFactionState faction)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (fromNodeId == toNodeId)
            {
                return AiSupplyResult.Reached(0);
            }

            if (!graph.TryGetNodeIndex(fromNodeId, out int fromIndex)
                || !graph.TryGetNodeIndex(toNodeId, out int toIndex))
            {
                return AiSupplyResult.Unreachable();
            }

            return Search(graph, fromIndex, toIndex, Array.Empty<int>(), 0, faction);
        }

        public AiSupplyResult DistanceToNearestCm(
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
                return AiSupplyResult.Unreachable();
            }

            if (!graph.TryGetNodeIndex(fromNodeId, out int fromIndex))
            {
                return AiSupplyResult.Unreachable();
            }

            for (int i = 0; i < networkCount; i++)
            {
                if (networkNodeIds[i] == fromNodeId)
                {
                    return AiSupplyResult.Reached(0);
                }
            }

            return Search(graph, fromIndex, -1, networkNodeIds, networkCount, faction);
        }

        public AiSupplyResult FillFromSources(
            RoadGraph graph,
            int[] sourceNodeIds,
            int sourceCount,
            AiFactionState faction)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            Ensure(graph.NodeCount);
            AiSupplyResult result = SearchFromMany(graph, sourceNodeIds, sourceCount, faction);
            CopyDistances(_fromNet);
            return result;
        }

        public AiSupplyResult FillFromNode(RoadGraph graph, int fromNodeId, AiFactionState faction)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (!graph.TryGetNodeIndex(fromNodeId, out int fromIndex))
            {
                return AiSupplyResult.Unreachable();
            }

            Ensure(graph.NodeCount);
            AiSupplyResult result = Search(graph, fromIndex, -1, Array.Empty<int>(), 0, faction);
            CopyDistances(_fromTarget);
            return result;
        }

        public int NetworkDistanceAt(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _fromNet.Length)
            {
                return AiConstants.UnreachableCm;
            }

            return _fromNet[nodeIndex];
        }

        public int TargetDistanceAt(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= _fromTarget.Length)
            {
                return AiConstants.UnreachableCm;
            }

            return _fromTarget[nodeIndex];
        }

        private void CopyDistances(int[] dest)
        {
            for (int i = 0; i < dest.Length; i++)
            {
                long d = _dist[i];
                dest[i] = d > int.MaxValue ? AiConstants.UnreachableCm : (int)d;
            }
        }

        private AiSupplyResult SearchFromMany(
            RoadGraph graph,
            int[] sourceNodeIds,
            int sourceCount,
            AiFactionState faction)
        {
            if (sourceNodeIds == null || sourceCount < 1)
            {
                return AiSupplyResult.Unreachable();
            }

            int nodeCount = graph.NodeCount;
            for (int i = 0; i < nodeCount; i++)
            {
                _dist[i] = long.MaxValue / 4;
                _seen[i] = false;
            }

            _heap.Clear();
            int seeded = 0;
            for (int s = 0; s < sourceCount; s++)
            {
                if (!graph.TryGetNodeIndex(sourceNodeIds[s], out int index))
                {
                    continue;
                }

                if (_dist[index] == 0)
                {
                    continue;
                }

                _dist[index] = 0;
                _heap.Push(0, graph.GetNodeId(index), index);
                seeded++;
            }

            if (seeded < 1)
            {
                return AiSupplyResult.Unreachable();
            }

            while (_heap.TryPop(out int index, out long f))
            {
                if (_seen[index])
                {
                    continue;
                }

                if (!TryCharge(faction))
                {
                    if (faction.SupplyBudgetDefers < int.MaxValue)
                    {
                        faction.SupplyBudgetDefers++;
                    }

                    return AiSupplyResult.BudgetExhausted();
                }

                _seen[index] = true;
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

            return AiSupplyResult.Reached(0);
        }

        public bool ConnectedToNetwork(
            RoadGraph graph,
            int fromNodeId,
            int[] networkNodeIds,
            int networkCount,
            AiFactionState faction)
        {
            return DistanceToNearestCm(graph, fromNodeId, networkNodeIds, networkCount, faction).IsReached;
        }

        /// <summary>
        /// One-source Dijkstra from <paramref name="fromIndex"/>. When
        /// <paramref name="stopIndex"/> is a node index, stop on that node.
        /// When <paramref name="networkNodeIds"/> is set, stop as soon as any
        /// network node is settled (one multi-source-equivalent pass).
        /// </summary>
        private AiSupplyResult Search(
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
                    if (faction.SupplyBudgetDefers < int.MaxValue)
                    {
                        faction.SupplyBudgetDefers++;
                    }

                    return AiSupplyResult.BudgetExhausted();
                }

                _seen[index] = true;
                if (f != _dist[index])
                {
                    continue;
                }

                if (stopIndex >= 0 && index == stopIndex)
                {
                    return ReachedOf(index);
                }

                if (stopOnNetwork && IsNetworkIndex(graph, index, networkNodeIds, networkCount))
                {
                    return ReachedOf(index);
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

            return AiSupplyResult.Unreachable();
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

        private AiSupplyResult ReachedOf(int index)
        {
            if (_dist[index] > int.MaxValue)
            {
                return AiSupplyResult.Unreachable();
            }

            return AiSupplyResult.Reached((int)_dist[index]);
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
            _fromNet = new int[nodeCount];
            _fromTarget = new int[nodeCount];
            _seen = new bool[nodeCount];
        }
    }
}
