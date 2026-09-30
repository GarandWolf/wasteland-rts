using System;
using System.Collections.Generic;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// v0 stopgap: two territories are neighbours if they are road-connected —
    /// incident road edges of different <c>territoryId</c>s share a node, or
    /// towns in those territories are road-neighbours. Built at match load.
    /// Does not persist raw node or edge ids. Replace with a map-neighbour-list
    /// implementation when Gorge v1 (PR #6) merges.
    /// </summary>
    public sealed class RoadConnectedTerritoryAdjacency : ITerritoryAdjacency
    {
        private readonly int[] _territoryIds;
        private readonly int[] _neighborOffset;
        private readonly int[] _neighborIds;

        public RoadConnectedTerritoryAdjacency(int[] territoryIds, int[][] neighborsByTerritory)
        {
            if (territoryIds == null)
            {
                throw new ArgumentNullException(nameof(territoryIds));
            }

            if (neighborsByTerritory == null)
            {
                throw new ArgumentNullException(nameof(neighborsByTerritory));
            }

            if (territoryIds.Length != neighborsByTerritory.Length)
            {
                throw new ArgumentException("Territory and neighbour arrays must align.");
            }

            _territoryIds = (int[])territoryIds.Clone();
            _neighborOffset = new int[_territoryIds.Length + 1];
            int total = 0;
            for (int i = 0; i < neighborsByTerritory.Length; i++)
            {
                int[] row = neighborsByTerritory[i] ?? Array.Empty<int>();
                total += row.Length;
            }

            _neighborIds = new int[total];
            int cursor = 0;
            for (int i = 0; i < neighborsByTerritory.Length; i++)
            {
                _neighborOffset[i] = cursor;
                int[] row = neighborsByTerritory[i] ?? Array.Empty<int>();
                for (int j = 0; j < row.Length; j++)
                {
                    _neighborIds[cursor++] = row[j];
                }
            }

            _neighborOffset[_territoryIds.Length] = cursor;
        }

        public string SourceName
        {
            get { return AiConstants.RoadConnectedAdjacencySource; }
        }

        public int TerritoryCount
        {
            get { return _territoryIds.Length; }
        }

        public int TerritoryIdAt(int index)
        {
            return _territoryIds[index];
        }

        public bool Contains(int territoryId)
        {
            return IndexOf(territoryId) >= 0;
        }

        public bool AreNeighbors(int territoryIdA, int territoryIdB)
        {
            int index = IndexOf(territoryIdA);
            if (index < 0)
            {
                return false;
            }

            int start = _neighborOffset[index];
            int end = _neighborOffset[index + 1];
            for (int i = start; i < end; i++)
            {
                if (_neighborIds[i] == territoryIdB)
                {
                    return true;
                }
            }

            return false;
        }

        public void CopyNeighbors(int territoryId, List<int> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            int index = IndexOf(territoryId);
            if (index < 0)
            {
                return;
            }

            int start = _neighborOffset[index];
            int end = _neighborOffset[index + 1];
            for (int i = start; i < end; i++)
            {
                into.Add(_neighborIds[i]);
            }
        }

        private int IndexOf(int territoryId)
        {
            int lo = 0;
            int hi = _territoryIds.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int value = _territoryIds[mid];
                if (value == territoryId)
                {
                    return mid;
                }

                if (value < territoryId)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return -1;
        }
    }
}
