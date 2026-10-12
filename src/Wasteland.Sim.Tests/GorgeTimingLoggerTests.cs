using System;
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
            Assert.Contains("remnant_tier2", log.Jsonl);
            Assert.True(log.CompactArriveTick >= 0, "Compact did not arrive at The Dalles.");
            Assert.True(log.RemnantTier2Tick >= 0, "Remnant did not reach Tier II.");
            Assert.InRange(log.RemnantTier2Tick, 2700, 3300);

            for (int i = 0; i < log.WhiteSalmonSamples.Count; i++)
            {
                WhiteSalmonSample sample = log.WhiteSalmonSamples[i];
                Assert.True(
                    sample.Contesting,
                    "Compact battalion " + sample.BattalionId
                    + " (" + sample.UnitId + ") entered White Salmon zone at tick "
                    + sample.Tick + " without contesting town 30.");
            }
        }

        [Fact]
        public void WritesJsonlForTwoAiGorgeMatch_RealBuildings()
        {
            GorgeTimingLog log = GorgeTimingLogger.Run(BalanceLoad.Tables());
            string path = Path.Combine(AppContext.BaseDirectory, "gorge_real_buildings_timings.jsonl");
            File.WriteAllText(path, log.Jsonl);
            Assert.True(File.Exists(path));
            Assert.Contains("compact_dalles_arrive", log.Jsonl);
            Assert.True(log.CompactArriveTick >= 0, "Compact did not arrive at The Dalles.");
            Assert.True(log.RemnantArriveTick >= 0, "Remnant did not arrive at The Dalles.");
        }
    }
}
