#nullable disable
using System;
using System.IO;
using Wasteland.Presentation;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    /// <summary>
    /// Headless check of the configuration <see cref="GorgeListeningHarness"/> boots.
    /// The harness is Unity; this test drives the same <see cref="PlayableMatch"/>
    /// faction list through <see cref="AiMatchHost.Start"/>.
    /// </summary>
    public sealed class PlayableMatchSmokeTests
    {
        const int FirstTicks = 100;

        [Fact]
        public void DefaultMatch_AiSitsOppositeTheHuman_AndCommandsWithin100Ticks()
        {
            string root = FindRepoRoot();
            string harness = File.ReadAllText(Path.Combine(
                root, "Assets", "_Project", "Presentation", "GorgeListeningHarness.cs"));
            Assert.Contains("PlayableMatch.AiFactionIds()", harness, StringComparison.Ordinal);
            Assert.Contains("PlayableMatch.MatchSeed", harness, StringComparison.Ordinal);

            string balanceDir = Path.Combine(root, "data", "balance");
            string mapName = SimScenario.ExpectedWrmbFileName("golden_ci_columbia_gorge_100_v1_marsh");
            byte[] map = File.ReadAllBytes(Path.Combine(root, "data", "golden", mapName));
            AiBalanceTables balance = AiBalanceTables.FromDirectory(balanceDir);
            int[] aiFactions = PlayableMatch.AiFactionIds();
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(PlayableMatch.MatchSeed),
                map,
                aiFactions,
                AiConstants.DifficultyNormal,
                balance);
            try
            {
                Assert.NotEmpty(host.HumanSeats);
                Assert.Equal(new[] { PlayableMatch.HumanFactionId }, host.HumanSeats);
                Assert.True(host.IsHumanSeat(PlayableMatch.HumanFactionId));
                for (int i = 0; i < aiFactions.Length; i++)
                {
                    int ai = aiFactions[i];
                    Assert.DoesNotContain(ai, host.HumanSeats);
                    Assert.False(host.IsHumanSeat(ai));
                    Assert.True(host.World.TryGetSeat(ai, out SeatView aiSeat));
                    Assert.NotEqual(PlayableMatch.HumanFactionId, aiSeat.FactionId);
                }

                for (int tick = 0; tick < FirstTicks; tick++)
                {
                    host.Step();
                }

                bool aiCommanded = false;
                for (int i = 0; i < host.CommandLog.Count; i++)
                {
                    SimCommand command = host.CommandLog[i];
                    if (command.IssuedTick >= FirstTicks)
                    {
                        continue;
                    }

                    for (int f = 0; f < aiFactions.Length; f++)
                    {
                        if (command.IssuerId == aiFactions[f])
                        {
                            aiCommanded = true;
                        }
                    }
                }

                Assert.True(
                    aiCommanded,
                    "The AI seat issued no command in the first " + FirstTicks + " ticks.");
            }
            finally
            {
                host.Shutdown();
            }
        }

        static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                string marker = Path.Combine(dir.FullName, "data", "balance", "scenarios", "columbia_gorge_100mi_mvp.json");
                if (File.Exists(marker))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the repository root from " + AppContext.BaseDirectory);
        }
    }
}
