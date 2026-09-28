using System;
using System.Diagnostics;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Benchmarks
{
    /// <summary>
    /// Micro-benchmark for an ~8k-node road graph and a 1600×1600 cost grid.
    /// Not a CI gate: <c>dotnet run -c Release --project src/Wasteland.Sim.Benchmarks</c>.
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            MovementTables tables = MovementTables.Default();
            BenchmarkRoads(tables);
            BenchmarkGrid(tables);
            return 0;
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
            Stopwatch load = Stopwatch.StartNew();
            MovementService svc = MovementService.Create(roads.ToBlob(), terrain.ToBlob(), tables);
            load.Stop();
            Console.WriteLine("  load {0} ms, nodes={1} edges={2}", load.ElapsedMilliseconds, svc.Graph.NodeCount, svc.Graph.EdgeCount);

            int from = 1;
            int to = side * side;
            RoadCostWeights weights = RoadCostWeights.Uniform();
            Stopwatch warm = Stopwatch.StartNew();
            PathResult first = svc.PathOnRoads(from, to, MovementClass.Wheeled, weights);
            warm.Stop();
            Console.WriteLine("  first PathOnRoads reachable={0} cost={1} hash=0x{2:X16} {3} ms", first.Reachable, first.TotalCostTicks, first.Hash, warm.ElapsedMilliseconds);

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

            Stopwatch search = Stopwatch.StartNew();
            ulong mix = 0;
            for (int i = 0; i < repeats; i++)
            {
                PathResult r = svc.PathOnRoads(froms[i], tos[i], MovementClass.Wheeled, weights);
                mix ^= r.Hash;
            }

            search.Stop();
            Console.WriteLine("  {0} unique PathOnRoads {1} ms ({2:F2} ms/query) mix=0x{3:X16}", repeats, search.ElapsedMilliseconds, search.Elapsed.TotalMilliseconds / repeats, mix);
        }

        private static void BenchmarkGrid(MovementTables tables)
        {
            const int side = 1600;
            Console.WriteLine("Building {0}×{0} terrain grid ({1} cells)...", side, side * side);
            TerrainGridBuilder terrain = new TerrainGridBuilder(side, side).Fill(0, LandCover.Field);
            RoadGraphBuilder roads = new RoadGraphBuilder().AddNode(1, 5000, 5000);
            Stopwatch load = Stopwatch.StartNew();
            MovementService svc = MovementService.Create(roads.ToBlob(), terrain.ToBlob(), tables);
            load.Stop();
            Console.WriteLine("  load {0} ms", load.ElapsedMilliseconds);

            int cell = svc.Terrain.CellSizeCm;
            int fromX = cell / 2;
            int fromY = cell / 2;
            int toX = svc.Terrain.CellCenterXCm(side - 1);
            int toY = svc.Terrain.CellCenterYCm(side - 1);
            Stopwatch first = Stopwatch.StartNew();
            PathResult corner = svc.PathOffRoad(fromX, fromY, toX, toY, MovementClass.Foot);
            first.Stop();
            Console.WriteLine("  corner-to-corner foot reachable={0} cost={1} waypoints={2} {3} ms hash=0x{4:X16}", corner.Reachable, corner.TotalCostTicks, corner.Waypoints.Length, first.ElapsedMilliseconds, corner.Hash);

            Stopwatch eight = Stopwatch.StartNew();
            ulong mix = 0;
            for (int i = 0; i < 8; i++)
            {
                int x1 = svc.Terrain.CellCenterXCm(i * 100);
                int y1 = svc.Terrain.CellCenterYCm(i * 80);
                int x2 = svc.Terrain.CellCenterXCm(side - 1 - i * 90);
                int y2 = svc.Terrain.CellCenterYCm(side - 1 - i * 70);
                PathResult r = svc.PathOffRoad(x1, y1, x2, y2, MovementClass.Wheeled);
                mix ^= r.Hash;
            }

            eight.Stop();
            Console.WriteLine("  8 unique wheeled PathOffRoad {0} ms ({1:F2} ms/query) mix=0x{2:X16}", eight.ElapsedMilliseconds, eight.Elapsed.TotalMilliseconds / 8.0, mix);
        }
    }
}
