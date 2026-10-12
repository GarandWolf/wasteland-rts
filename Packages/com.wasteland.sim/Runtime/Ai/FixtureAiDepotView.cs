using System.Collections.Generic;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Test depot read view. Production reads depots from owned state,
    /// not from a command-sink list.
    /// </summary>
    public sealed class FixtureAiDepotView : IAiDepotQuery
    {
        private readonly Dictionary<long, AiDepotReadback> _depots = new Dictionary<long, AiDepotReadback>();

        public bool TryGet(int factionId, int nodeId, out AiDepotReadback readback)
        {
            return _depots.TryGetValue(Key(factionId, nodeId), out readback);
        }

        public void RecordSite(int factionId, int nodeId, int siteTick, int activeTick)
        {
            _depots[Key(factionId, nodeId)] = new AiDepotReadback(siteTick, activeTick);
        }

        public int CountSites(int factionId)
        {
            int count = 0;
            foreach (KeyValuePair<long, AiDepotReadback> pair in _depots)
            {
                if ((int)(pair.Key >> 32) == factionId)
                {
                    count++;
                }
            }

            return count;
        }

        public int InFlightNodeId(int factionId, int tick)
        {
            int best = 0;
            foreach (KeyValuePair<long, AiDepotReadback> pair in _depots)
            {
                if ((int)(pair.Key >> 32) != factionId)
                {
                    continue;
                }

                if (pair.Value.IsActive(tick))
                {
                    continue;
                }

                int nodeId = (int)(uint)pair.Key;
                if (best == 0 || nodeId < best)
                {
                    best = nodeId;
                }
            }

            return best;
        }

        private static long Key(int factionId, int nodeId)
        {
            return ((long)factionId << 32) ^ (uint)nodeId;
        }
    }
}
