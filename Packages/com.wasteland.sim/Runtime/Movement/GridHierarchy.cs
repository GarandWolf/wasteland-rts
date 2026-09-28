using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wasteland.Sim.Hashing;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// HPA*-style abstract graph for one movement class, built at match load.
    /// Maps that fit in a single cluster leave this unused so goldens stay exact A*.
    /// Pure function of (cost grid, cluster size, algorithm version).
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

        public ulong Fingerprint()
        {
            XxHash64Builder hash = new XxHash64Builder();
            hash.AddInt(MovementConfig.HierarchyAlgorithmVersion);
            hash.AddInt(ClusterSize);
            hash.AddInt(ClustersX);
            hash.AddInt(ClustersY);
            hash.AddInt(NodeCount);
            AddInts(hash, NodeCell);
            AddInts(hash, NodeCluster);
            AddInts(hash, ClusterNodeOffset);
            AddInts(hash, ClusterNodes);
            AddInts(hash, AdjOffset);
            AddInts(hash, AdjNeighbor);
            AddInts(hash, AdjCost);
            for (int i = 0; i < AdjInter.Length; i++)
            {
                hash.AddByte(AdjInter[i]);
            }

            return hash.Digest();
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
            return Build(terrain, cellTicks, clusterSize, 0);
        }

        /// <param name="maxDegreeOfParallelism">1 = serial (for equality tests). 0 = ProcessorCount.</param>
        public static GridHierarchy Build(TerrainGrid terrain, int[] cellTicks, int clusterSize, int maxDegreeOfParallelism)
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
                perCluster[i] = new List<int>(16);
            }

            int cells = width * height;
            int[] cellToNode = new int[cells];
            for (int i = 0; i < cells; i++)
            {
                cellToNode[i] = -1;
            }

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

            int[] nodeCellArr = nodeCell.ToArray();
            int[] nodeClusterArr = nodeCluster.ToArray();
            int nodeCount = nodeCellArr.Length;

            List<int> edgeFrom = new List<int>(nodeCount * 4);
            List<int> edgeTo = new List<int>(nodeCount * 4);
            List<int> edgeCost = new List<int>(nodeCount * 4);
            List<byte> edgeInter = new List<byte>(nodeCount * 4);

            for (int i = 0; i < nodeCount; i++)
            {
                int cell = nodeCellArr[i];
                int x = cell % width;
                int y = cell / width;
                int cluster = nodeClusterArr[i];
                for (int d = 0; d < 8; d++)
                {
                    TryInter(
                        cellTicks,
                        width,
                        height,
                        cellToNode,
                        nodeClusterArr,
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

            int workers = maxDegreeOfParallelism;
            if (workers <= 0)
            {
                workers = Environment.ProcessorCount;
            }

            if (workers < 1)
            {
                workers = 1;
            }

            int[][] intraFrom = new int[clusterCount][];
            int[][] intraTo = new int[clusterCount][];
            int[][] intraCost = new int[clusterCount][];
            int clusterCells = clusterSize * clusterSize;
            if (workers == 1 || clusterCount <= 1)
            {
                ClusterScratch scratch = new ClusterScratch(clusterCells);
                for (int cluster = 0; cluster < clusterCount; cluster++)
                {
                    BuildIntra(cluster, clustersX, clusterSize, width, height, cellTicks, nodeCellArr, clusterNodeOffset, clusterNodes, scratch, intraFrom, intraTo, intraCost);
                }
            }
            else
            {
                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = workers;
                ThreadLocal<ClusterScratch> local = new ThreadLocal<ClusterScratch>(() => new ClusterScratch(clusterCells), false);
                try
                {
                    Parallel.For(0, clusterCount, options, delegate (int cluster)
                    {
                        ClusterScratch scratch = local.Value!;
                        BuildIntra(cluster, clustersX, clusterSize, width, height, cellTicks, nodeCellArr, clusterNodeOffset, clusterNodes, scratch, intraFrom, intraTo, intraCost);
                    });
                }
                finally
                {
                    local.Dispose();
                }
            }

            for (int cluster = 0; cluster < clusterCount; cluster++)
            {
                int[] from = intraFrom[cluster];
                if (from == null || from.Length == 0)
                {
                    continue;
                }

                int[] to = intraTo[cluster];
                int[] cost = intraCost[cluster];
                for (int i = 0; i < from.Length; i++)
                {
                    edgeFrom.Add(from[i]);
                    edgeTo.Add(to[i]);
                    edgeCost.Add(cost[i]);
                    edgeInter.Add(0);
                }
            }

            BuildCsr(nodeCount, edgeFrom, edgeTo, edgeCost, edgeInter, out int[] adjOffset, out int[] adjNeighbor, out int[] adjCost, out byte[] adjInter);
            return new GridHierarchy(
                clusterSize,
                clustersX,
                clustersY,
                nodeCellArr,
                nodeClusterArr,
                clusterNodeOffset,
                clusterNodes,
                adjOffset,
                adjNeighbor,
                adjCost,
                adjInter);
        }

        internal static GridHierarchy FromCache(
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
            return new GridHierarchy(
                clusterSize,
                clustersX,
                clustersY,
                nodeCell,
                nodeCluster,
                clusterNodeOffset,
                clusterNodes,
                adjOffset,
                adjNeighbor,
                adjCost,
                adjInter);
        }

        private static void BuildIntra(
            int cluster,
            int clustersX,
            int clusterSize,
            int width,
            int height,
            int[] cellTicks,
            int[] nodeCell,
            int[] clusterNodeOffset,
            int[] clusterNodes,
            ClusterScratch scratch,
            int[][] intraFrom,
            int[][] intraTo,
            int[][] intraCost)
        {
            int start = clusterNodeOffset[cluster];
            int end = clusterNodeOffset[cluster + 1];
            int count = end - start;
            if (count <= 1)
            {
                intraFrom[cluster] = Array.Empty<int>();
                intraTo[cluster] = Array.Empty<int>();
                intraCost[cluster] = Array.Empty<int>();
                return;
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
            List<int> from = scratch.EdgeFrom;
            List<int> to = scratch.EdgeTo;
            List<int> costList = scratch.EdgeCost;
            from.Clear();
            to.Clear();
            costList.Clear();
            int pairCap = count * (count - 1) / 2;
            if (from.Capacity < pairCap)
            {
                from.Capacity = pairCap;
                to.Capacity = pairCap;
                costList.Capacity = pairCap;
            }

            int uniformTick = ClusterUniformTick(cellTicks, width, x0, y0, x1, y1);
            if (uniformTick > 0)
            {
                for (int a = start; a < end - 1; a++)
                {
                    int fromNode = clusterNodes[a];
                    int fromCell = nodeCell[fromNode];
                    int fx = fromCell % width;
                    int fy = fromCell / width;
                    for (int b = a + 1; b < end; b++)
                    {
                        int toNode = clusterNodes[b];
                        int toCell = nodeCell[toNode];
                        int tx = toCell % width;
                        int ty = toCell / width;
                        int cost = OctileUniformCost(tx - fx, ty - fy, uniformTick);
                        from.Add(fromNode);
                        to.Add(toNode);
                        costList.Add(cost);
                    }
                }
            }
            else
            {
                int[] targets = scratch.Targets;
                for (int a = start; a < end - 1; a++)
                {
                    int fromNode = clusterNodes[a];
                    int fromCell = nodeCell[fromNode];
                    int targetCount = 0;
                    for (int b = a + 1; b < end; b++)
                    {
                        int toCell = nodeCell[clusterNodes[b]];
                        int tx = toCell % width;
                        int ty = toCell / width;
                        targets[targetCount++] = (tx - x0) + (ty - y0) * localW;
                    }

                    DijkstraCluster(
                        cellTicks,
                        width,
                        fromCell,
                        x0,
                        y0,
                        localW,
                        localH,
                        scratch,
                        targets,
                        targetCount);
                    for (int b = a + 1; b < end; b++)
                    {
                        int toNode = clusterNodes[b];
                        int toCell = nodeCell[toNode];
                        int tx = toCell % width;
                        int ty = toCell / width;
                        int local = (tx - x0) + (ty - y0) * localW;
                        if (scratch.Gen[local] != scratch.SearchId)
                        {
                            continue;
                        }

                        int cost = scratch.G[local];
                        if (cost < 1)
                        {
                            cost = 1;
                        }

                        from.Add(fromNode);
                        to.Add(toNode);
                        costList.Add(cost);
                    }
                }
            }

            intraFrom[cluster] = from.ToArray();
            intraTo[cluster] = to.ToArray();
            intraCost[cluster] = costList.ToArray();
        }

        private static int ClusterUniformTick(int[] cellTicks, int width, int x0, int y0, int x1, int y1)
        {
            int first = cellTicks[y0 * width + x0];
            if (first <= 0)
            {
                return 0;
            }

            for (int y = y0; y < y1; y++)
            {
                int row = y * width;
                for (int x = x0; x < x1; x++)
                {
                    if (cellTicks[row + x] != first)
                    {
                        return 0;
                    }
                }
            }

            return first;
        }

        private static int OctileUniformCost(int dx, int dy, int cellTicks)
        {
            if (dx < 0)
            {
                dx = -dx;
            }

            if (dy < 0)
            {
                dy = -dy;
            }

            int min = dx < dy ? dx : dy;
            int max = dx < dy ? dy : dx;
            if (max == 0)
            {
                return 0;
            }

            int diag = (int)((long)cellTicks * MovementConfig.Sqrt2Ppt / SimConfig.PptScale);
            if (diag < 1)
            {
                diag = 1;
            }

            int cost = min * diag + (max - min) * cellTicks;
            if (cost < 1)
            {
                cost = 1;
            }

            return cost;
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
            int[] cellToNode,
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
            int[] cellToNode,
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
            int[] cellToNode,
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
            int[] cellToNode,
            List<int> nodeCell,
            List<int> nodeCluster,
            List<int>[] perCluster)
        {
            int cell = y * width + x;
            if (cellTicks[cell] <= 0)
            {
                return;
            }

            if (cellToNode[cell] >= 0)
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
            int[] cellToNode,
            int[] nodeCluster,
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
            int toNode = cellToNode[toCell];
            if (toNode < 0 || toNode <= fromNode)
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
            ClusterScratch scratch,
            int[] targets,
            int targetCount)
        {
            scratch.SearchId++;
            if (scratch.SearchId == int.MaxValue)
            {
                Array.Clear(scratch.Gen, 0, scratch.Gen.Length);
                Array.Clear(scratch.Closed, 0, scratch.Closed.Length);
                Array.Clear(scratch.Mark, 0, scratch.Mark.Length);
                scratch.SearchId = 1;
            }

            int searchId = scratch.SearchId;
            int[] g = scratch.G;
            int[] gen = scratch.Gen;
            int[] closed = scratch.Closed;
            int[] mark = scratch.Mark;
            ClusterHeap heap = scratch.Heap;
            heap.Clear();

            int remaining = 0;
            for (int i = 0; i < targetCount; i++)
            {
                int t = targets[i];
                if (mark[t] != searchId)
                {
                    mark[t] = searchId;
                    remaining++;
                }
            }

            if (remaining <= 0)
            {
                return;
            }

            int fx = fromCell % width;
            int fy = fromCell / width;
            int fromLocal = (fx - x0) + (fy - y0) * localW;
            g[fromLocal] = 0;
            gen[fromLocal] = searchId;
            heap.Push(0, fromLocal);

            int x1 = x0 + localW;
            int y1 = y0 + localH;
            int[] dirX = GridDirs.X;
            int[] dirY = GridDirs.Y;
            while (heap.TryPop(out int current, out int popped))
            {
                if (gen[current] != searchId || closed[current] == searchId)
                {
                    continue;
                }

                if (popped != g[current])
                {
                    continue;
                }

                closed[current] = searchId;
                if (mark[current] == searchId)
                {
                    remaining--;
                    if (remaining <= 0)
                    {
                        break;
                    }
                }

                int cx = x0 + current % localW;
                int cy = y0 + current / localW;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dirX[d];
                    int ny = cy + dirY[d];
                    if (nx < x0 || ny < y0 || nx >= x1 || ny >= y1)
                    {
                        continue;
                    }

                    int nextLocal = (nx - x0) + (ny - y0) * localW;
                    if (closed[nextLocal] == searchId)
                    {
                        continue;
                    }

                    int nextCell = ny * width + nx;
                    int step = cellTicks[nextCell];
                    if (step <= 0)
                    {
                        continue;
                    }

                    bool diagonal = dirX[d] != 0 && dirY[d] != 0;
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

                    int ng = g[current] + step;
                    if (ng < 0)
                    {
                        ng = int.MaxValue;
                    }

                    if (gen[nextLocal] != searchId || ng < g[nextLocal])
                    {
                        gen[nextLocal] = searchId;
                        g[nextLocal] = ng;
                        heap.Push(ng, nextLocal);
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

            for (int n = 0; n < nodeCount; n++)
            {
                int start = adjOffset[n];
                int count = adjOffset[n + 1] - start;
                for (int i = 1; i < count; i++)
                {
                    int ni = adjNeighbor[start + i];
                    int ci = adjCost[start + i];
                    byte ii = adjInter[start + i];
                    int j = i - 1;
                    while (j >= 0)
                    {
                        int cmp = adjNeighbor[start + j].CompareTo(ni);
                        if (cmp < 0 || (cmp == 0 && adjCost[start + j] <= ci))
                        {
                            break;
                        }

                        adjNeighbor[start + j + 1] = adjNeighbor[start + j];
                        adjCost[start + j + 1] = adjCost[start + j];
                        adjInter[start + j + 1] = adjInter[start + j];
                        j--;
                    }

                    adjNeighbor[start + j + 1] = ni;
                    adjCost[start + j + 1] = ci;
                    adjInter[start + j + 1] = ii;
                }
            }
        }

        private static void AddInts(XxHash64Builder hash, int[] values)
        {
            hash.AddInt(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                hash.AddInt(values[i]);
            }
        }

        private sealed class ClusterScratch
        {
            public readonly int[] G;
            public readonly int[] Gen;
            public readonly int[] Closed;
            public readonly int[] Mark;
            public readonly int[] Targets;
            public readonly ClusterHeap Heap;
            public readonly List<int> EdgeFrom;
            public readonly List<int> EdgeTo;
            public readonly List<int> EdgeCost;
            public int SearchId;

            public ClusterScratch(int clusterCells)
            {
                G = new int[clusterCells];
                Gen = new int[clusterCells];
                Closed = new int[clusterCells];
                Mark = new int[clusterCells];
                Targets = new int[clusterCells];
                Heap = new ClusterHeap(clusterCells);
                EdgeFrom = new List<int>(64);
                EdgeTo = new List<int>(64);
                EdgeCost = new List<int>(64);
            }
        }
    }

    /// <summary>4-ary min-heap of (f, index) for intra-cluster Dijkstra. Ties do not affect costs.</summary>
    internal sealed class ClusterHeap
    {
        private int[] _f;
        private int[] _index;
        private int _count;

        public ClusterHeap(int capacity)
        {
            int cap = System.Math.Max(capacity, 8);
            _f = new int[cap];
            _index = new int[cap];
        }

        public void Clear()
        {
            _count = 0;
        }

        public void Push(int f, int index)
        {
            if (_count == _f.Length)
            {
                Grow();
            }

            int i = _count++;
            _f[i] = f;
            _index[i] = index;
            SiftUp(i);
        }

        public bool TryPop(out int index, out int f)
        {
            if (_count == 0)
            {
                index = 0;
                f = 0;
                return false;
            }

            index = _index[0];
            f = _f[0];
            _count--;
            if (_count > 0)
            {
                _f[0] = _f[_count];
                _index[0] = _index[_count];
                SiftDown(0);
            }

            return true;
        }

        private void Grow()
        {
            int cap = _f.Length * 2;
            int[] nf = new int[cap];
            int[] ni = new int[cap];
            Array.Copy(_f, 0, nf, 0, _count);
            Array.Copy(_index, 0, ni, 0, _count);
            _f = nf;
            _index = ni;
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) >> 2;
                if (!Less(i, p))
                {
                    break;
                }

                Swap(i, p);
                i = p;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int c0 = (i << 2) + 1;
                if (c0 >= _count)
                {
                    break;
                }

                int smallest = c0;
                int last = c0 + 3;
                if (last >= _count)
                {
                    last = _count - 1;
                }

                for (int c = c0 + 1; c <= last; c++)
                {
                    if (Less(c, smallest))
                    {
                        smallest = c;
                    }
                }

                if (!Less(smallest, i))
                {
                    break;
                }

                Swap(i, smallest);
                i = smallest;
            }
        }

        private bool Less(int a, int b)
        {
            int fa = _f[a];
            int fb = _f[b];
            if (fa != fb)
            {
                return fa < fb;
            }

            return _index[a] < _index[b];
        }

        private void Swap(int a, int b)
        {
            int f = _f[a];
            _f[a] = _f[b];
            _f[b] = f;
            int idx = _index[a];
            _index[a] = _index[b];
            _index[b] = idx;
        }
    }

    internal static class GridDirs
    {
        public static readonly int[] X = { 0, 1, 0, -1, 1, 1, -1, -1 };
        public static readonly int[] Y = { 1, 0, -1, 0, 1, -1, -1, 1 };
    }
}
