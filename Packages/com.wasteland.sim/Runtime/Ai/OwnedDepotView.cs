namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Production depot read view over saved faction depot slots.
    /// </summary>
    public sealed class OwnedDepotView : IAiDepotQuery
    {
        private readonly AiOwnedState _owned;

        public OwnedDepotView(AiOwnedState owned)
        {
            _owned = owned ?? throw new System.ArgumentNullException(nameof(owned));
        }

        public bool TryGet(int factionId, int nodeId, out AiDepotReadback readback)
        {
            for (int f = 0; f < _owned.FactionCount; f++)
            {
                AiFactionState faction = _owned.FactionAt(f);
                if (faction.FactionId != factionId)
                {
                    continue;
                }

                for (int d = 0; d < faction.DepotCount; d++)
                {
                    if (faction.Depots[d].NodeId != nodeId)
                    {
                        continue;
                    }

                    readback = new AiDepotReadback(faction.DepotSiteTick, faction.DepotActiveTick);
                    return true;
                }
            }

            readback = default;
            return false;
        }

        public void RecordSite(int factionId, int nodeId, int siteTick, int activeTick)
        {
            for (int f = 0; f < _owned.FactionCount; f++)
            {
                AiFactionState faction = _owned.FactionAt(f);
                if (faction.FactionId != factionId)
                {
                    continue;
                }

                faction.DepotSiteTick = siteTick;
                faction.DepotActiveTick = activeTick;
                return;
            }
        }
    }
}
