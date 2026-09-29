using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// The only mutation path the AI may use. Implementations must forward to
    /// the foundation <see cref="SimWorld.Submit"/> (or a test sink of it).
    /// </summary>
    public interface ISimCommandSink
    {
        int Tick { get; }

        int CommandDelayTicks { get; }

        SimCommand Submit(SimCommand command);
    }

    /// <summary>
    /// Fog-honest world reads. Enemy live state is unknown until a vision hook
    /// exists; M0 therefore returns no visible enemies.
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
}
