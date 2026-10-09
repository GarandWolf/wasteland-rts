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
    }
}
