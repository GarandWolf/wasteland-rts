using System.Collections.Generic;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Frontline / neighbour source for the strategic layer.
    /// Gorge v1 uses <see cref="MapNeighborListAdjacency"/> (baked
    /// <c>TERR.neighborTerritoryIds</c>). Gorge v0 still uses
    /// <see cref="RoadConnectedTerritoryAdjacency"/> because that golden's
    /// neighbour lists are not the v1 contract (Goldendale–Hood River).
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
