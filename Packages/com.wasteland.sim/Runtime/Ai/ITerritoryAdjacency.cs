using System.Collections.Generic;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Frontline / neighbour source for the strategic layer.
    /// Gorge v0 has no trusted map neighbour lists (<c>defenseGroupId</c> /
    /// v1 <c>neighborTerritoryIds</c> land with PR #6). Until then the AI uses
    /// <see cref="RoadConnectedTerritoryAdjacency"/> (road-connected stopgap).
    /// A v1 loader should implement this same interface from the baked lists
    /// and drop the stopgap.
    /// </summary>
    public interface ITerritoryAdjacency
    {
        string SourceName { get; }

        int TerritoryCount { get; }

        int TerritoryIdAt(int index);

        bool Contains(int territoryId);

        bool AreNeighbors(int territoryIdA, int territoryIdB);

        void CopyNeighbors(int territoryId, List<int> into);
    }
}
