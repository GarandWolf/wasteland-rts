namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Per-building balance fields the AI reads. Shape matches Friday's
    /// <c>TryGetBuildingRole</c> so the switch to
    /// <c>BalanceCatalog</c> is a one-line adapter.
    /// </summary>
    public interface IAiBuildingCatalog
    {
        bool TryGetBuildingRole(string buildingId, out string role);

        /// <summary>
        /// Manpower / materiel / fuel cost of a building, from the balance
        /// catalog <c>buildings.json</c> row. False when the id is unknown
        /// or the catalog has no cost (empty harness fixtures).
        /// </summary>
        bool TryGetBuildCost(string buildingId, out int manpower, out int materiel, out int fuel);
    }
}
