using Wasteland.Sim.Hashing;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// Per-query ppt multipliers for road A*. Uniform 1000 is identity.
    /// A zero weight makes that tier (or span) impassable for the query.
    /// </summary>
    public readonly struct RoadCostWeights
    {
        public readonly int TrackPpt;
        public readonly int DirtPpt;
        public readonly int PavedPpt;
        public readonly int HighwayPpt;
        public readonly int BridgeSpanPpt;
        public readonly ulong Key;

        public RoadCostWeights(int trackPpt, int dirtPpt, int pavedPpt, int highwayPpt, int bridgeSpanPpt)
        {
            TrackPpt = trackPpt;
            DirtPpt = dirtPpt;
            PavedPpt = pavedPpt;
            HighwayPpt = highwayPpt;
            BridgeSpanPpt = bridgeSpanPpt;
            XxHash64Builder hash = new XxHash64Builder();
            hash.AddInt(trackPpt);
            hash.AddInt(dirtPpt);
            hash.AddInt(pavedPpt);
            hash.AddInt(highwayPpt);
            hash.AddInt(bridgeSpanPpt);
            Key = hash.Digest();
        }

        public static RoadCostWeights Uniform(int ppt = SimConfig.PptScale)
        {
            return new RoadCostWeights(ppt, ppt, ppt, ppt, ppt);
        }

        public int PptFor(RoadTier tier, bool isBridgeSpan)
        {
            int ppt;
            switch (tier)
            {
                case RoadTier.Track:
                    ppt = TrackPpt;
                    break;
                case RoadTier.Dirt:
                    ppt = DirtPpt;
                    break;
                case RoadTier.Paved:
                    ppt = PavedPpt;
                    break;
                default:
                    ppt = HighwayPpt;
                    break;
            }

            if (isBridgeSpan)
            {
                ppt = (int)((long)ppt * BridgeSpanPpt / SimConfig.PptScale);
            }

            return ppt;
        }
    }
}
