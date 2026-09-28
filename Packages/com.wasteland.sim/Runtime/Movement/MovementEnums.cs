using System;

namespace Wasteland.Sim.Movement
{
    public enum MovementClass : byte
    {
        Wheeled = 0,
        Tracked = 1,
        Foot = 2,
        Air = 3
    }

    public enum LandCover : byte
    {
        Field = 0,
        Scrub = 1,
        Urban = 2,
        Forest = 3,
        Marsh = 4,
        Water = 5,
        Ford = 6
    }

    public enum RoadTier : byte
    {
        Track = 0,
        Dirt = 1,
        Paved = 2,
        Highway = 3
    }

    public enum RoadSurface : byte
    {
        Dirt = 0,
        Gravel = 1,
        Asphalt = 2,
        BridgeDeck = 3
    }

    [Flags]
    public enum RoadNodeFlags : ushort
    {
        None = 0,
        BridgeHead = 1 << 0
    }

    [Flags]
    public enum RoadEdgeFlags : ushort
    {
        None = 0,
        BridgeSpan = 1 << 0
    }

    public enum PathQueryKind : byte
    {
        OnRoads = 1,
        OffRoad = 2,
        Combined = 3
    }

    public enum PathKind : byte
    {
        Unreachable = 0,
        OffRoad = 1,
        OnRoads = 2,
        Combined = 3
    }

    public enum PathSegmentKind : byte
    {
        GridCell = 1,
        RoadNode = 2
    }
}
