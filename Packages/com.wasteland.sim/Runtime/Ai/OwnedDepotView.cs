using System.Collections.Generic;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Depot read view. Live sites come from <see cref="ISimWorldView.CopyDepots"/>
    /// (reserved + started + active). Fixture slots stay as a fallback.
    /// </summary>
    public sealed class OwnedDepotView : IAiDepotQuery
    {
        private readonly AiOwnedState _owned;
        private readonly ISimWorldView? _world;
        private readonly List<DepotView> _scratch = new List<DepotView>();

        public OwnedDepotView(AiOwnedState owned)
            : this(owned, null)
        {
        }

        public OwnedDepotView(AiOwnedState owned, ISimWorldView? world)
        {
            _owned = owned ?? throw new System.ArgumentNullException(nameof(owned));
            _world = world;
        }

        public bool TryGet(int factionId, int nodeId, out AiDepotReadback readback)
        {
            if (_world != null)
            {
                _world.CopyDepots(_scratch);
                SortDepots(_scratch);
                for (int i = 0; i < _scratch.Count; i++)
                {
                    DepotView depot = _scratch[i];
                    if (depot.OwnerFactionId != factionId || depot.NodeId != nodeId)
                    {
                        continue;
                    }

                    readback = new AiDepotReadback(depot.SiteTick, depot.ActiveTick);
                    return true;
                }
            }

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

        public int CountSites(int factionId)
        {
            if (_world != null)
            {
                _world.CopyDepots(_scratch);
                int count = 0;
                for (int i = 0; i < _scratch.Count; i++)
                {
                    if (_scratch[i].OwnerFactionId == factionId)
                    {
                        count++;
                    }
                }

                return count;
            }

            int owned = 0;
            for (int f = 0; f < _owned.FactionCount; f++)
            {
                if (_owned.FactionAt(f).FactionId == factionId)
                {
                    owned += _owned.FactionAt(f).DepotCount;
                }
            }

            return owned;
        }

        private static void SortDepots(List<DepotView> depots)
        {
            for (int i = 1; i < depots.Count; i++)
            {
                DepotView row = depots[i];
                int j = i;
                while (j > 0 && depots[j - 1].DepotId > row.DepotId)
                {
                    depots[j] = depots[j - 1];
                    j--;
                }

                depots[j] = row;
            }
        }
    }
}
