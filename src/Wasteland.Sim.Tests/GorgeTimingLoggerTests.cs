using System.IO;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class GorgeTimingLoggerTests
    {
        [Fact]
        public void WritesJsonlForTwoAiGorgeMatch()
        {
            GorgeTimingLog log = GorgeTimingLogger.Run();
            Assert.True(File.Exists(log.Path));
            Assert.False(string.IsNullOrEmpty(log.Jsonl));
            Assert.Contains("\"event\":", log.Jsonl);
            Assert.Contains("compact_dalles_arrive", log.Jsonl);
            Assert.True(log.CompactArriveTick >= 0, "Compact did not arrive at The Dalles.");
        }
    }
}
