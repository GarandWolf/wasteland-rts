using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Math;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class DeterminismTests
    {
        private const ulong MatchSeed = 0xC0FFEEUL;
        private const int MatchTicks = 120;

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

            log.Add(SimCommand.Move(40, playerId: 1, unitId: 1, Fix64Vec2.FromInts(10, 90)));
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
            Assert.NotEqual(a[0], b[0]);
        }

        [Fact]
        public void MoveCommand_ReachesTarget()
        {
            SimWorld world = new SimWorld(1UL);
            world.Submit(SimCommand.Spawn(0, 1, 1, Fix64Vec2.Zero, Fix64.FromInt(20)));
            world.Submit(SimCommand.Move(0, 1, 1, Fix64Vec2.FromInts(3, 4)));

            for (int i = 0; i < 20; i++)
            {
                world.Step();
            }

            SimUnit unit = world.GetUnit(1);
            Assert.Equal(Fix64Vec2.FromInts(3, 4), unit.Position);
            Assert.False(unit.HasTarget);
        }

        [Fact]
        public void SubmitOrder_DoesNotAffectSameTickResult()
        {
            SimCommand spawnB = SimCommand.Spawn(0, 1, 2, Fix64Vec2.FromInts(5, 0), Fix64.FromInt(4));
            SimCommand spawnA = SimCommand.Spawn(0, 1, 1, Fix64Vec2.FromInts(0, 5), Fix64.FromInt(4));

            SimWorld worldAB = new SimWorld(11UL);
            worldAB.Submit(spawnA);
            worldAB.Submit(spawnB);

            SimWorld worldBA = new SimWorld(11UL);
            worldBA.Submit(spawnB);
            worldBA.Submit(spawnA);

            Assert.Equal(worldAB.Step(), worldBA.Step());
            Assert.Equal(worldAB.GetUnit(1).Position, worldBA.GetUnit(1).Position);
            Assert.Equal(worldAB.GetUnit(2).Position, worldBA.GetUnit(2).Position);
        }

        private static List<SimCommand> ScriptedMatch()
        {
            return new List<SimCommand>
            {
                SimCommand.Spawn(0, 1, 1, Fix64Vec2.FromInts(0, 0), Fix64.FromInt(10)),
                SimCommand.Spawn(0, 1, 2, Fix64Vec2.FromInts(100, 0), Fix64.FromInt(5)),
                SimCommand.Move(1, 1, 1, Fix64Vec2.FromInts(50, 50)),
                SimCommand.Move(5, 1, 2, Fix64Vec2.FromInts(0, 80)),
                SimCommand.Move(30, 1, 1, Fix64Vec2.FromInts(20, 10)),
            };
        }

        private static List<ulong> Run(ulong seed, IReadOnlyList<SimCommand> log)
        {
            SimWorld world = new SimWorld(seed);
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
