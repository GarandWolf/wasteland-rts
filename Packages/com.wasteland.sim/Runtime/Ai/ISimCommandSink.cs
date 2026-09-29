using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// The only mutation path the AI may use. Implementations must forward
    /// spawn/move to the foundation <see cref="SimWorld.Submit"/> (or a test
    /// sink of it). <see cref="CommandKind.Build"/> is accepted here in M1
    /// and is not forwarded; SimWorld does not stub buildings.
    /// </summary>
    public interface ISimCommandSink
    {
        int Tick { get; }

        int CommandDelayTicks { get; }

        SimCommand Submit(SimCommand command);
    }

    /// <summary>
    /// Fog-honest world reads. Enemy live intent stays hidden.
    /// </summary>
    public interface ISimWorldView
    {
        int Tick { get; }

        int BattalionCount { get; }

        void CopyOwnBattalionIds(int factionId, List<int> into);

        void CopyVisibleEnemyBattalionIds(int factionId, List<int> into);

        int CountOwnBattalions(int factionId);

        bool TryGetOwnBattalion(int factionId, int battalionId, out SimBattalion battalion);
    }

    /// <summary>
    /// Capture occupancy is a world process, not vision. The AI may ask about
    /// zones it already knows; the view returns only empty / one faction /
    /// contested. Battalion identities and positions are not exposed.
    /// </summary>
    public enum CaptureOccupancyKind : byte
    {
        Empty = 0,
        OneFaction = 1,
        Contested = 2
    }

    public readonly struct CaptureZoneOccupancyResult
    {
        public readonly CaptureOccupancyKind Kind;
        public readonly int FactionId;

        public CaptureZoneOccupancyResult(CaptureOccupancyKind kind, int factionId)
        {
            Kind = kind;
            FactionId = factionId;
        }
    }

    public interface ICaptureOccupancyView
    {
        CaptureZoneOccupancyResult OccupancyOf(int xCm, int yCm, int radiusCm);
    }

    /// <summary>
    /// Manpower / income until sim economy exists. Designer rates are per minute.
    /// </summary>
    public interface IAiEconomy
    {
        int IncomePerMinute(int factionId);

        void AccrueTick(int tick);
    }

    /// <summary>
    /// Starting homes are not map-authored. Personality → territory and
    /// starting manpower until the sim owns spawn/HQ placement. Do not read
    /// <c>rules.json</c> scenario here.
    /// </summary>
    public interface IAiHomeSeeder
    {
        int TerritoryIdFor(string personalityId);

        void Seed(AiOwnedState owned, AiMapSnapshot map);
    }
}
