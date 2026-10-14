using Wasteland.Sim.Ai;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Default match the Windows player boots on the Columbia Gorge scenario.
    /// Remnant (faction A, scenario slot 0) is the human seat.
    /// Compact (faction B, scenario slot 1) is the only AI seat.
    /// </summary>
    public static class PlayableMatch
    {
        public const ulong MatchSeed = 0xC01055A1UL;

        public const int HumanFactionId = AiConstants.DefaultAiFactionA;

        public const int AiFactionId = AiConstants.DefaultAiFactionB;

        public static int[] AiFactionIds()
        {
            return new[] { AiFactionId };
        }
    }
}
