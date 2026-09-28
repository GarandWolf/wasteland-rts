using System;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// ALT landmarks: per-class distances from k landmarks on the uncut road graph
    /// at uniform ppt=1000. Still admissible after a span cut (actual distances only grow)
    /// and after weights ≥ 1000; scaled by min weight otherwise.
    /// </summary>
    internal sealed class RoadLandmarks
    {
        internal const long Inf = long.MaxValue / 4;

        public readonly int Count;
        public readonly int[] LandmarkNodeIndex;
        public readonly long[] Dist;

        private RoadLandmarks(int count, int[] landmarkNodeIndex, long[] dist)
        {
            Count = count;
            LandmarkNodeIndex = landmarkNodeIndex;
            Dist = dist;
        }

        public long EstimateBytes
        {
            get { return (long)Dist.Length * 8 + LandmarkNodeIndex.Length * 4; }
        }

        public long Alt(int fromIndex, int toIndex)
        {
            long best = 0;
            int n = LandmarkNodeIndex.Length;
            int nodes = Dist.Length / System.Math.Max(n, 1);
            for (int i = 0; i < n; i++)
            {
                long du = Dist[i * nodes + fromIndex];
                long dt = Dist[i * nodes + toIndex];
                if (du >= Inf || dt >= Inf)
                {
                    continue;
                }

                long alt = du > dt ? du - dt : dt - du;
                if (alt > best)
                {
                    best = alt;
                }
            }

            return best;
        }

        public static RoadLandmarks Build(RoadGraph graph, int[] roadTicks, int landmarkCount)
        {
            int nodeCount = graph.NodeCount;
            if (nodeCount == 0 || landmarkCount <= 0)
            {
                return new RoadLandmarks(0, Array.Empty<int>(), Array.Empty<long>());
            }

            int k = landmarkCount;
            if (k > nodeCount)
            {
                k = nodeCount;
            }

            int[] landmarks = new int[k];
            long[] minDist = new long[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                minDist[i] = Inf;
            }

            long[] scratchG = new long[nodeCount];
            int[] scratchGen = new int[nodeCount];
            int searchId = 0;
            MinHeap heap = new MinHeap(nodeCount);
            landmarks[0] = 0;
            Dijkstra(graph, roadTicks, 0, scratchG, scratchGen, ref searchId, heap);
            CopyMin(minDist, scratchG, scratchGen, searchId, nodeCount);

            for (int i = 1; i < k; i++)
            {
                int best = 0;
                long bestD = -1;
                for (int n = 0; n < nodeCount; n++)
                {
                    long d = minDist[n];
                    if (d >= Inf)
                    {
                        d = 0;
                    }

                    if (d > bestD || (d == bestD && n < best))
                    {
                        bestD = d;
                        best = n;
                    }
                }

                landmarks[i] = best;
                Dijkstra(graph, roadTicks, best, scratchG, scratchGen, ref searchId, heap);
                CopyMin(minDist, scratchG, scratchGen, searchId, nodeCount);
            }

            long[] dist = new long[k * nodeCount];
            for (int i = 0; i < k; i++)
            {
                Dijkstra(graph, roadTicks, landmarks[i], scratchG, scratchGen, ref searchId, heap);
                int baseIndex = i * nodeCount;
                for (int n = 0; n < nodeCount; n++)
                {
                    dist[baseIndex + n] = scratchGen[n] == searchId ? scratchG[n] : Inf;
                }
            }

            return new RoadLandmarks(k, landmarks, dist);
        }

        private static void CopyMin(long[] minDist, long[] g, int[] gen, int searchId, int nodeCount)
        {
            for (int n = 0; n < nodeCount; n++)
            {
                long d = gen[n] == searchId ? g[n] : Inf;
                if (d < minDist[n])
                {
                    minDist[n] = d;
                }
            }
        }

        private static void Dijkstra(
            RoadGraph graph,
            int[] roadTicks,
            int source,
            long[] g,
            int[] gen,
            ref int searchId,
            MinHeap heap)
        {
            searchId++;
            if (searchId == int.MaxValue)
            {
                Array.Clear(gen, 0, gen.Length);
                searchId = 1;
            }

            heap.Clear();
            g[source] = 0;
            gen[source] = searchId;
            heap.Push(0, source, source);
            while (heap.TryPop(out int current, out _))
            {
                if (gen[current] != searchId)
                {
                    continue;
                }

                int adjStart = graph.AdjOffset[current];
                int adjEnd = graph.AdjOffset[current + 1];
                for (int a = adjStart; a < adjEnd; a++)
                {
                    int edge = graph.AdjEdgeIndex[a];
                    int ticks = roadTicks[edge];
                    if (ticks <= 0)
                    {
                        continue;
                    }

                    int next = graph.AdjNeighborIndex[a];
                    long ng = g[current] + ticks;
                    if (gen[next] != searchId || ng < g[next])
                    {
                        gen[next] = searchId;
                        g[next] = ng;
                        heap.Push(ng, next, next);
                    }
                }
            }
        }
    }
}
