using System;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// One chokepoint record from the map/AI WRMB <c>CHOK</c> section.
    /// <see cref="DefenseGroupId"/> 255 = none. v0 files store padding in the
    /// group and wheeled-bypass bytes.
    /// </summary>
    public readonly struct Chokepoint
    {
        public readonly int Id;
        public readonly byte Kind;
        public readonly byte DefenseGroupId;
        public readonly byte BypassWheeledBlocked;
        public readonly int XCm;
        public readonly int YCm;
        public readonly int RoadNodeId;
        public readonly int RoadEdgeId;
        public readonly int SideATerritoryId;
        public readonly int SideBTerritoryId;
        public readonly ulong StableKey;

        public Chokepoint(
            int id,
            byte kind,
            byte defenseGroupId,
            byte bypassWheeledBlocked,
            int xCm,
            int yCm,
            int roadNodeId,
            int roadEdgeId,
            int sideATerritoryId,
            int sideBTerritoryId,
            ulong stableKey)
        {
            Id = id;
            Kind = kind;
            DefenseGroupId = defenseGroupId;
            BypassWheeledBlocked = bypassWheeledBlocked;
            XCm = xCm;
            YCm = yCm;
            RoadNodeId = roadNodeId;
            RoadEdgeId = roadEdgeId;
            SideATerritoryId = sideATerritoryId;
            SideBTerritoryId = sideBTerritoryId;
            StableKey = stableKey;
        }

        public bool HasDefenseGroup
        {
            get { return DefenseGroupId != Wrmb.NoDefenseGroup; }
        }
    }

    /// <summary>
    /// One territory from <c>TERR</c>. <see cref="StableKey"/> is a u64 OSM-derived
    /// key in formatVersion 1 (never a double); 0 on v0 records.
    /// </summary>
    public readonly struct Territory
    {
        public readonly int Id;
        public readonly ulong StableKey;

        public Territory(int id, ulong stableKey)
        {
            Id = id;
            StableKey = stableKey;
        }
    }

    /// <summary>
    /// Loaded map/AI WRMB: roads plus the chokepoint and territory tables the
    /// sim and AI read. Terrain stays in the paired terrain file.
    /// </summary>
    public sealed class WrmbMap
    {
        public readonly WrmbFile File;
        public readonly RoadGraph Roads;
        public readonly Chokepoint[] Chokepoints;
        public readonly Territory[] Territories;

        public WrmbMap(WrmbFile file, RoadGraph roads, Chokepoint[] chokepoints, Territory[] territories)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            Roads = roads ?? throw new ArgumentNullException(nameof(roads));
            Chokepoints = chokepoints ?? throw new ArgumentNullException(nameof(chokepoints));
            Territories = territories ?? throw new ArgumentNullException(nameof(territories));
        }

        public ushort FormatVersion
        {
            get { return File.FileFormatVersion; }
        }

        public static WrmbMap Load(byte[] mapBlob)
        {
            WrmbFile file = WrmbFile.Parse(mapBlob);
            return new WrmbMap(
                file,
                Wrmb.ReadRoadGraph(file),
                Wrmb.ReadChokepoints(file),
                Wrmb.ReadTerritories(file));
        }
    }
}
