using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiCaptureTests
    {
        [Fact]
        public void CaptureTicks_MapPopulationClassToFourTierTable()
        {
            Assert.Equal(5, AiConstants.CaptureHoldTicksByPopulationClass.ClassCount);
            Assert.Equal(30 * AiConstants.TickRateHz, AiConstants.CaptureHoldTicksByPopulationClass.Class0);
            Assert.Equal(60 * AiConstants.TickRateHz, AiConstants.CaptureHoldTicksByPopulationClass.Class1);
            Assert.Equal(120 * AiConstants.TickRateHz, AiConstants.CaptureHoldTicksByPopulationClass.Class2);
            Assert.Equal(180 * AiConstants.TickRateHz, AiConstants.CaptureHoldTicksByPopulationClass.Class3);
            Assert.Equal(180 * AiConstants.TickRateHz, AiConstants.CaptureHoldTicksByPopulationClass.Class4);

            Assert.Equal(600, AiMapSnapshot.CaptureTicksForClass(0));
            Assert.Equal(1200, AiMapSnapshot.CaptureTicksForClass(1));
            Assert.Equal(2400, AiMapSnapshot.CaptureTicksForClass(2));
            Assert.Equal(3600, AiMapSnapshot.CaptureTicksForClass(3));
            Assert.Equal(3600, AiMapSnapshot.CaptureTicksForClass(4));
            Assert.Equal(600, AiMapSnapshot.CaptureTicksForClass(-1));
        }

        [Fact]
        public void GorgeTowns_UsePopulationClassZeroThroughTwo()
        {
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(AiSeamsTests.LoadGorgeV1Bytes());
            int[] counts = new int[AiConstants.CaptureHoldTicksByPopulationClass.ClassCount];
            int maxClass = 0;
            for (int i = 0; i < map.TownCount; i++)
            {
                int populationClass = map.TownAt(i).PopulationClass;
                Assert.InRange(populationClass, 0, AiConstants.CaptureHoldTicksByPopulationClass.ClassCount - 1);
                counts[populationClass]++;
                if (populationClass > maxClass)
                {
                    maxClass = populationClass;
                }
            }

            Assert.Equal(2, maxClass);
            Assert.True(counts[0] > 0);
            Assert.True(counts[1] > 0);
            Assert.True(counts[2] > 0);
            Assert.Equal(0, counts[3]);
            Assert.Equal(0, counts[4]);
        }

        [Fact]
        public void MixedOccupancy_PausesCaptureTimer()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());

            int townIndex = FindNeutralTown(host);
            AiTownRecord town = host.Ai.Map.TownAt(townIndex);
            int delay = host.World.CommandDelayTicks;

            host.World.Submit(SimCommand.SpawnBattalion(
                host.Tick,
                AiConstants.DefaultAiFactionA,
                9000001,
                town.XCm,
                town.YCm,
                0));
            host.Run(delay + 8);

            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            int heldBeforeContest = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.True(heldBeforeContest > 0);
            Assert.True(heldBeforeContest < AiMapSnapshot.CaptureTicksForClass(town.PopulationClass));

            host.World.Submit(SimCommand.SpawnBattalion(
                host.Tick,
                AiConstants.DefaultAiFactionB,
                9000002,
                town.XCm,
                town.YCm,
                0));
            host.Run(delay + 1);

            int paused = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            host.Run(80);

            Assert.Equal(paused, host.Ai.Owned.CaptureHeldTicksAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));

            host.Shutdown();
        }

        private static int FindNeutralTown(AiMatchHost host)
        {
            AiMapSnapshot map = host.Ai.Map;
            Assert.True(map.TryGetTown(map.GoldendaleTownId, out AiTownRecord goldendale));
            Assert.True(map.TryGetTown(map.HoodRiverTownId, out AiTownRecord hoodRiver));
            long minSepSq = 3L * AiConstants.MileCm * AiConstants.MileCm;
            for (int i = 0; i < map.TownCount; i++)
            {
                if (host.Ai.Owned.TownOwnerAt(i) != 0)
                {
                    continue;
                }

                AiTownRecord town = map.TownAt(i);
                if (DistSq(town, goldendale) < minSepSq || DistSq(town, hoodRiver) < minSepSq)
                {
                    continue;
                }

                return i;
            }

            throw new System.InvalidOperationException("No far unowned town on Gorge v1.");
        }

        private static long DistSq(AiTownRecord a, AiTownRecord b)
        {
            long dx = (long)a.XCm - b.XCm;
            long dy = (long)a.YCm - b.YCm;
            return (dx * dx) + (dy * dy);
        }
    }
}
