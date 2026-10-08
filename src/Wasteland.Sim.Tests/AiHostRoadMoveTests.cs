using System;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiHostRoadMoveTests
    {
        [Fact]
        public void HostMove_FollowsRoadGraph_NotAStraightSegment()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());

            int factionId = AiConstants.DefaultAiFactionA;
            Assert.True(host.World.TryGetSeat(factionId, out SeatView seat));
            SimBattalion mover = FirstOwned(host.World, factionId);
            Assert.Equal(seat.HqXCm, mover.PositionXCm);
            Assert.Equal(seat.HqYCm, mover.PositionYCm);

            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.TheDallesTownId, out AiTownRecord dalles));
            Assert.True(dalles.XCm != seat.HqXCm || dalles.YCm != seat.HqYCm);

            host.World.Submit(SimCommand.Move(
                host.Tick,
                factionId,
                mover.Id,
                dalles.XCm,
                dalles.YCm));
            int steps = host.World.CommandDelayTicks + 1;
            for (int i = 0; i < steps; i++)
            {
                host.World.Step();
            }

            SimBattalion moved = host.World.GetBattalion(mover.Id);
            Assert.True(moved.PathCount > 0);
            Assert.NotNull(moved.PathXCm);
            Assert.NotNull(moved.PathYCm);
            bool singleStraightSegment = moved.PathCount == 1
                && moved.PathXCm[0] == dalles.XCm
                && moved.PathYCm[0] == dalles.YCm;
            Assert.False(singleStraightSegment);
            host.Shutdown();
        }

        private static SimBattalion FirstOwned(SimWorld world, int factionId)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == factionId)
                {
                    return battalion;
                }
            }

            throw new InvalidOperationException("faction " + factionId + " has no battalion.");
        }
    }
}
