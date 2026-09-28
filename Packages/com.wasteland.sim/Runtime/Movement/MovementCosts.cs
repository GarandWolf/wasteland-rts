namespace Wasteland.Sim.Movement
{
    /// <summary>Per-class cost grids and road speeds, computed at match load.</summary>
    internal sealed class MovementCosts
    {
        public readonly int[][] CellTicks;
        public readonly int[][] RoadSpeedCmPerTick;
        public readonly int[][] RoadTicks;
        public readonly int[] MaxGridSpeed;
        public readonly int[] MaxRoadSpeed;
        public readonly int[] NodeCellIndex;

        private MovementCosts(
            int[][] cellTicks,
            int[][] roadSpeedCmPerTick,
            int[][] roadTicks,
            int[] maxGridSpeed,
            int[] maxRoadSpeed,
            int[] nodeCellIndex)
        {
            CellTicks = cellTicks;
            RoadSpeedCmPerTick = roadSpeedCmPerTick;
            RoadTicks = roadTicks;
            MaxGridSpeed = maxGridSpeed;
            MaxRoadSpeed = maxRoadSpeed;
            NodeCellIndex = nodeCellIndex;
        }

        public static MovementCosts Build(TerrainGrid terrain, RoadGraph graph, MovementTables tables)
        {
            int classCount = MovementTables.ClassCount;
            int cells = terrain.CellCount;
            int[][] cellTicks = new int[classCount][];
            int[] maxGrid = new int[classCount];
            for (int c = 0; c < classCount; c++)
            {
                MovementClass clas = (MovementClass)c;
                int[] row = new int[cells];
                int maxSpeed = 0;
                for (int i = 0; i < cells; i++)
                {
                    int speed = tables.OffRoadSpeedCmPerTick(clas, terrain.CoverAt(i), terrain.SlopeAt(i));
                    if (speed > maxSpeed)
                    {
                        maxSpeed = speed;
                    }

                    row[i] = speed <= 0 ? 0 : MovementTables.TicksToTraverse(terrain.CellSizeCm, speed);
                }

                cellTicks[c] = row;
                maxGrid[c] = maxSpeed;
            }

            int edges = graph.EdgeCount;
            int[][] roadSpeed = new int[classCount][];
            int[][] roadTicks = new int[classCount][];
            int[] maxRoad = new int[classCount];
            for (int c = 0; c < classCount; c++)
            {
                MovementClass clas = (MovementClass)c;
                int[] row = new int[edges];
                int[] tickRow = new int[edges];
                int maxSpeed = 0;
                for (int e = 0; e < edges; e++)
                {
                    int speed = tables.RoadSpeedCmPerTick(clas, graph.GetEdgeSurface(e));
                    row[e] = speed;
                    tickRow[e] = speed <= 0 ? 0 : MovementTables.TicksToTraverse(graph.EdgeLengthCm[e], speed);
                    if (speed > maxSpeed)
                    {
                        maxSpeed = speed;
                    }
                }

                roadSpeed[c] = row;
                roadTicks[c] = tickRow;
                maxRoad[c] = maxSpeed;
            }

            int[] nodeCell = new int[graph.NodeCount];
            for (int n = 0; n < graph.NodeCount; n++)
            {
                if (terrain.TryWorldToCell(graph.NodeXCm[n], graph.NodeYCm[n], out int cx, out int cy))
                {
                    nodeCell[n] = terrain.Index(cx, cy);
                }
                else
                {
                    nodeCell[n] = -1;
                }
            }

            return new MovementCosts(cellTicks, roadSpeed, roadTicks, maxGrid, maxRoad, nodeCell);
        }

        public bool IsPassable(MovementClass clas, int cellIndex)
        {
            return CellTicks[(int)clas][cellIndex] > 0;
        }
    }
}
