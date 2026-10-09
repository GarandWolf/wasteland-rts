using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class DeterminismTests
    {
        private const ulong MatchSeed = 0xC0FFEEUL;
        private const int MatchTicks = 120;
        private const int Issuer = 1;

        [Fact]
        public void SameSeedAndCommandLog_ProducesIdenticalHashesEveryTick()
        {
            IReadOnlyList<SimCommand> log = ScriptedMatch();
            RunResult first = Run(MatchSeed, log);
            RunResult second = Run(MatchSeed, log);

            Assert.Equal(MatchTicks, first.Hashes.Count);
            Assert.Equal(first.Hashes, second.Hashes);
            AssertSameEvents(first.Events, second.Events);
            Assert.Contains(first.Events, ev => ev.Tick < MatchTicks - SimWorld.EventHistoryTicks);
            AssertSameEvents(first.Recent, second.Recent);
            Assert.True(first.Recent.Count < first.Events.Count);
        }

        [Fact]
        public void DifferentCommandLog_ProducesDifferentHash()
        {
            List<SimCommand> log = new List<SimCommand>(ScriptedMatch());
            RunResult baseline = Run(MatchSeed, log);

            log.Add(SimCommand.Move(40, Issuer, 1, xCm: 1000, yCm: 9000));
            RunResult mutated = Run(MatchSeed, log);

            Assert.NotEqual(baseline.Hashes[baseline.Hashes.Count - 1], mutated.Hashes[mutated.Hashes.Count - 1]);
            Assert.NotEqual(baseline.Hashes, mutated.Hashes);
        }

        [Fact]
        public void DifferentSeed_ProducesDifferentHash()
        {
            IReadOnlyList<SimCommand> log = ScriptedMatch();
            RunResult a = Run(MatchSeed, log);
            RunResult b = Run(MatchSeed + 1UL, log);
            Assert.NotEqual(a.Hashes[a.Hashes.Count - 1], b.Hashes[b.Hashes.Count - 1]);
        }

        /// <summary>
        /// The AI issues commands for both seats. Two runs from one seed keep
        /// the same per-tick hashes and the full event log, including ticks
        /// that have already fallen out of the 64-tick history window.
        /// </summary>
        [Fact]
        public void AiMatchHost_FourThousandTicks_IsIdenticalAcrossRuns()
        {
            const int ticks = 4000;
            AiRun first = RunAi(ticks);
            AiRun second = RunAi(ticks);
            Assert.Equal(ticks, first.WorldHashes.Count);
            Assert.Equal(first.WorldHashes, second.WorldHashes);
            Assert.Equal(first.HostHashes, second.HostHashes);
            AssertSameEvents(first.Events, second.Events);
            Assert.Contains(first.Events, ev => ev.Tick < ticks - SimWorld.EventHistoryTicks);
            AssertSameEvents(first.Recent, second.Recent);
        }

        [Fact]
        public void MoveCommand_ReachesBattalionTarget()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, commandDelayTicks: 0));
            world.Submit(SimCommand.SpawnBattalion(0, Issuer, 1, xCm: 0, yCm: 0, speedCmPerSecond: 2000));
            world.Submit(SimCommand.Move(0, Issuer, 1, xCm: 300, yCm: 400));

            for (int i = 0; i < 20; i++)
            {
                world.Step();
            }

            SimBattalion battalion = world.GetBattalion(1);
            Assert.Equal(300, battalion.PositionXCm);
            Assert.Equal(400, battalion.PositionYCm);
            Assert.False(battalion.HasTarget);
            Assert.Equal(1, world.GetUnit(1).BattalionId);
        }

        [Fact]
        public void DefaultDelay_ExecutesTwoTicksLater()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            Assert.Equal(2, world.CommandDelayTicks);
            world.Submit(SimCommand.SpawnBattalion(0, Issuer, 1, 0, 0, 100));

            world.Step();
            Assert.Equal(0, world.BattalionCount);
            world.Step();
            Assert.Equal(0, world.BattalionCount);
            world.Step();
            Assert.Equal(1, world.BattalionCount);
        }

        [Fact]
        public void SubmitOrder_AcrossIssuers_DoesNotAffectHash()
        {
            SimCommand spawnA = SimCommand.SpawnBattalion(0, issuerId: 1, battalionId: 1, 0, 500, 400);
            SimCommand spawnB = SimCommand.SpawnBattalion(0, issuerId: 9, battalionId: 2, 500, 0, 400);

            SimWorld worldAB = new SimWorld(new MatchConfig(11UL, 0));
            worldAB.Submit(spawnA);
            worldAB.Submit(spawnB);

            SimWorld worldBA = new SimWorld(new MatchConfig(11UL, 0));
            worldBA.Submit(spawnB);
            worldBA.Submit(spawnA);

            Assert.Equal(worldAB.Step(), worldBA.Step());
            Assert.Equal(worldAB.GetBattalion(1).PositionYCm, worldBA.GetBattalion(1).PositionYCm);
            Assert.Equal(worldAB.GetBattalion(2).PositionXCm, worldBA.GetBattalion(2).PositionXCm);
        }

        [Fact]
        public void MoveUnknownBattalion_EmitsCommandRejected()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.Submit(SimCommand.Move(0, Issuer, battalionId: 99, 1, 1));
            world.Step();

            SimEvent ev = Assert.Single(world.Events);
            Assert.Equal(0, ev.EventId);
            Assert.Equal(0, ev.Tick);
            Assert.Equal(SimEventKind.CommandRejected, ev.Kind);
            Assert.Equal(99, ev.SubjectId);
            Assert.Equal(CommandRejectReason.UnknownBattalion, ev.RejectReason);
            Assert.Equal(0, world.BattalionCount);
        }

        [Fact]
        public void Events_AreMonotonicByEventId()
        {
            SimWorld world = new SimWorld(new MatchConfig(1UL, 0));
            world.Submit(SimCommand.SpawnBattalion(0, Issuer, 1, 0, 0, 100));
            world.Submit(SimCommand.Move(0, Issuer, 1, 0, 0));
            world.Step();

            for (int i = 0; i < world.Events.Count; i++)
            {
                Assert.Equal(i, world.Events[i].EventId);
            }
        }

        private static List<SimCommand> ScriptedMatch()
        {
            return new List<SimCommand>
            {
                SimCommand.SpawnBattalion(0, Issuer, 1, 0, 0, speedCmPerSecond: 1000),
                SimCommand.SpawnBattalion(0, Issuer, 2, 10000, 0, speedCmPerSecond: 500),
                SimCommand.Move(1, Issuer, 1, 5000, 5000),
                SimCommand.Move(5, Issuer, 2, 0, 8000),
                SimCommand.Move(30, Issuer, 1, 2000, 1000),
            };
        }

        private static RunResult Run(ulong seed, IReadOnlyList<SimCommand> log)
        {
            SimWorld world = new SimWorld(MatchConfig.Default(seed));
            for (int i = 0; i < log.Count; i++)
            {
                world.Submit(log[i]);
            }

            RunResult result = new RunResult();
            for (int i = 0; i < MatchTicks; i++)
            {
                result.Hashes.Add(world.Step());
            }

            result.Events = CopyEvents(world);
            world.CopyEventsInRange(world.Tick - SimWorld.EventHistoryTicks, world.Tick - 1, result.Recent);
            return result;
        }

        private static AiRun RunAi(int ticks)
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xA14000UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            AiRun run = new AiRun();
            for (int i = 0; i < ticks; i++)
            {
                host.Step();
                run.WorldHashes.Add(host.World.HashState());
                run.HostHashes.Add(host.HashWorld());
            }

            run.Events = CopyEvents(host.World);
            host.World.CopyEventsInRange(host.Tick - SimWorld.EventHistoryTicks, host.Tick - 1, run.Recent);
            host.Shutdown();
            return run;
        }

        private static List<SimEvent> CopyEvents(SimWorld world)
        {
            List<SimEvent> copy = new List<SimEvent>(world.Events.Count);
            for (int i = 0; i < world.Events.Count; i++)
            {
                copy.Add(world.Events[i]);
            }

            return copy;
        }

        private static void AssertSameEvents(List<SimEvent> left, List<SimEvent> right)
        {
            Assert.Equal(left.Count, right.Count);
            for (int i = 0; i < left.Count; i++)
            {
                Assert.Equal(left[i].EventId, right[i].EventId);
                Assert.Equal(left[i].Tick, right[i].Tick);
                Assert.Equal(left[i].Kind, right[i].Kind);
                Assert.Equal(left[i].SubjectId, right[i].SubjectId);
                Assert.Equal(left[i].AuxId, right[i].AuxId);
                Assert.Equal(left[i].Param0, right[i].Param0);
                Assert.Equal(left[i].Param1, right[i].Param1);
                Assert.Equal(left[i].Key, right[i].Key);
            }
        }

        private sealed class RunResult
        {
            public readonly List<ulong> Hashes = new List<ulong>();
            public List<SimEvent> Events = new List<SimEvent>();
            public readonly List<SimEvent> Recent = new List<SimEvent>();
        }

        private sealed class AiRun
        {
            public readonly List<ulong> WorldHashes = new List<ulong>();
            public readonly List<ulong> HostHashes = new List<ulong>();
            public List<SimEvent> Events = new List<SimEvent>();
            public readonly List<SimEvent> Recent = new List<SimEvent>();
        }
    }
}
