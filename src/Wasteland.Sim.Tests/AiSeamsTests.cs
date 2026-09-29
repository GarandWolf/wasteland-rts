using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
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
            Assert.IsType<LoggingCommandSink>(host.Ai.Commands);
            Assert.Equal(host.World.CommandDelayTicks, host.Ai.Commands.CommandDelayTicks);
            Assert.Equal(SimConfig.DefaultCommandDelayTicks, host.World.CommandDelayTicks);
            for (int i = 0; i < host.World.Events.Count; i++)
            {
                SimEvent ev = host.World.Events[i];
                if (ev.Kind == SimEventKind.CommandRejected)
                {
                    Assert.NotEqual(CommandRejectReason.UnknownKind, ev.RejectReason);
                }
            }

            Assert.True(host.CommandLog.Count > 0);
            for (int i = 0; i < host.CommandLog.Count; i++)
            {
                Assert.Equal(0, host.CommandLog[i].ExecuteTick);
            }

            host.Shutdown();
        }

        [Fact]
        public void ReactionDelayQueue_SubmitsOnlyAfterExtraTicks()
        {
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            int extra = balance.GetDifficulty(AiConstants.DifficultyNormal).ReactionExtraTicks;
            Assert.Equal(12, extra);

            AiMatchHost host = StartHost();
            int pending = 0;
            while (host.Tick < 200)
            {
                pending = TotalPending(host.Ai);
                if (pending > 0)
                {
                    break;
                }

                host.Step();
            }

            Assert.True(pending > 0);
            Assert.Empty(host.CommandLog);
            int ready = FirstReadyTick(host.Ai);
            Assert.True(ready > host.Tick);
            while (host.Tick < ready)
            {
                host.Step();
            }

            host.Step();
            Assert.True(host.CommandLog.Count > 0);
            Assert.Equal(ready, host.CommandLog[0].IssuedTick);
            Assert.Equal(0, host.CommandLog[0].ExecuteTick);
            host.Shutdown();
        }

        [Fact]
        public void ReactionDelayQueue_IsHashedAndRestoredOnSaveLoad()
        {
            AiMatchHost host = StartHost();
            while (host.Tick < 200 && TotalPending(host.Ai) == 0)
            {
                host.Step();
            }

            Assert.True(TotalPending(host.Ai) > 0);
            Assert.Empty(host.CommandLog);
            ulong before = host.Ai.HashState();
            byte[] blob = host.Ai.Save();
            int pending = TotalPending(host.Ai);
            int readyTick = host.Ai.Owned.FactionAt(0).PendingCount > 0
                ? host.Ai.Owned.FactionAt(0).Pending[0].ReadyTick
                : host.Ai.Owned.FactionAt(1).Pending[0].ReadyTick;

            AiMatchHost restored = StartHost();
            restored.Ai.Load(blob);
            Assert.Equal(before, restored.Ai.HashState());
            Assert.Equal(pending, TotalPending(restored.Ai));
            Assert.Equal(readyTick, FirstReadyTick(restored.Ai));
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void Brutal_HasNoReactionDelayOnSubmit()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xA11EEDUL),
                LoadGorgeBytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyBrutal,
                AiBalanceTablesTests.LoadBalance());
            Assert.Equal(0, host.Ai.Difficulty.ReactionExtraTicks);
            host.Run(80);
            Assert.True(host.CommandLog.Count > 0);
            Assert.Equal(0, host.CommandLog[0].ExecuteTick);
            host.Shutdown();
        }

        private static int TotalPending(EnemyAi ai)
        {
            int total = 0;
            for (int i = 0; i < ai.Owned.FactionCount; i++)
            {
                total += ai.Owned.FactionAt(i).PendingCount;
            }

            return total;
        }

        private static int FirstReadyTick(EnemyAi ai)
        {
            int best = int.MaxValue;
            for (int i = 0; i < ai.Owned.FactionCount; i++)
            {
                AiFactionState faction = ai.Owned.FactionAt(i);
                for (int p = 0; p < faction.PendingCount; p++)
                {
                    if (faction.Pending[p].ReadyTick < best)
                    {
                        best = faction.Pending[p].ReadyTick;
                    }
                }
            }

            return best;
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

        internal static byte[] LoadGorgeV1Bytes()
        {
            string path = TestDataPaths.FindRepoFile("data/golden/" + GoldenMapV1Tests.MapFileName);
            return GoldenMapFiles.ReadWrmb(path, GoldenMapV1Tests.MapBytes);
        }

        internal static AiMapSnapshot LoadGorgeMap()
        {
            return AiMapSnapshot.FromWrmb(LoadGorgeBytes());
        }
    }
}
