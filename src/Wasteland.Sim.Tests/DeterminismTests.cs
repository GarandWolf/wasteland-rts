using System.Collections.Generic;
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
            List<ulong> first = Run(MatchSeed, log);
            List<ulong> second = Run(MatchSeed, log);

            Assert.Equal(MatchTicks, first.Count);
            Assert.Equal(first, second);
        }

        [Fact]
        public void DifferentCommandLog_ProducesDifferentHash()
        {
            List<SimCommand> log = new List<SimCommand>(ScriptedMatch());
            List<ulong> baseline = Run(MatchSeed, log);

            log.Add(SimCommand.Move(40, Issuer, 1, xCm: 1000, yCm: 9000));
            List<ulong> mutated = Run(MatchSeed, log);

            Assert.NotEqual(baseline[baseline.Count - 1], mutated[mutated.Count - 1]);
            Assert.NotEqual(baseline, mutated);
        }

        [Fact]
        public void DifferentSeed_ProducesDifferentHash()
        {
            IReadOnlyList<SimCommand> log = ScriptedMatch();
            List<ulong> a = Run(MatchSeed, log);
            List<ulong> b = Run(MatchSeed + 1UL, log);
            Assert.NotEqual(a[a.Count - 1], b[b.Count - 1]);
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

        private static List<ulong> Run(ulong seed, IReadOnlyList<SimCommand> log)
        {
            SimWorld world = new SimWorld(MatchConfig.Default(seed));
            for (int i = 0; i < log.Count; i++)
            {
                world.Submit(log[i]);
            }

            List<ulong> hashes = new List<ulong>(MatchTicks);
            for (int i = 0; i < MatchTicks; i++)
            {
                hashes.Add(world.Step());
            }

            return hashes;
        }
    }
}
