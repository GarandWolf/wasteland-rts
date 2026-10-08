using System.IO;

namespace Wasteland.Sim.Scenario
{
    /// <summary>
    /// The bytes passed as the main map file are a Git LFS pointer, not the WRMB.
    /// This is not a SHA-256 mismatch.
    /// </summary>
    public sealed class LfsNotPulledException : IOException
    {
        public const string Code = "LfsNotPulled";

        public LfsNotPulledException(string fileName)
            : base(Code + ": '" + (fileName ?? string.Empty) + "' is a Git LFS pointer. Run git lfs pull so the map file is materialized before it is hashed.")
        {
            FileName = fileName ?? string.Empty;
        }

        public string FileName { get; }
    }
}
