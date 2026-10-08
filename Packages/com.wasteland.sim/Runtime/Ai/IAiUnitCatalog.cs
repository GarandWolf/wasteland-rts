namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Per-unit balance fields the AI reads. Shape matches Friday's
    /// <c>TryGetUnitTier</c> / <c>TryGetUnitRole</c> /
    /// <c>TryGetUnitTargetClass</c> so the switch to
    /// <c>BalanceCatalog</c> is a one-line adapter.
    /// </summary>
    public interface IAiUnitCatalog
    {
        bool TryGetTier(string unitId, out int tier);

        bool TryGetRole(string unitId, out string role);

        bool TryGetTargetClass(string unitId, out string targetClass);

        bool TryGetManpowerCost(string unitId, out int manpower);
    }
}
