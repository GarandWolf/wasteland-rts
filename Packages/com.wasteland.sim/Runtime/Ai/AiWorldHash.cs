using Wasteland.Sim.Hashing;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// One mix of sim world hash and AI-owned hash. Test host and runtime host
    /// both call this; neither inlines <see cref="XxHash64.Mix"/>.
    /// </summary>
    public static class AiWorldHash
    {
        public static ulong Mix(ulong simWorldHash, ulong aiStateHash)
        {
            return XxHash64.Mix(simWorldHash, aiStateHash);
        }
    }
}
