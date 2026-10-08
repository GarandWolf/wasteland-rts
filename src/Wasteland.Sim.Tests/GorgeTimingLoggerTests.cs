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
        }
    }
}
