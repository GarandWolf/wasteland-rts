using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
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

        [Fact]
        public void SimWorld_RejectsBuildAsUnknownKind()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            world.Submit(SimCommand.Build(0, 1, 0, 0, AiConstants.BuildingTagDepot));
            world.Step();
            world.Step();
            world.Step();
            Assert.Single(world.Events);
            Assert.Equal(SimEventKind.CommandRejected, world.Events[0].Kind);
            Assert.Equal(CommandRejectReason.UnknownKind, world.Events[0].RejectReason);
        }

        [Fact]
        public void AiCommandSink_AcceptsBuildWithoutForwardingToSimWorld()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            sink.Submit(SimCommand.Build(0, 1, 100, 200, AiConstants.BuildingTagDepot));
            world.Step();
            world.Step();
            world.Step();
            Assert.Empty(world.Events);
            Assert.Equal(0, world.BattalionCount);
        }

        [Fact]
        public void HomeSeeder_MapsLaunchPersonalities()
        {
            IAiHomeSeeder seeder = new AiHomeSeeder();
            Assert.Equal(AiConstants.RemnantHomeTerritoryId, seeder.TerritoryIdFor(AiConstants.PersonalityWarlord));
            Assert.Equal(AiConstants.CompactHomeTerritoryId, seeder.TerritoryIdFor(AiConstants.PersonalitySettler));
            Assert.Equal(0, seeder.TerritoryIdFor(AiConstants.PersonalityRaider));
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

        [Fact]
        public void ReactionDelayQueue_DropsLowestPriorityWhenFull()
        {
            AiMatchHost host = StartHost();
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            for (int i = 0; i < AiConstants.PendingCommandCapacity; i++)
            {
                AiPendingCommand move = default;
                move.ReadyTick = 1000;
                move.BattalionId = 100 + i;
                move.Kind = CommandKind.Move;
                Assert.True(faction.EnqueuePending(move));
            }

            Assert.Equal(AiConstants.PendingCommandCapacity, faction.PendingCount);
            ulong full = host.Ai.HashState();

            AiPendingCommand spawn = default;
            spawn.ReadyTick = 1;
            spawn.BattalionId = 1;
            spawn.Kind = CommandKind.SpawnBattalion;
            Assert.True(faction.EnqueuePending(spawn));
            Assert.Equal(AiConstants.PendingCommandCapacity, faction.PendingCount);
            Assert.Equal(1, faction.PendingDropped);
            Assert.NotEqual(full, host.Ai.HashState());

            bool foundSpawn = false;
            bool foundReplacedMove = false;
            for (int i = 0; i < faction.PendingCount; i++)
            {
                if (faction.Pending[i].Kind == CommandKind.SpawnBattalion
                    && faction.Pending[i].BattalionId == 1)
                {
                    foundSpawn = true;
                }

                if (faction.Pending[i].BattalionId == 100 + AiConstants.PendingCommandCapacity - 1)
                {
                    foundReplacedMove = true;
                }
            }

            Assert.True(foundSpawn);
            Assert.False(foundReplacedMove);

            AiPendingCommand build = default;
            build.ReadyTick = 9999;
            build.BattalionId = 9999;
            build.Kind = CommandKind.Build;
            Assert.False(faction.EnqueuePending(build));
            Assert.Equal(2, faction.PendingDropped);
            Assert.Equal(AiConstants.PendingCommandCapacity, faction.PendingCount);

            byte[] blob = host.Ai.Save();
            AiMatchHost restored = StartHost();
            restored.Ai.Load(blob);
            Assert.Equal(2, restored.Ai.Owned.FactionAt(0).PendingDropped);
            Assert.Equal(host.Ai.HashState(), restored.Ai.HashState());
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void CaptureOccupancy_ReturnsOnlyEmptyOneFactionOrContested()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            CaptureZoneOccupancyResult empty = sink.OccupancyOf(0, 0, 80000);
            Assert.Equal(CaptureOccupancyKind.Empty, empty.Kind);
            Assert.Equal(0, empty.FactionId);

            sink.Submit(SimCommand.SpawnBattalion(0, 1, 11, 0, 0, 100));
            sink.Submit(SimCommand.SpawnBattalion(0, 2, 12, 0, 0, 100));
            world.Step();
            world.Step();
            world.Step();

            CaptureZoneOccupancyResult contested = sink.OccupancyOf(0, 0, 80000);
            Assert.Equal(CaptureOccupancyKind.Contested, contested.Kind);
            Assert.Equal(0, contested.FactionId);

            CaptureZoneOccupancyResult far = sink.OccupancyOf(5_000_000, 5_000_000, 100);
            Assert.Equal(CaptureOccupancyKind.Empty, far.Kind);
        }

        [Fact]
        public void LoggingCommandSink_CapsTheRecordedLog()
        {
            Assert.Equal(4096, AiConstants.CommandLogCapacity);
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            LoggingCommandSink sink = new LoggingCommandSink(world);
            for (int i = 0; i < 4; i++)
            {
                sink.Submit(SimCommand.SpawnBattalion(0, 1, 20 + i, i, 0, 100));
            }

            Assert.Equal(4, sink.Log.Count);
            Assert.Equal(0, sink.DroppedFromLog);
            Assert.Equal(world.Tick, sink.Log[0].IssuedTick);
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

        internal static byte[] LoadGorgeV1Bytes()
        {
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1Tests.MapFileName);
            return GoldenMapFiles.ReadWrmb(Path.Combine(dir, GoldenMapV1Tests.MapFileName), GoldenMapV1Tests.MapBytes);
        }

        internal static AiMapSnapshot LoadGorgeMap()
        {
            return AiMapSnapshot.FromWrmb(LoadGorgeBytes());
        }
    }
}
