using System;
using System.Collections.Generic;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// HPA*-style abstract graph for one movement class, built at match load.
    /// Maps that fit in a single cluster leave this unused so goldens stay exact A*.
    /// </summary>
    internal sealed class GridHierarchy
    {
        internal const long Inf = long.MaxValue / 4;

        public readonly int ClusterSize;
        public readonly int ClustersX;
        public readonly int ClustersY;
        public readonly int NodeCount;
        public readonly int[] NodeCell;
        public readonly int[] NodeCluster;
        public readonly int[] ClusterNodeOffset;
        public readonly int[] ClusterNodes;
        public readonly int[] AdjOffset;
        public readonly int[] AdjNeighbor;
        public readonly int[] AdjCost;
        public readonly byte[] AdjInter;

        private GridHierarchy(
            int clusterSize,
            int clustersX,
            int clustersY,
            int[] nodeCell,
            int[] nodeCluster,
            int[] clusterNodeOffset,
            int[] clusterNodes,
            int[] adjOffset,
            int[] adjNeighbor,
            int[] adjCost,
            byte[] adjInter)
        {
            ClusterSize = clusterSize;
            ClustersX = clustersX;
            ClustersY = clustersY;
            NodeCount = nodeCell.Length;
            NodeCell = nodeCell;
            NodeCluster = nodeCluster;
            ClusterNodeOffset = clusterNodeOffset;
            ClusterNodes = clusterNodes;
            AdjOffset = adjOffset;
            AdjNeighbor = adjNeighbor;
            AdjCost = adjCost;
            AdjInter = adjInter;
        }

        public int ClusterCount
        {
            get { return ClustersX * ClustersY; }
        }

        public long EstimateBytes
        {
            get
            {
                return (long)NodeCount * 8
                    + ClusterNodeOffset.Length * 4
                    + ClusterNodes.Length * 4
                    + AdjNeighbor.Length * 9L;
            }
        }

        public int ClusterOfCell(int cell, int mapWidth)
        {
            int x = cell % mapWidth;
            int y = cell / mapWidth;
            int cx = x / ClusterSize;
            int cy = y / ClusterSize;
            return cy * ClustersX + cx;
        }

        public void ClusterBounds(int cluster, int mapWidth, int mapHeight, out int x0, out int y0, out int x1, out int y1)
        {
            int cx = cluster % ClustersX;
            int cy = cluster / ClustersX;
            x0 = cx * ClusterSize;
            y0 = cy * ClusterSize;
            x1 = x0 + ClusterSize;
            y1 = y0 + ClusterSize;
            if (x1 > mapWidth)
            {
                x1 = mapWidth;
            }

            if (y1 > mapHeight)
            {
                y1 = mapHeight;
            }
        }

        public static GridHierarchy Build(TerrainGrid terrain, int[] cellTicks, int clusterSize)
        {
            int width = terrain.Width;
            int height = terrain.Height;
            int clustersX = (width + clusterSize - 1) / clusterSize;
            int clustersY = (height + clusterSize - 1) / clusterSize;
            if (clustersX < 1)
            {
                clustersX = 1;
            }

            if (clustersY < 1)
            {
                clustersY = 1;
            }

            List<int> nodeCell = new List<int>();
            List<int> nodeCluster = new List<int>();
            int clusterCount = clustersX * clustersY;
            List<int>[] perCluster = new List<int>[clusterCount];
            for (int i = 0; i < clusterCount; i++)
            {
                perCluster[i] = new List<int>();
            }

            Dictionary<int, int> cellToNode = new Dictionary<int, int>();
            for (int cy = 0; cy < clustersY; cy++)
            {
                for (int cx = 0; cx < clustersX; cx++)
                {
                    int cluster = cy * clustersX + cx;
                    int x0 = cx * clusterSize;
                    int y0 = cy * clusterSize;
                    int x1 = x0 + clusterSize;
                    int y1 = y0 + clusterSize;
                    if (x1 > width)
                    {
                        x1 = width;
                    }

                    if (y1 > height)
                    {
                        y1 = height;
                    }

                    AddBorderEntrances(cellTicks, width, height, cluster, x0, y0, x1, y1, 0, -1, cellToNode, nodeCell, nodeCluster, perCluster);
                    AddBorderEntrances(cellTicks, width, height, cluster, x0, y0, x1, y1, 1, 0, cellToNode, nodeCell, nodeCluster, perCluster);
                    AddBorderEntrances(cellTicks, width, height, cluster, x0, y0, x1, y1, 0, 1, cellToNode, nodeCell, nodeCluster, perCluster);
                    AddBorderEntrances(cellTicks, width, height, cluster, x0, y0, x1, y1, -1, 0, cellToNode, nodeCell, nodeCluster, perCluster);
                }
            }

            int[] clusterNodeOffset = new int[clusterCount + 1];
            int totalCn = 0;
            for (int i = 0; i < clusterCount; i++)
            {
                clusterNodeOffset[i] = totalCn;
                totalCn += perCluster[i].Count;
            }

            clusterNodeOffset[clusterCount] = totalCn;
            int[] clusterNodes = new int[totalCn];
            for (int i = 0; i < clusterCount; i++)
            {
                List<int> list = perCluster[i];
                list.Sort();
                int dst = clusterNodeOffset[i];
                for (int k = 0; k < list.Count; k++)
                {
                    clusterNodes[dst + k] = list[k];
                }
            }

            List<int> edgeFrom = new List<int>();
            List<int> edgeTo = new List<int>();
            List<int> edgeCost = new List<int>();
            List<byte> edgeInter = new List<byte>();

            int nodeCount = nodeCell.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                int cell = nodeCell[i];
                int x = cell % width;
                int y = cell / width;
                int cluster = nodeCluster[i];
                for (int d = 0; d < 8; d++)
                {
                    TryInter(
                        cellTicks,
                        width,
                        height,
                        cellToNode,
                        nodeCluster,
                        cluster,
                        i,
                        x,
                        y,
                        x + GridDirs.X[d],
                        y + GridDirs.Y[d],
                        GridDirs.X[d] != 0 && GridDirs.Y[d] != 0,
                        edgeFrom,
                        edgeTo,
                        edgeCost,
                        edgeInter);
                }
            }

            int clusterCells = clusterSize * clusterSize;
            long[] g = new long[clusterCells];
            int[] gen = new int[clusterCells];
            int searchId = 0;
            MinHeap heap = new MinHeap(clusterCells);
            for (int cluster = 0; cluster < clusterCount; cluster++)
            {
                int start = clusterNodeOffset[cluster];
                int end = clusterNodeOffset[cluster + 1];
                int count = end - start;
                if (count <= 1)
                {
                    continue;
                }

                int cx = cluster % clustersX;
                int cy = cluster / clustersX;
                int x0 = cx * clusterSize;
                int y0 = cy * clusterSize;
                int x1 = x0 + clusterSize;
                int y1 = y0 + clusterSize;
                if (x1 > width)
                {
                    x1 = width;
                }

                if (y1 > height)
                {
                    y1 = height;
                }

                int localW = x1 - x0;
                int localH = y1 - y0;
                for (int a = start; a < end; a++)
                {
                    int fromNode = clusterNodes[a];
                    int fromCell = nodeCell[fromNode];
                    DijkstraCluster(
                        cellTicks,
                        width,
                        fromCell,
                        x0,
                        y0,
                        localW,
                        localH,
                        g,
                        gen,
                        ref searchId,
                        heap);
                    for (int b = a + 1; b < end; b++)
                    {
                        int toNode = clusterNodes[b];
                        int toCell = nodeCell[toNode];
                        int tx = toCell % width;
                        int ty = toCell / width;
                        int local = (tx - x0) + (ty - y0) * localW;
                        if (gen[local] != searchId || g[local] >= Inf)
                        {
                            continue;
                        }

                        int cost = g[local] > int.MaxValue ? int.MaxValue : (int)g[local];
                        if (cost < 1)
                        {
                            cost = 1;
                        }

                        edgeFrom.Add(fromNode);
                        edgeTo.Add(toNode);
                        edgeCost.Add(cost);
                        edgeInter.Add(0);
                    }
                }
            }

            BuildCsr(nodeCount, edgeFrom, edgeTo, edgeCost, edgeInter, out int[] adjOffset, out int[] adjNeighbor, out int[] adjCost, out byte[] adjInter);
            return new GridHierarchy(
                clusterSize,
                clustersX,
                clustersY,
                nodeCell.ToArray(),
                nodeCluster.ToArray(),
                clusterNodeOffset,
                clusterNodes,
                adjOffset,
                adjNeighbor,
                adjCost,
                adjInter);
        }

        private static void AddBorderEntrances(
            int[] cellTicks,
            int width,
            int height,
            int cluster,
            int x0,
            int y0,
            int x1,
            int y1,
            int dx,
            int dy,
            Dictionary<int, int> cellToNode,
            List<int> nodeCell,
            List<int> nodeCluster,
            List<int>[] perCluster)
        {
            bool horizontal = dy == 0;
            int runStart = -1;
            int runCount = 0;
            if (horizontal)
            {
                int x = dx > 0 ? x1 - 1 : x0;
                int nx = x + dx;
                if ((uint)nx >= (uint)width)
                {
                    return;
                }

                for (int y = y0; y < y1; y++)
                {
                    bool ok = IsEntrancePair(cellTicks, width, height, x, y, nx, y);
                    if (ok)
                    {
                        if (runStart < 0)
                        {
                            runStart = y;
                            runCount = 0;
                        }

                        runCount++;
                    }
                    else if (runStart >= 0)
                    {
                        AddRunEntrances(cellTicks, width, true, x, runStart, runCount, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
                        runStart = -1;
                    }
                }

                if (runStart >= 0)
                {
                    AddRunEntrances(cellTicks, width, true, x, runStart, runCount, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
                }
            }
            else
            {
                int y = dy > 0 ? y1 - 1 : y0;
                int ny = y + dy;
                if ((uint)ny >= (uint)height)
                {
                    return;
                }

                for (int x = x0; x < x1; x++)
                {
                    bool ok = IsEntrancePair(cellTicks, width, height, x, y, x, ny);
                    if (ok)
                    {
                        if (runStart < 0)
                        {
                            runStart = x;
                            runCount = 0;
                        }

                        runCount++;
                    }
                    else if (runStart >= 0)
                    {
                        AddRunEntrances(cellTicks, width, false, y, runStart, runCount, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
                        runStart = -1;
                    }
                }

                if (runStart >= 0)
                {
                    AddRunEntrances(cellTicks, width, false, y, runStart, runCount, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
                }
            }
        }

        private static void AddRunEntrances(
            int[] cellTicks,
            int width,
            bool horizontal,
            int fixedCoord,
            int runStart,
            int runCount,
            int cluster,
            Dictionary<int, int> cellToNode,
            List<int> nodeCell,
            List<int> nodeCluster,
            List<int>[] perCluster)
        {
            if (runCount <= 0)
            {
                return;
            }

            AddRunEntranceAt(cellTicks, width, horizontal, fixedCoord, runStart, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
            AddRunEntranceAt(cellTicks, width, horizontal, fixedCoord, runStart + runCount - 1, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
            AddRunEntranceAt(cellTicks, width, horizontal, fixedCoord, runStart + runCount / 2, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
            int interval = MovementConfig.DefaultEntranceInterval;
            if (interval < 1)
            {
                interval = 1;
            }

            for (int i = 0; i < runCount; i += interval)
            {
                AddRunEntranceAt(cellTicks, width, horizontal, fixedCoord, runStart + i, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
            }
        }

        private static void AddRunEntranceAt(
            int[] cellTicks,
            int width,
            bool horizontal,
            int fixedCoord,
            int varying,
            int cluster,
            Dictionary<int, int> cellToNode,
            List<int> nodeCell,
            List<int> nodeCluster,
            List<int>[] perCluster)
        {
            if (horizontal)
            {
                AddEntrance(cellTicks, width, fixedCoord, varying, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
            }
            else
            {
                AddEntrance(cellTicks, width, varying, fixedCoord, cluster, cellToNode, nodeCell, nodeCluster, perCluster);
            }
        }

        private static bool IsEntrancePair(int[] cellTicks, int width, int height, int x, int y, int nx, int ny)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height || (uint)nx >= (uint)width || (uint)ny >= (uint)height)
            {
                return false;
            }

            int a = y * width + x;
            int b = ny * width + nx;
            return cellTicks[a] > 0 && cellTicks[b] > 0;
        }

        private static void AddEntrance(
            int[] cellTicks,
            int width,
            int x,
            int y,
            int cluster,
            Dictionary<int, int> cellToNode,
            List<int> nodeCell,
            List<int> nodeCluster,
            List<int>[] perCluster)
        {
            int cell = y * width + x;
            if (cellTicks[cell] <= 0)
            {
                return;
            }

            if (cellToNode.ContainsKey(cell))
            {
                return;
            }

            int id = nodeCell.Count;
            cellToNode[cell] = id;
            nodeCell.Add(cell);
            nodeCluster.Add(cluster);
            perCluster[cluster].Add(id);
        }

        private static void TryInter(
            int[] cellTicks,
            int width,
            int height,
            Dictionary<int, int> cellToNode,
            List<int> nodeCluster,
            int fromCluster,
            int fromNode,
            int x,
            int y,
            int nx,
            int ny,
            bool diagonal,
            List<int> edgeFrom,
            List<int> edgeTo,
            List<int> edgeCost,
            List<byte> edgeInter)
        {
            if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
            {
                return;
            }

            int toCell = ny * width + nx;
            if (!cellToNode.TryGetValue(toCell, out int toNode) || toNode <= fromNode)
            {
                return;
            }

            if (nodeCluster[toNode] == fromCluster)
            {
                return;
            }

            int cost = cellTicks[toCell];
            if (cost <= 0)
            {
                return;
            }

            if (diagonal)
            {
                int orthoA = y * width + nx;
                int orthoB = ny * width + x;
                if (cellTicks[orthoA] <= 0 || cellTicks[orthoB] <= 0)
                {
                    return;
                }

                cost = (int)((long)cost * MovementConfig.Sqrt2Ppt / SimConfig.PptScale);
                if (cost < 1)
                {
                    cost = 1;
                }
            }

            edgeFrom.Add(fromNode);
            edgeTo.Add(toNode);
            edgeCost.Add(cost);
            edgeInter.Add(1);
        }

        private static void DijkstraCluster(
            int[] cellTicks,
            int width,
            int fromCell,
            int x0,
            int y0,
            int localW,
            int localH,
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
            int fx = fromCell % width;
            int fy = fromCell / width;
            int fromLocal = (fx - x0) + (fy - y0) * localW;
            g[fromLocal] = 0;
            gen[fromLocal] = searchId;
            heap.Push(0, fromLocal, fromLocal);

            while (heap.TryPop(out int current, out _))
            {
                if (gen[current] != searchId)
                {
                    continue;
                }

                int cx = x0 + current % localW;
                int cy = y0 + current / localW;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + GridDirs.X[d];
                    int ny = cy + GridDirs.Y[d];
                    if (nx < x0 || ny < y0 || nx >= x0 + localW || ny >= y0 + localH)
                    {
                        continue;
                    }

                    int nextCell = ny * width + nx;
                    int step = cellTicks[nextCell];
                    if (step <= 0)
                    {
                        continue;
                    }

                    bool diagonal = GridDirs.X[d] != 0 && GridDirs.Y[d] != 0;
                    if (diagonal)
                    {
                        int orthoA = cy * width + nx;
                        int orthoB = ny * width + cx;
                        if (cellTicks[orthoA] <= 0 || cellTicks[orthoB] <= 0)
                        {
                            continue;
                        }

                        step = (int)((long)step * MovementConfig.Sqrt2Ppt / SimConfig.PptScale);
                        if (step < 1)
                        {
                            step = 1;
                        }
                    }

                    int nextLocal = (nx - x0) + (ny - y0) * localW;
                    long ng = g[current] + step;
                    if (gen[nextLocal] != searchId || ng < g[nextLocal])
                    {
                        gen[nextLocal] = searchId;
                        g[nextLocal] = ng;
                        heap.Push(ng, nextLocal, nextLocal);
                    }
                }
            }
        }

        private static void BuildCsr(
            int nodeCount,
            List<int> edgeFrom,
            List<int> edgeTo,
            List<int> edgeCost,
            List<byte> edgeInter,
            out int[] adjOffset,
            out int[] adjNeighbor,
            out int[] adjCost,
            out byte[] adjInter)
        {
            int undirected = edgeFrom.Count;
            int[] degree = new int[nodeCount];
            for (int i = 0; i < undirected; i++)
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
            adjNeighbor = new int[total];
            adjCost = new int[total];
            adjInter = new byte[total];
            int[] cursor = new int[nodeCount];
            Array.Copy(adjOffset, cursor, nodeCount);
            for (int i = 0; i < undirected; i++)
            {
                int a = edgeFrom[i];
                int b = edgeTo[i];
                int c = edgeCost[i];
                byte inter = edgeInter[i];
                int pa = cursor[a]++;
                adjNeighbor[pa] = b;
                adjCost[pa] = c;
                adjInter[pa] = inter;
                int pb = cursor[b]++;
                adjNeighbor[pb] = a;
                adjCost[pb] = c;
                adjInter[pb] = inter;
            }

            List<int> order = new List<int>();
            for (int n = 0; n < nodeCount; n++)
            {
                int start = adjOffset[n];
                int count = adjOffset[n + 1] - start;
                if (count <= 1)
                {
                    continue;
                }

                order.Clear();
                for (int k = 0; k < count; k++)
                {
                    order.Add(start + k);
                }

                int[] neighbor = adjNeighbor;
                int[] cost = adjCost;
                byte[] inter = adjInter;
                order.Sort(delegate (int ia, int ib)
                {
                    int c = neighbor[ia].CompareTo(neighbor[ib]);
                    if (c != 0)
                    {
                        return c;
                    }

                    return cost[ia].CompareTo(cost[ib]);
                });

                int[] nCopy = new int[count];
                int[] cCopy = new int[count];
                byte[] iCopy = new byte[count];
                for (int k = 0; k < count; k++)
                {
                    nCopy[k] = neighbor[order[k]];
                    cCopy[k] = cost[order[k]];
                    iCopy[k] = inter[order[k]];
                }

                for (int k = 0; k < count; k++)
                {
                    neighbor[start + k] = nCopy[k];
                    cost[start + k] = cCopy[k];
                    inter[start + k] = iCopy[k];
                }
            }
        }
    }

    internal static class GridDirs
    {
        public static readonly int[] X = { 0, 1, 0, -1, 1, 1, -1, -1 };
        public static readonly int[] Y = { 1, 0, -1, 0, 1, -1, -1, 1 };
    }
}
