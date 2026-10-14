#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Presentation;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class PlayableMatchSmokeTests
    {
        const int FirstTicks = 100;

        [Fact]
        public void DefaultMatch_AiSitsOppositeTheHuman_AndCommandsWithin100Ticks()
        {
            PlayableMatchBoot boot = PlayableMatch.CreateHost(FindRepoRoot());
            AiMatchHost host = boot.Host;
            try
            {
                SimScenario scenario = boot.Scenario;
                ScenarioSeat remnant = scenario.SeatAt(0);
                ScenarioSeat compact = scenario.SeatAt(1);
                Assert.Equal("remnant_command", remnant.Faction);
                Assert.Equal(0, remnant.Slot);
                Assert.False(remnant.IsAi);
                Assert.Equal(host.Ai.Map.GoldendaleTownId, remnant.HomeTownId);
                Assert.Equal("homestead_compact", compact.Faction);
                Assert.Equal(1, compact.Slot);
                Assert.True(compact.IsAi);
                Assert.Equal("homestead_compact_settler", compact.AiPersonality);
                Assert.Equal(host.Ai.Map.HoodRiverTownId, compact.HomeTownId);

                Assert.True(host.World.TryGetSeat(PlayableMatch.HumanFactionId, out SeatView human));
                Assert.Equal(remnant.Slot, human.Slot);
                Assert.Equal(remnant.HomeTownId, human.HomeTownId);
                Assert.True(host.IsHumanSeat(human.FactionId));
                Assert.True(host.World.TryGetSeat(PlayableMatch.AiFactionId, out SeatView aiSeat));
                Assert.Equal(compact.Slot, aiSeat.Slot);
                Assert.Equal(compact.HomeTownId, aiSeat.HomeTownId);
                Assert.False(host.IsHumanSeat(aiSeat.FactionId));
                Assert.NotEqual(human.FactionId, aiSeat.FactionId);

                for (int tick = 0; tick < FirstTicks; tick++)
                {
                    host.Step();
                }

                Assert.Equal(FirstTicks, host.Tick);
                AssertAiCommandAcceptedAndApplied(host, human.FactionId, aiSeat.FactionId);
            }
            finally
            {
                host.Shutdown();
            }
        }

        static void AssertAiCommandAcceptedAndApplied(AiMatchHost host, int remnantId, int aiId)
        {
            bool applied = false;
            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < log.Count; i++)
            {
                SimCommand command = log[i];
                Assert.NotEqual(remnantId, command.IssuerId);
                if (command.IssuerId != aiId || command.IssuedTick >= FirstTicks)
                {
                    continue;
                }

                int executeTick = command.IssuedTick + host.World.CommandDelayTicks;
                if (executeTick >= host.Tick || Rejected(host, command, executeTick))
                {
                    continue;
                }

                if (BattalionCarriesOrder(host, command.BattalionId) || RaiseStarted(host, command))
                {
                    applied = true;
                }
            }

            Assert.True(applied, "No AI command was accepted and applied in the first " + FirstTicks + " ticks.");
        }

        static bool Rejected(AiMatchHost host, SimCommand command, int executeTick)
        {
            IReadOnlyList<SimEvent> events = host.World.Events;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind == SimEventKind.CommandRejected
                    && ev.SubjectId == command.BattalionId
                    && ev.Tick == executeTick)
                {
                    return true;
                }
            }

            return false;
        }

        static bool BattalionCarriesOrder(AiMatchHost host, int battalionId)
        {
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.Id == battalionId && (battalion.HasTarget || battalion.PathCount > 0))
                {
                    return true;
                }
            }

            return false;
        }

        static bool RaiseStarted(AiMatchHost host, SimCommand command)
        {
            if (command.Kind != CommandKind.RaiseBattalion)
            {
                return false;
            }

            IReadOnlyList<SimEvent> events = host.World.Events;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind == SimEventKind.BattalionRaiseStarted
                    && ev.SubjectId == command.BattalionId
                    && ev.Tick >= command.IssuedTick)
                {
                    return true;
                }
            }

            return false;
        }

        static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                string marker = Path.Combine(
                    dir.FullName,
                    "data",
                    "balance",
                    "scenarios",
                    PlayableMatch.ScenarioId + ".json");
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
