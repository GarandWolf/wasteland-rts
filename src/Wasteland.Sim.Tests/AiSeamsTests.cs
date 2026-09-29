using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiSeamsTests
    {
        [Fact]
        public void RoadConnectedAdjacency_TreatsGoldendaleAndHoodRiverAsNeighborsOnV0()
        {
            AiMapSnapshot map = LoadGorgeMap();
            Assert.Equal(AiConstants.RoadConnectedAdjacencySource, map.Adjacency.SourceName);
            Assert.Equal(14, map.TheDallesTownId);
            Assert.True(map.Adjacency.AreNeighbors(11, 6));
            Assert.True(map.Adjacency.AreNeighbors(6, 11));

            List<int> neighbors = new List<int>();
            map.Adjacency.CopyNeighbors(11, neighbors);
            Assert.Contains(6, neighbors);
            for (int i = 1; i < neighbors.Count; i++)
            {
                Assert.True(neighbors[i - 1] < neighbors[i]);
            }
        }

        [Fact]
        public void SaveLoad_RestoresHashedAiOwnedState()
        {
            AiMatchHost host = StartHost();
            host.Run(80);
            ulong before = host.Ai.HashState();
            byte[] blob = host.Ai.Save();

            AiMatchHost restored = StartHost();
            Assert.NotEqual(before, restored.Ai.HashState());
            restored.Ai.Load(blob);
            Assert.Equal(before, restored.Ai.HashState());
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void OnTick_DoesNotBypassTheCommandQueue()
        {
            AiMatchHost host = StartHost();
            host.Run(200);
            Assert.Equal(0, host.World.BattalionCount);
            Assert.Empty(host.World.Events);
            Assert.IsType<SimWorldCommandSink>(host.Ai.Commands);
            Assert.Equal(host.World.CommandDelayTicks, host.Ai.Commands.CommandDelayTicks);
            Assert.Equal(SimConfig.DefaultCommandDelayTicks, host.World.CommandDelayTicks);
            host.Shutdown();
        }

        [Fact]
        public void PathBudget_IsRespected()
        {
            AiMatchHost host = StartHost();
            host.Ai.OnTick(host.World.Tick);
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            int accepted = 0;
            for (int i = 0; i < 16; i++)
            {
                if (host.Ai.TryConsumePathRequest(faction))
                {
                    accepted++;
                }
            }

            Assert.Equal(AiConstants.PathRequestsPerTick, accepted);
            host.Shutdown();
        }

        [Fact]
        public void SubmitThroughSink_IsTheSameQueueHumansUse()
        {
            AiMatchHost host = StartHost();
            host.Ai.Commands.Submit(SimCommand.SpawnBattalion(0, 1, 7, 0, 0, 100));
            host.World.Step();
            host.World.Step();
            host.World.Step();
            Assert.Equal(1, host.World.BattalionCount);
            Assert.Equal(1, host.World.GetBattalion(7).OwnerId);
            host.Shutdown();
        }

        private static AiMatchHost StartHost()
        {
            return AiMatchHost.Start(
                MatchConfig.Default(0xA11EEDUL),
                LoadGorgeBytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
        }

        internal static byte[] LoadGorgeBytes()
        {
            string path = TestDataPaths.FindRepoFile("data/golden/" + GoldenMapTests.MapFileName);
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            if (bytes.Length != GoldenMapTests.MapBytes)
            {
                throw new System.IO.InvalidDataException(
                    path + " is not a materialized WRMB (" + bytes.Length
                    + " bytes). Run git lfs pull --include=\"data/golden/*.wrmb\".");
            }

            return bytes;
        }

        internal static AiMapSnapshot LoadGorgeMap()
        {
            return AiMapSnapshot.FromWrmb(LoadGorgeBytes());
        }
    }
}
