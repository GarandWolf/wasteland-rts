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
    /// Capture occupancy is a world process, not vision. Until the sim owns
    /// capture zones this copies every battalion position.
    /// </summary>
    public interface ICaptureOccupancyView
    {
        void CopyOccupancyBattalions(List<SimBattalion> into);
    }

    /// <summary>
    /// Manpower / income until sim economy exists.
    /// </summary>
    public interface IAiEconomy
    {
        int IncomePerSecond(int factionId);

        void AccrueTick();
    }

    /// <summary>
    /// Starting homes are not map-authored. Personality → territory until the
    /// sim owns spawn/HQ placement.
    /// </summary>
    public interface IAiHomeSeeder
    {
        int TerritoryIdFor(string personalityId);

        void Seed(AiOwnedState owned, AiMapSnapshot map);
    }
}
