using System;
using System.Diagnostics;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Benchmarks
{
    /// <summary>
    /// Micro-benchmark for road ALT, HPA* off-road, and expansion-capped queue ticks.
    /// Not a CI gate: <c>dotnet run -c Release --project src/Wasteland.Sim.Benchmarks</c>.
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            MovementTables tables = MovementTables.Default();
            BenchmarkRoads(tables);
            BenchmarkGrid(tables, 1600);
            BenchmarkGrid(tables, 3200);
            return 0;
        }

        private static void PrintLoad(MovementLoadStats s)
        {
            Console.WriteLine(
                "  load {0} ms  clusters={1} size={2} absNodes/class={3} absEdges/class={4} landmarks={5}",
                s.LoadMilliseconds,
                s.ClusterCount,
                s.ClusterSize,
                s.AbstractNodesPerClass,
                s.AbstractDirectedEdgesPerClass,
                s.LandmarkCount);
            Console.WriteLine(
                "  memory/class: costGrid={0:F1} MB  hierarchy={1:F1} MB  landmarks={2:F1} MB  scratch={3:F1} MB",
                s.CostGridBytesPerClass / 1048576.0,
                s.HierarchyBytesPerClass / 1048576.0,
                s.LandmarkBytesPerClass / 1048576.0,
                s.ScratchBytes / 1048576.0);
        }

        private static void BenchmarkRoads(MovementTables tables)
        {
            const int side = 90;
            RoadGraphBuilder roads = new RoadGraphBuilder();
            int cell = MovementConfig.DefaultCellSizeCm;
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int id = y * side + x + 1;
                    roads.AddNode(id, x * cell + cell / 2, y * cell + cell / 2);
                    if (x > 0)
                    {
                        int left = y * side + (x - 1) + 1;
                        roads.AddEdge(id * 2, left, id, cell, RoadTier.Paved, RoadSurface.Asphalt);
                    }

                    if (y > 0)
                    {
                        int below = (y - 1) * side + x + 1;
                        roads.AddEdge(id * 2 + 1, below, id, cell, RoadTier.Paved, RoadSurface.Asphalt);
                    }
                }
            }

            TerrainGridBuilder terrain = new TerrainGridBuilder(side, side).Fill(0, LandCover.Field);
            Console.WriteLine("Building {0}×{0} road graph ({1} nodes)...", side, side * side);
            MovementService svc = MovementService.Create(roads.ToBlob(), terrain.ToBlob(), tables);
            PrintLoad(svc.LoadStats);
            Console.WriteLine("  nodes={0} edges={1}", svc.Graph.NodeCount, svc.Graph.EdgeCount);

            int from = 1;
            int to = side * side;
            RoadCostWeights weights = RoadCostWeights.Uniform();
            Stopwatch warm = Stopwatch.StartNew();
            PathResult first = svc.PathOnRoads(from, to, MovementClass.Wheeled, weights);
            warm.Stop();
            Console.WriteLine(
                "  first PathOnRoads reachable={0} cost={1} expansions={2} {3} ms hash=0x{4:X16}",
                first.Reachable,
                first.TotalCostTicks,
                svc.LastExpansions,
                warm.ElapsedMilliseconds,
                first.Hash);

            const int repeats = 40;
            int[] froms = new int[repeats];
            int[] tos = new int[repeats];
            for (int i = 0; i < repeats; i++)
            {
                froms[i] = 1 + i * 17;
                tos[i] = side * side - i * 13;
                if (tos[i] <= froms[i])
                {
                    tos[i] = froms[i] + 50;
                }
            }

            long expSum = 0;
            Stopwatch search = Stopwatch.StartNew();
            ulong mix = 0;
            for (int i = 0; i < repeats; i++)
            {
                PathResult r = svc.PathOnRoads(froms[i], tos[i], MovementClass.Wheeled, weights);
                mix ^= r.Hash;
                expSum += svc.LastExpansions;
            }

            search.Stop();
            Console.WriteLine(
                "  {0} unique PathOnRoads {1} ms ({2:F3} ms/query, {3:F1} expansions/query) mix=0x{4:X16}",
                repeats,
                search.ElapsedMilliseconds,
                search.Elapsed.TotalMilliseconds / repeats,
                expSum / (double)repeats,
                mix);
        }

        private static void BenchmarkGrid(MovementTables tables, int side)
        {
            Console.WriteLine("Building {0}×{0} terrain grid ({1} cells)...", side, side * side);
            TerrainGridBuilder terrain = new TerrainGridBuilder(side, side).Fill(0, LandCover.Field);
            RoadGraphBuilder roads = new RoadGraphBuilder().AddNode(1, 5000, 5000);
            MovementService svc = MovementService.Create(roads.ToBlob(), terrain.ToBlob(), tables);
            PrintLoad(svc.LoadStats);

            int fromX = svc.Terrain.CellCenterXCm(0);
            int fromY = svc.Terrain.CellCenterYCm(0);
            int toX = svc.Terrain.CellCenterXCm(side - 1);
            int toY = svc.Terrain.CellCenterYCm(side - 1);
            Stopwatch first = Stopwatch.StartNew();
            PathResult corner = svc.PathOffRoad(fromX, fromY, toX, toY, MovementClass.Foot);
            first.Stop();
            Console.WriteLine(
                "  corner-to-corner foot reachable={0} cost={1} waypoints={2} expansions={3} {4} ms hash=0x{5:X16}",
                corner.Reachable,
                corner.TotalCostTicks,
                corner.Waypoints.Length,
                svc.LastExpansions,
                first.ElapsedMilliseconds,
                corner.Hash);

            const int n = 8;
            long expSum = 0;
            Stopwatch eight = Stopwatch.StartNew();
            ulong mix = 0;
            for (int i = 0; i < n; i++)
            {
                int x1 = svc.Terrain.CellCenterXCm(System.Math.Min(i * (side / 16), side - 1));
                int y1 = svc.Terrain.CellCenterYCm(System.Math.Min(i * (side / 20), side - 1));
                int x2 = svc.Terrain.CellCenterXCm(System.Math.Max(side - 1 - i * (side / 18), 0));
                int y2 = svc.Terrain.CellCenterYCm(System.Math.Max(side - 1 - i * (side / 22), 0));
                PathResult r = svc.PathOffRoad(x1, y1, x2, y2, MovementClass.Wheeled);
                mix ^= r.Hash;
                expSum += svc.LastExpansions;
            }

            eight.Stop();
            Console.WriteLine(
                "  {0} unique wheeled PathOffRoad {1} ms ({2:F3} ms/query, {3:F1} expansions/query) mix=0x{4:X16}",
                n,
                eight.ElapsedMilliseconds,
                eight.Elapsed.TotalMilliseconds / n,
                expSum / (double)n,
                mix);

            MovementService queued = MovementService.Create(
                roads.ToBlob(),
                terrain.ToBlob(),
                tables,
                MovementServiceConfig.Default);
            int qid = queued.Enqueue(PathRequest.OffRoad(fromX, fromY, toX, toY, MovementClass.Foot));
            int ticks = 0;
            long maxExp = 0;
            long maxTickMs = 0;
            Stopwatch tickClock = new Stopwatch();
            while (!queued.TryGetResult(qid, out _) && ticks < 100000)
            {
                tickClock.Restart();
                queued.ProcessQueue();
                tickClock.Stop();
                if (queued.ExpansionsThisTick > maxExp)
                {
                    maxExp = queued.ExpansionsThisTick;
                }

                if (tickClock.ElapsedMilliseconds > maxTickMs)
                {
                    maxTickMs = tickClock.ElapsedMilliseconds;
                }

                ticks++;
            }

            queued.TryGetResult(qid, out PathResult committed);
            Console.WriteLine(
                "  sliced corner: ticks={0} commitTick={1} maxExpansions/tick={2} (cap {3}) worstTick={4} ms reachable={5}",
                ticks,
                committed.CommitTick,
                maxExp,
                queued.ExpansionsPerTick,
                maxTickMs,
                committed.Reachable);
        }
    }
}
