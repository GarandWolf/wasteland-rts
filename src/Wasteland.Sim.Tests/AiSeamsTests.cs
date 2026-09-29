using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Movement;
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
            Assert.NotEqual(0UL, map.MapKey);

            List<int> neighbors = new List<int>();
            map.Adjacency.CopyNeighbors(11, neighbors);
            Assert.Contains(6, neighbors);
            for (int i = 1; i < neighbors.Count; i++)
            {
                Assert.True(neighbors[i - 1] < neighbors[i]);
            }
        }

        [Fact]
        public void FromWrmb_LoadsV1Gorge()
        {
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName);
            byte[] bytes = GoldenMapFiles.ReadWrmb(
                Path.Combine(dir, GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(bytes);
            Assert.Equal(14, map.TheDallesTownId);
            Assert.NotEqual(0UL, map.MapKey);
            Assert.Equal(
                MapKey.For(WrmbFile.Parse(bytes), AiConstants.MapKeySectionTags()),
                map.MapKey);
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
        public void Load_RejectsMatchSeedMismatch()
        {
            AiMatchHost host = StartHost();
            host.Run(16);
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            AiMatchHost other = AiMatchHost.Start(
                MatchConfig.Default(0xBEEFUL),
                LoadGorgeBytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            Assert.Throws<InvalidDataException>(() => other.Ai.Load(blob));
            other.Shutdown();
        }

        [Fact]
        public void Load_RejectsBalanceAndMapHeaderMismatch()
        {
            AiMatchHost host = StartHost();
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            byte[] flippedBalance = (byte[])blob.Clone();
            flippedBalance[12] ^= 1;
            AiMatchHost other = StartHost();
            Assert.Throws<InvalidDataException>(() => other.Ai.Load(flippedBalance));

            byte[] flippedMap = (byte[])blob.Clone();
            flippedMap[20] ^= 1;
            Assert.Throws<InvalidDataException>(() => other.Ai.Load(flippedMap));
            other.Shutdown();
        }

        [Fact]
        public void Load_RejectsConfigIntMismatch()
        {
            AiMatchHost host = StartHost();
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            int offset;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(blob)))
            {
                reader.ReadInt32();
                reader.ReadUInt64();
                reader.ReadUInt64();
                reader.ReadUInt64();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadString();
                reader.ReadString();
                offset = (int)reader.BaseStream.Position;
            }

            byte[] mutated = (byte[])blob.Clone();
            mutated[offset] ^= 1;
            AiMatchHost other = StartHost();
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => other.Ai.Load(mutated));
            Assert.Contains("ReactionExtraTicks", ex.Message);
            other.Shutdown();
        }

        [Fact]
        public void StrategicSlice_RunsOnPhaseSixAfterCadence()
        {
            AiMatchHost host = StartHost();
            Assert.Equal(0, host.Ai.StrategicSliceRuns);
            host.Run(50);
            Assert.True(host.Ai.StrategicSliceRuns > 0);
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

        [Fact]
        public void Submit_OverwritesIssuedTickToWorldTick()
        {
            AiMatchHost host = StartHost();
            host.Run(5);
            SimCommand stamped = host.Ai.Commands.Submit(SimCommand.SpawnBattalion(0, 1, 9, 0, 0, 100));
            Assert.Equal(host.World.Tick, stamped.IssuedTick);
            host.Shutdown();
        }

        [Fact]
        public void MatchInit_RejectsCommandDelayMismatch()
        {
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            byte[] map = LoadGorgeBytes();
            MatchConfig match = new MatchConfig(0xA11EEDUL, 5).WithKeys(
                balance.BalanceKey,
                MapKey.For(WrmbFile.Parse(map), AiConstants.MapKeySectionTags()));
            SimWorld world = new SimWorld(new MatchConfig(0xA11EEDUL, 2));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            Assert.Throws<System.ArgumentException>(() => EnemyAi.MatchInit(
                match,
                map,
                new[] { AiConstants.DefaultAiFactionA },
                AiConstants.DifficultyNormal,
                balance,
                sink,
                sink));
        }

        [Fact]
        public void AiRngDraws_DoNotMoveWorldRng()
        {
            AiMatchHost host = StartHost();
            ulong beforeState = host.World.Rng.State;
            ulong beforeInc = host.World.Rng.Increment;
            host.Ai.Owned.FactionAt(0).StrategyRng.NextUInt();
            host.Ai.Owned.FactionAt(0).TacticalRng.NextUInt();
            host.Ai.Owned.FactionAt(1).ScoutRng.NextUInt();
            Assert.Equal(beforeState, host.World.Rng.State);
            Assert.Equal(beforeInc, host.World.Rng.Increment);
            Assert.NotEqual(host.World.Rng.Increment, host.Ai.Owned.FactionAt(0).StrategyRng.Increment);
            Assert.NotEqual(
                host.Ai.Owned.FactionAt(0).StrategyRng.Increment,
                host.Ai.Owned.FactionAt(0).TacticalRng.Increment);
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
            string dir = GoldenMapFiles.FindDirectory(GoldenMapTests.MapFileName);
            return GoldenMapFiles.ReadWrmb(Path.Combine(dir, GoldenMapTests.MapFileName), GoldenMapTests.MapBytes);
        }

        internal static AiMapSnapshot LoadGorgeMap()
        {
            return AiMapSnapshot.FromWrmb(LoadGorgeBytes());
        }
    }
}
